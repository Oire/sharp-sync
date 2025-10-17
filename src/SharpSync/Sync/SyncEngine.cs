using System.Collections.Concurrent;
using System.Diagnostics;
using Oire.SharpSync.Core;

namespace Oire.SharpSync.Sync;

/// <summary>
/// Production-ready sync engine with incremental sync, change detection, and parallel processing
/// Optimized for large file sets and efficient synchronization
/// </summary>
public class SyncEngine : ISyncEngine
{
    private readonly ISyncStorage _localStorage;
    private readonly ISyncStorage _remoteStorage;
    private readonly ISyncDatabase _database;
    private readonly ISyncFilter _filter;
    private readonly IConflictResolver _conflictResolver;
    
    // Configuration
    private readonly int _maxParallelism;
    private readonly bool _useChecksums;
    private readonly TimeSpan _changeDetectionWindow;
    
    // State
    private bool _disposed;
    private readonly SemaphoreSlim _syncSemaphore;
    private CancellationTokenSource? _currentSyncCts;

    /// <summary>
    /// Gets whether the engine is currently synchronizing
    /// </summary>
    public bool IsSynchronizing => _syncSemaphore.CurrentCount == 0;

    /// <summary>
    /// Event raised when sync progress changes
    /// </summary>
    public event EventHandler<SyncProgressEventArgs>? ProgressChanged;

    /// <summary>
    /// Event raised when a conflict is detected
    /// </summary>
    public event EventHandler<FileConflictEventArgs>? ConflictDetected;

    /// <summary>
    /// Creates a new sync engine instance
    /// </summary>
    /// <param name="localStorage">Local storage implementation</param>
    /// <param name="remoteStorage">Remote storage implementation</param>
    /// <param name="database">Sync state database</param>
    /// <param name="filter">File filter for selective sync</param>
    /// <param name="conflictResolver">Conflict resolution strategy</param>
    /// <param name="maxParallelism">Maximum parallel operations (default: 4)</param>
    /// <param name="useChecksums">Whether to use checksums for change detection (default: false)</param>
    /// <param name="changeDetectionWindow">Time window for modification detection (default: 2 seconds)</param>
    public SyncEngine(
        ISyncStorage localStorage,
        ISyncStorage remoteStorage,
        ISyncDatabase database,
        ISyncFilter filter,
        IConflictResolver conflictResolver,
        int maxParallelism = 4,
        bool useChecksums = false,
        TimeSpan? changeDetectionWindow = null)
    {
        _localStorage = localStorage ?? throw new ArgumentNullException(nameof(localStorage));
        _remoteStorage = remoteStorage ?? throw new ArgumentNullException(nameof(remoteStorage));
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _filter = filter ?? throw new ArgumentNullException(nameof(filter));
        _conflictResolver = conflictResolver ?? throw new ArgumentNullException(nameof(conflictResolver));
        
        _maxParallelism = Math.Max(1, maxParallelism);
        _useChecksums = useChecksums;
        _changeDetectionWindow = changeDetectionWindow ?? TimeSpan.FromSeconds(2);
        
        _syncSemaphore = new SemaphoreSlim(1, 1);
    }

    /// <summary>
    /// Simplified constructor with default settings
    /// </summary>
    public SyncEngine(
        ISyncStorage localStorage,
        ISyncStorage remoteStorage,
        ISyncDatabase database,
        ISyncFilter filter,
        IConflictResolver conflictResolver)
        : this(localStorage, remoteStorage, database, filter, conflictResolver, 4, false, null)
    {
    }

    /// <summary>
    /// Performs incremental synchronization
    /// </summary>
    public async Task<SyncResult> SynchronizeAsync(SyncOptions? options = null, CancellationToken cancellationToken = default)
    {
        if (!await _syncSemaphore.WaitAsync(0, cancellationToken))
            throw new InvalidOperationException("Synchronization is already in progress");

        try
        {
            using (_currentSyncCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                var syncToken = _currentSyncCts.Token;
                var result = new SyncResult();
                var sw = Stopwatch.StartNew();

                try
                {
                    // Phase 1: Fast change detection
                    RaiseProgress(new SyncProgress { CurrentItem = "Detecting changes..." }, SyncOperation.Scanning);
                    var changes = await DetectChangesAsync(options, syncToken);
                    
                    if (changes.TotalChanges == 0)
                    {
                        result.Success = true;
                        result.Details = "No changes detected";
                        return result;
                    }
                    
                    // Phase 2: Process changes (respecting dry run mode)
                    RaiseProgress(new SyncProgress { TotalItems = changes.TotalChanges }, SyncOperation.Unknown);
                    
                    if (options?.DryRun != true)
                    {
                        await ProcessChangesAsync(changes, options, result, syncToken);
                        
                        // Phase 3: Update database state
                        await UpdateDatabaseStateAsync(changes, syncToken);
                    }
                    else
                    {
                        // Dry run - just count what would be done
                        result.FilesSynchronized = changes.Additions.Count + changes.Modifications.Count;
                        result.FilesDeleted = changes.Deletions.Count;
                        result.Details = $"Dry run: Would sync {result.FilesSynchronized} files, delete {result.FilesDeleted}";
                    }
                    
                    result.Success = true;
                }
                catch (OperationCanceledException)
                {
                    result.Error = new InvalidOperationException("Synchronization was cancelled");
                    throw;
                }
                catch (Exception ex)
                {
                    result.Error = ex;
                    result.Details = ex.Message;
                }
                finally
                {
                    result.ElapsedTime = sw.Elapsed;
                }

                return result;
            }
        }
        finally
        {
            _currentSyncCts = null;
            _syncSemaphore.Release();
        }
    }

    /// <summary>
    /// Preview what would be synchronized without making changes
    /// </summary>
    public async Task<SyncResult> PreviewSyncAsync(SyncOptions? options = null, CancellationToken cancellationToken = default)
    {
        var previewOptions = options?.Clone() ?? new SyncOptions();
        previewOptions.DryRun = true;
        return await SynchronizeAsync(previewOptions, cancellationToken);
    }

    /// <summary>
    /// Efficient change detection using database state
    /// </summary>
    private async Task<ChangeSet> DetectChangesAsync(SyncOptions? options, CancellationToken cancellationToken)
    {
        var changeSet = new ChangeSet();
        
        // Get all tracked items from database
        var trackedItems = (await _database.GetAllSyncStatesAsync(cancellationToken))
            .ToDictionary(s => s.Path, StringComparer.OrdinalIgnoreCase);

        // Scan local and remote in parallel
        var localScanTask = ScanStorageAsync(_localStorage, trackedItems, true, changeSet, cancellationToken);
        var remoteScanTask = ScanStorageAsync(_remoteStorage, trackedItems, false, changeSet, cancellationToken);
        
        await Task.WhenAll(localScanTask, remoteScanTask);

        // Detect deletions (items in DB but not found in scans)
        foreach (var tracked in trackedItems.Values.Where(t => !changeSet.ProcessedPaths.Contains(t.Path)))
        {
            if (tracked.Status == SyncStatus.Synced || tracked.Status == SyncStatus.LocalModified || tracked.Status == SyncStatus.RemoteModified)
            {
                changeSet.Deletions.Add(new DeletionChange
                {
                    Path = tracked.Path,
                    DeletedLocally = !await _localStorage.ExistsAsync(tracked.Path, cancellationToken),
                    DeletedRemotely = !await _remoteStorage.ExistsAsync(tracked.Path, cancellationToken),
                    TrackedState = tracked
                });
            }
        }

        return changeSet;
    }

    /// <summary>
    /// Scans storage for changes compared to tracked state
    /// </summary>
    private async Task ScanStorageAsync(
        ISyncStorage storage,
        Dictionary<string, SyncState> trackedItems,
        bool isLocal,
        ChangeSet changeSet,
        CancellationToken cancellationToken)
    {
        await ScanDirectoryRecursiveAsync(storage, "", trackedItems, isLocal, changeSet, cancellationToken);
    }

    private async Task ScanDirectoryRecursiveAsync(
        ISyncStorage storage,
        string dirPath,
        Dictionary<string, SyncState> trackedItems,
        bool isLocal,
        ChangeSet changeSet,
        CancellationToken cancellationToken)
    {
        try
        {
            var items = await storage.ListItemsAsync(dirPath, cancellationToken);
            var tasks = new List<Task>();

            foreach (var item in items)
            {
                if (!_filter.ShouldSync(item.Path))
                    continue;

                changeSet.ProcessedPaths.Add(item.Path);

                // Check if item is tracked
                if (trackedItems.TryGetValue(item.Path, out var tracked))
                {
                    // Check for modifications
                    if (await HasChangedAsync(storage, item, tracked, isLocal, cancellationToken))
                    {
                        changeSet.Modifications.Add(new ModificationChange
                        {
                            Path = item.Path,
                            Item = item,
                            IsLocal = isLocal,
                            TrackedState = tracked
                        });
                    }
                }
                else
                {
                    // New item
                    changeSet.Additions.Add(new AdditionChange
                    {
                        Path = item.Path,
                        Item = item,
                        IsLocal = isLocal
                    });
                }

                // Recursively scan directories
                if (item.IsDirectory)
                {
                    tasks.Add(ScanDirectoryRecursiveAsync(storage, item.Path, trackedItems, isLocal, changeSet, cancellationToken));
                }
            }

            if (tasks.Any())
            {
                await Task.WhenAll(tasks);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Log error but continue scanning other directories
            Debug.WriteLine($"Error scanning directory {dirPath}: {ex.Message}");
        }
    }

    /// <summary>
    /// Determines if an item has changed compared to tracked state
    /// </summary>
    private async Task<bool> HasChangedAsync(ISyncStorage storage, SyncItem item, SyncState tracked, bool isLocal, CancellationToken cancellationToken)
    {
        if (isLocal)
        {
            // Check local changes
            if (tracked.LocalModified == null) return true; // Was deleted, now exists
            
            // Use ETag if available (fast)
            if (!string.IsNullOrEmpty(item.ETag) && item.ETag == tracked.LocalHash)
                return false;
            
            // Check modification time (considering detection window)
            if (Math.Abs((item.LastModified - tracked.LocalModified.Value).TotalMilliseconds) > _changeDetectionWindow.TotalMilliseconds)
                return true;
            
            // Check size
            if (item.Size != tracked.LocalSize)
                return true;
            
            // If using checksums, compute and compare
            if (_useChecksums && !item.IsDirectory)
            {
                var hash = await storage.ComputeHashAsync(item.Path, cancellationToken);
                return hash != tracked.LocalHash;
            }
            
            return false;
        }
        else
        {
            // Check remote changes
            if (tracked.RemoteModified == null) return true; // Was deleted, now exists
            
            // Use ETag if available (fast)
            if (!string.IsNullOrEmpty(item.ETag) && item.ETag == tracked.RemoteHash)
                return false;
            
            // Check modification time
            if (Math.Abs((item.LastModified - tracked.RemoteModified.Value).TotalMilliseconds) > _changeDetectionWindow.TotalMilliseconds)
                return true;
            
            // Check size
            if (item.Size != tracked.RemoteSize)
                return true;
            
            // If using checksums, compute and compare
            if (_useChecksums && !item.IsDirectory)
            {
                var hash = await storage.ComputeHashAsync(item.Path, cancellationToken);
                return hash != tracked.RemoteHash;
            }
            
            return false;
        }
    }

    /// <summary>
    /// Processes detected changes with optimized parallel execution
    /// Uses batching and prioritization for maximum efficiency
    /// </summary>
    private async Task ProcessChangesAsync(ChangeSet changes, SyncOptions? options, SyncResult result, CancellationToken cancellationToken)
    {
        var progressCounter = new ProgressCounter();
        var totalChanges = changes.TotalChanges;
        var threadSafeResult = new ThreadSafeSyncResult(result);
        
        // Analyze and prioritize changes
        var actionGroups = AnalyzeAndPrioritizeChanges(changes);
        
        // Process in phases for optimal efficiency
        await ProcessPhase1_DirectoriesAndSmallFilesAsync(actionGroups, threadSafeResult, progressCounter, totalChanges, cancellationToken);
        await ProcessPhase2_LargeFilesAsync(actionGroups, threadSafeResult, progressCounter, totalChanges, cancellationToken);  
        await ProcessPhase3_DeletesAndConflictsAsync(actionGroups, threadSafeResult, progressCounter, totalChanges, cancellationToken);
    }

    /// <summary>
    /// Analyzes and prioritizes changes for optimal parallel processing
    /// </summary>
    private ActionGroups AnalyzeAndPrioritizeChanges(ChangeSet changes)
    {
        const long LargeFileThreshold = 10 * 1024 * 1024; // 10MB
        
        var groups = new ActionGroups();
        
        // Process additions
        foreach (var addition in changes.Additions)
        {
            var action = new SyncAction
            {
                Type = addition.IsLocal ? SyncActionType.Upload : SyncActionType.Download,
                Path = addition.Path,
                LocalItem = addition.IsLocal ? addition.Item : null,
                RemoteItem = !addition.IsLocal ? addition.Item : null,
                Priority = CalculatePriority(addition.Item)
            };
            
            CategorizeAction(action, groups, LargeFileThreshold);
        }
        
        // Process modifications (detect conflicts)
        var modsByPath = changes.Modifications.GroupBy(m => m.Path, StringComparer.OrdinalIgnoreCase);
        foreach (var pathMods in modsByPath)
        {
            var mods = pathMods.ToList();
            if (mods.Count > 1)
            {
                // Both modified - conflict
                var local = mods.First(m => m.IsLocal);
                var remote = mods.First(m => !m.IsLocal);
                
                groups.Conflicts.Add(new SyncAction
                {
                    Type = SyncActionType.Conflict,
                    Path = pathMods.Key,
                    LocalItem = local.Item,
                    RemoteItem = remote.Item,
                    ConflictType = ConflictType.BothModified,
                    Priority = 1000 // High priority for conflicts
                });
            }
            else
            {
                // One-sided modification
                var mod = mods[0];
                var action = new SyncAction
                {
                    Type = mod.IsLocal ? SyncActionType.Upload : SyncActionType.Download,
                    Path = mod.Path,
                    LocalItem = mod.IsLocal ? mod.Item : null,
                    RemoteItem = !mod.IsLocal ? mod.Item : null,
                    Priority = CalculatePriority(mod.Item)
                };
                
                CategorizeAction(action, groups, LargeFileThreshold);
            }
        }
        
        // Process deletions
        foreach (var deletion in changes.Deletions)
        {
            if (deletion.DeletedLocally && deletion.DeletedRemotely)
            {
                // Both deleted - just update DB (will happen in UpdateDatabaseStateAsync)
                continue;
            }
            
            var action = CreateDeletionAction(deletion);
            if (action != null)
            {
                groups.Deletes.Add(action);
            }
        }
        
        // Sort by priority (higher priority first)
        groups.SortByPriority();
        
        return groups;
    }

    private static void CategorizeAction(SyncAction action, ActionGroups groups, long largeFileThreshold)
    {
        var item = action.LocalItem ?? action.RemoteItem;
        
        if (item?.IsDirectory == true)
        {
            groups.Directories.Add(action);
        }
        else if (item?.Size >= largeFileThreshold)
        {
            groups.LargeFiles.Add(action);
        }
        else
        {
            groups.SmallFiles.Add(action);
        }
    }

    private static int CalculatePriority(SyncItem item)
    {
        // Higher number = higher priority
        int priority = 0;
        
        // Directories first
        if (item.IsDirectory)
            priority += 1000;
            
        // Smaller files first (within same category)
        priority += (int)(1000000 - Math.Min(item.Size / 1024, 999999));
        
        // Recently modified files first
        var ageHours = (DateTime.UtcNow - item.LastModified).TotalHours;
        priority += Math.Max(0, 100 - (int)ageHours);
        
        return priority;
    }

    private SyncAction? CreateDeletionAction(DeletionChange deletion)
    {
        if (deletion.DeletedLocally)
        {
            // Check if remote was modified - potential conflict
            if (deletion.TrackedState.RemoteModified > deletion.TrackedState.LocalModified)
            {
                return new SyncAction
                {
                    Type = SyncActionType.Conflict,
                    Path = deletion.Path,
                    ConflictType = ConflictType.DeletedLocallyModifiedRemotely,
                    Priority = 1000 // High priority for conflicts
                };
            }
            else
            {
                return new SyncAction
                {
                    Type = SyncActionType.DeleteRemote,
                    Path = deletion.Path,
                    Priority = 500 // Medium priority for deletes
                };
            }
        }
        else
        {
            // Check if local was modified - potential conflict
            if (deletion.TrackedState.LocalModified > deletion.TrackedState.RemoteModified)
            {
                return new SyncAction
                {
                    Type = SyncActionType.Conflict,
                    Path = deletion.Path,
                    ConflictType = ConflictType.ModifiedLocallyDeletedRemotely,
                    Priority = 1000 // High priority for conflicts
                };
            }
            else
            {
                return new SyncAction
                {
                    Type = SyncActionType.DeleteLocal,
                    Path = deletion.Path,
                    Priority = 500 // Medium priority for deletes
                };
            }
        }
    }

    /// <summary>
    /// Phase 1: Process directories and small files with maximum parallelism
    /// These are typically fast operations that benefit from high concurrency
    /// </summary>
    private async Task ProcessPhase1_DirectoriesAndSmallFilesAsync(
        ActionGroups actionGroups,
        ThreadSafeSyncResult result,
        ProgressCounter progressCounter,
        int totalChanges,
        CancellationToken cancellationToken)
    {
        var allSmallActions = actionGroups.Directories.Concat(actionGroups.SmallFiles).ToList();
        
        if (!allSmallActions.Any()) return;
        
        // Use high parallelism for small operations
        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = _maxParallelism * 2, // Allow more concurrency for small files
            CancellationToken = cancellationToken
        };
        
        await Parallel.ForEachAsync(allSmallActions, parallelOptions, async (action, ct) =>
        {
            try
            {
                await ProcessActionAsync(action, result, ct);
                
                var newCount = progressCounter.Increment();
                
                // Report progress less frequently to reduce overhead
                if (newCount % 10 == 0 || action.LocalItem?.IsDirectory == true)
                {
                    RaiseProgress(new SyncProgress
                    {
                        ProcessedItems = newCount,
                        TotalItems = totalChanges,
                        CurrentItem = action.Path
                    }, GetOperationType(action.Type));
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                result.IncrementFilesSkipped();
                Debug.WriteLine($"Error processing {action.Path}: {ex.Message}");
            }
        });
    }

    /// <summary>
    /// Phase 2: Process large files with controlled parallelism and progress reporting
    /// These operations are bandwidth-intensive and need careful resource management
    /// </summary>
    private async Task ProcessPhase2_LargeFilesAsync(
        ActionGroups actionGroups,
        ThreadSafeSyncResult result,
        ProgressCounter progressCounter,
        int totalChanges,
        CancellationToken cancellationToken)
    {
        if (!actionGroups.LargeFiles.Any()) return;
        
        // Use reduced parallelism for large files to avoid bandwidth saturation
        var semaphore = new SemaphoreSlim(Math.Max(1, _maxParallelism / 2), Math.Max(1, _maxParallelism / 2));
        var tasks = new List<Task>();
        
        foreach (var action in actionGroups.LargeFiles)
        {
            tasks.Add(ProcessLargeFileAsync(action, result, progressCounter, totalChanges, semaphore, cancellationToken));
        }
        
        await Task.WhenAll(tasks);
        semaphore.Dispose();
    }

    private async Task ProcessLargeFileAsync(
        SyncAction action,
        ThreadSafeSyncResult result,
        ProgressCounter progressCounter,
        int totalChanges,
        SemaphoreSlim semaphore,
        CancellationToken cancellationToken)
    {
        await semaphore.WaitAsync(cancellationToken);
        try
        {
            // Report start of large file processing
            RaiseProgress(new SyncProgress
            {
                ProcessedItems = progressCounter.Value,
                TotalItems = totalChanges,
                CurrentItem = action.Path
            }, GetOperationType(action.Type));
            
            await ProcessActionAsync(action, result, cancellationToken);
            
            var newCount = progressCounter.Increment();
            
            // Always report progress for large files
            RaiseProgress(new SyncProgress
            {
                ProcessedItems = newCount,
                TotalItems = totalChanges,
                CurrentItem = action.Path
            }, GetOperationType(action.Type));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            result.IncrementFilesSkipped();
            Debug.WriteLine($"Error processing large file {action.Path}: {ex.Message}");
        }
        finally
        {
            semaphore.Release();
        }
    }

    /// <summary>
    /// Phase 3: Process deletes and conflicts sequentially for safety
    /// These operations need careful ordering and error handling
    /// </summary>
    private async Task ProcessPhase3_DeletesAndConflictsAsync(
        ActionGroups actionGroups,
        ThreadSafeSyncResult result,
        ProgressCounter progressCounter,
        int totalChanges,
        CancellationToken cancellationToken)
    {
        // Process conflicts first (they might resolve to deletes)
        foreach (var action in actionGroups.Conflicts)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                await ProcessActionAsync(action, result, cancellationToken);
                
                var newCount = progressCounter.Increment();
                RaiseProgress(new SyncProgress
                {
                    ProcessedItems = newCount,
                    TotalItems = totalChanges,
                    CurrentItem = action.Path
                }, SyncOperation.ResolvingConflict);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                result.IncrementFilesSkipped();
                Debug.WriteLine($"Error resolving conflict for {action.Path}: {ex.Message}");
            }
        }
        
        // Process deletes last (in reverse depth order to delete children before parents)
        var sortedDeletes = actionGroups.Deletes
            .OrderByDescending(a => a.Path.Count(c => c == '/'))
            .ToList();
        
        foreach (var action in sortedDeletes)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                await ProcessActionAsync(action, result, cancellationToken);
                
                var newCount = progressCounter.Increment();
                RaiseProgress(new SyncProgress
                {
                    ProcessedItems = newCount,
                    TotalItems = totalChanges,
                    CurrentItem = action.Path
                }, SyncOperation.Deleting);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                result.IncrementFilesSkipped();
                Debug.WriteLine($"Error deleting {action.Path}: {ex.Message}");
            }
        }
    }

    private async Task ProcessActionAsync(SyncAction action, ThreadSafeSyncResult result, CancellationToken cancellationToken)
    {
        switch (action.Type)
        {
            case SyncActionType.Download:
                await DownloadFileAsync(action, result, cancellationToken);
                break;
                
            case SyncActionType.Upload:
                await UploadFileAsync(action, result, cancellationToken);
                break;
                
            case SyncActionType.DeleteLocal:
                await DeleteLocalAsync(action, result, cancellationToken);
                break;
                
            case SyncActionType.DeleteRemote:
                await DeleteRemoteAsync(action, result, cancellationToken);
                break;
                
            case SyncActionType.Conflict:
                await ResolveConflictAsync(action, result, cancellationToken);
                break;
        }
    }

    private async Task DownloadFileAsync(SyncAction action, ThreadSafeSyncResult result, CancellationToken cancellationToken)
    {
        if (action.RemoteItem!.IsDirectory)
        {
            await _localStorage.CreateDirectoryAsync(action.Path, cancellationToken);
        }
        else
        {
            using var remoteStream = await _remoteStorage.ReadFileAsync(action.Path, cancellationToken);
            await _localStorage.WriteFileAsync(action.Path, remoteStream, cancellationToken);
        }
        
        result.IncrementFilesSynchronized();
    }

    private async Task UploadFileAsync(SyncAction action, ThreadSafeSyncResult result, CancellationToken cancellationToken)
    {
        if (action.LocalItem!.IsDirectory)
        {
            await _remoteStorage.CreateDirectoryAsync(action.Path, cancellationToken);
        }
        else
        {
            using var localStream = await _localStorage.ReadFileAsync(action.Path, cancellationToken);
            await _remoteStorage.WriteFileAsync(action.Path, localStream, cancellationToken);
        }
        
        result.IncrementFilesSynchronized();
    }

    private async Task DeleteLocalAsync(SyncAction action, ThreadSafeSyncResult result, CancellationToken cancellationToken)
    {
        await _localStorage.DeleteAsync(action.Path, cancellationToken);
        result.IncrementFilesDeleted();
    }

    private async Task DeleteRemoteAsync(SyncAction action, ThreadSafeSyncResult result, CancellationToken cancellationToken)
    {
        await _remoteStorage.DeleteAsync(action.Path, cancellationToken);
        result.IncrementFilesDeleted();
    }

    private async Task ResolveConflictAsync(SyncAction action, ThreadSafeSyncResult result, CancellationToken cancellationToken)
    {
        // Get full item details if needed
        action.LocalItem ??= await _localStorage.GetItemAsync(action.Path, cancellationToken);
        action.RemoteItem ??= await _remoteStorage.GetItemAsync(action.Path, cancellationToken);
        
        var conflictArgs = new FileConflictEventArgs(
            action.Path,
            action.LocalItem,
            action.RemoteItem,
            action.ConflictType);
        
        // Raise event for UI
        ConflictDetected?.Invoke(this, conflictArgs);
        
        // Get resolution
        var resolution = await _conflictResolver.ResolveConflictAsync(conflictArgs, cancellationToken);
        
        // Apply resolution
        switch (resolution)
        {
            case ConflictResolution.UseLocal:
                if (action.LocalItem != null)
                    await UploadFileAsync(action, result, cancellationToken);
                break;
                
            case ConflictResolution.UseRemote:
                if (action.RemoteItem != null)
                    await DownloadFileAsync(action, result, cancellationToken);
                break;
                
            case ConflictResolution.Skip:
                result.IncrementFilesSkipped();
                break;
                
            case ConflictResolution.RenameLocal:
                // TODO: Implement rename logic
                result.IncrementFilesConflicted();
                break;
                
            case ConflictResolution.RenameRemote:
                // TODO: Implement rename logic
                result.IncrementFilesConflicted();
                break;
                
            default:
                result.IncrementFilesConflicted();
                break;
        }
    }

    private async Task UpdateDatabaseStateAsync(ChangeSet changes, CancellationToken cancellationToken)
    {
        // Update database with new sync states
        var updates = new List<Task>();
        
        // Update additions
        foreach (var addition in changes.Additions)
        {
            var state = new SyncState
            {
                Path = addition.Path,
                IsDirectory = addition.Item.IsDirectory,
                Status = SyncStatus.Synced,
                LastSyncTime = DateTime.UtcNow
            };
            
            if (addition.IsLocal)
            {
                state.LocalHash = addition.Item.ETag ?? await _localStorage.ComputeHashAsync(addition.Path, cancellationToken);
                state.LocalSize = addition.Item.Size;
                state.LocalModified = addition.Item.LastModified;
                state.RemoteHash = state.LocalHash;
                state.RemoteSize = state.LocalSize;
                state.RemoteModified = state.LocalModified;
            }
            else
            {
                state.RemoteHash = addition.Item.ETag ?? await _remoteStorage.ComputeHashAsync(addition.Path, cancellationToken);
                state.RemoteSize = addition.Item.Size;
                state.RemoteModified = addition.Item.LastModified;
                state.LocalHash = state.RemoteHash;
                state.LocalSize = state.RemoteSize;
                state.LocalModified = state.RemoteModified;
            }
            
            updates.Add(_database.UpdateSyncStateAsync(state, cancellationToken));
        }
        
        // Update modifications
        foreach (var mod in changes.Modifications)
        {
            var state = mod.TrackedState;
            state.Status = SyncStatus.Synced;
            state.LastSyncTime = DateTime.UtcNow;
            
            if (mod.IsLocal)
            {
                state.LocalHash = mod.Item.ETag ?? await _localStorage.ComputeHashAsync(mod.Path, cancellationToken);
                state.LocalSize = mod.Item.Size;
                state.LocalModified = mod.Item.LastModified;
                state.RemoteHash = state.LocalHash;
                state.RemoteSize = state.LocalSize;
                state.RemoteModified = state.LocalModified;
            }
            else
            {
                state.RemoteHash = mod.Item.ETag ?? await _remoteStorage.ComputeHashAsync(mod.Path, cancellationToken);
                state.RemoteSize = mod.Item.Size;
                state.RemoteModified = mod.Item.LastModified;
                state.LocalHash = state.RemoteHash;
                state.LocalSize = state.RemoteSize;
                state.LocalModified = state.RemoteModified;
            }
            
            updates.Add(_database.UpdateSyncStateAsync(state, cancellationToken));
        }
        
        // Update deletions
        foreach (var deletion in changes.Deletions)
        {
            if (deletion.DeletedLocally && deletion.DeletedRemotely)
            {
                // Both deleted - remove from DB
                updates.Add(_database.DeleteSyncStateAsync(deletion.Path, cancellationToken));
            }
            else
            {
                // One-sided deletion that was synced
                updates.Add(_database.DeleteSyncStateAsync(deletion.Path, cancellationToken));
            }
        }
        
        await Task.WhenAll(updates);
    }

    private SyncOperation GetOperationType(SyncActionType actionType)
    {
        return actionType switch
        {
            SyncActionType.Download => SyncOperation.Downloading,
            SyncActionType.Upload => SyncOperation.Uploading,
            SyncActionType.DeleteLocal or SyncActionType.DeleteRemote => SyncOperation.Deleting,
            SyncActionType.Conflict => SyncOperation.ResolvingConflict,
            _ => SyncOperation.Unknown
        };
    }

    private void RaiseProgress(SyncProgress progress, SyncOperation operation)
    {
        ProgressChanged?.Invoke(this, new SyncProgressEventArgs(progress, progress.CurrentItem, operation));
    }

    public async Task<DatabaseStats> GetStatsAsync(CancellationToken cancellationToken = default)
    {
        return await _database.GetStatsAsync(cancellationToken);
    }

    public async Task ResetSyncStateAsync(CancellationToken cancellationToken = default)
    {
        await _database.ClearAsync(cancellationToken);
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _currentSyncCts?.Cancel();
            _syncSemaphore?.Dispose();
            _disposed = true;
        }
    }
}

#region Change Tracking Types

internal class ChangeSet
{
    public List<AdditionChange> Additions { get; } = new();
    public List<ModificationChange> Modifications { get; } = new();
    public List<DeletionChange> Deletions { get; } = new();
    public HashSet<string> ProcessedPaths { get; } = new(StringComparer.OrdinalIgnoreCase);
    
    public int TotalChanges => Additions.Count + Modifications.Count + Deletions.Count;
}

internal interface IChange
{
    string Path { get; }
}

internal class AdditionChange : IChange
{
    public string Path { get; set; } = string.Empty;
    public SyncItem Item { get; set; } = new();
    public bool IsLocal { get; set; }
}

internal class ModificationChange : IChange
{
    public string Path { get; set; } = string.Empty;
    public SyncItem Item { get; set; } = new();
    public bool IsLocal { get; set; }
    public SyncState TrackedState { get; set; } = new();
}

internal class DeletionChange : IChange
{
    public string Path { get; set; } = string.Empty;
    public bool DeletedLocally { get; set; }
    public bool DeletedRemotely { get; set; }
    public SyncState TrackedState { get; set; } = new();
}

internal enum SyncActionType
{
    Download,
    Upload,
    DeleteLocal,
    DeleteRemote,
    Conflict
}

internal class SyncAction
{
    public SyncActionType Type { get; set; }
    public string Path { get; set; } = string.Empty;
    public SyncItem? LocalItem { get; set; }
    public SyncItem? RemoteItem { get; set; }
    public ConflictType ConflictType { get; set; }
    public int Priority { get; set; }
}

/// <summary>
/// Organizes sync actions into optimized processing groups
/// </summary>
internal class ActionGroups
{
    public List<SyncAction> Directories { get; } = new();
    public List<SyncAction> SmallFiles { get; } = new();
    public List<SyncAction> LargeFiles { get; } = new();
    public List<SyncAction> Deletes { get; } = new();
    public List<SyncAction> Conflicts { get; } = new();
    
    public void SortByPriority()
    {
        Directories.Sort((a, b) => b.Priority.CompareTo(a.Priority));
        SmallFiles.Sort((a, b) => b.Priority.CompareTo(a.Priority));
        LargeFiles.Sort((a, b) => b.Priority.CompareTo(a.Priority));
        Deletes.Sort((a, b) => b.Priority.CompareTo(a.Priority));
        Conflicts.Sort((a, b) => b.Priority.CompareTo(a.Priority));
    }
    
    public int TotalActions => Directories.Count + SmallFiles.Count + LargeFiles.Count + Deletes.Count + Conflicts.Count;
}

#endregion