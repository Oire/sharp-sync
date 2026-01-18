using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Oire.SharpSync.Core;
using Oire.SharpSync.Logging;
using Oire.SharpSync.Storage;

namespace Oire.SharpSync.Sync;

/// <summary>
/// Sync engine with incremental sync, change detection, and parallel processing
/// Optimized for large file sets and efficient synchronization
/// </summary>
public class SyncEngine: ISyncEngine {
    private readonly ISyncStorage _localStorage;
    private readonly ISyncStorage _remoteStorage;
    private readonly ISyncDatabase _database;
    private readonly ISyncFilter _filter;
    private readonly IConflictResolver _conflictResolver;
    private readonly ILogger<SyncEngine> _logger;

    // Configuration
    private readonly int _maxParallelism;
    private readonly bool _useChecksums;
    private readonly TimeSpan _changeDetectionWindow;

    // State
    private bool _disposed;
    private readonly SemaphoreSlim _syncSemaphore;
    private CancellationTokenSource? _currentSyncCts;
    private long? _currentMaxBytesPerSecond;
    private SyncOptions? _currentOptions;

    // Pause/Resume state
    private readonly ManualResetEventSlim _pauseEvent = new(true); // Initially not paused (signaled)
    private volatile SyncEngineState _state = SyncEngineState.Idle;
    private readonly object _stateLock = new();
    private SyncProgress? _pausedProgress;
    private TaskCompletionSource? _pauseCompletionSource;

    /// <summary>
    /// Gets whether the engine is currently synchronizing
    /// </summary>
    public bool IsSynchronizing => _syncSemaphore.CurrentCount == 0;

    /// <summary>
    /// Gets whether the engine is currently paused
    /// </summary>
    public bool IsPaused => _state == SyncEngineState.Paused;

    /// <summary>
    /// Gets the current state of the sync engine
    /// </summary>
    public SyncEngineState State => _state;

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
    /// <param name="logger">Optional logger for diagnostic output</param>
    /// <param name="maxParallelism">Maximum parallel operations (default: 4)</param>
    /// <param name="useChecksums">Whether to use checksums for change detection (default: false)</param>
    /// <param name="changeDetectionWindow">Time window for modification detection (default: 2 seconds)</param>
    public SyncEngine(
        ISyncStorage localStorage,
        ISyncStorage remoteStorage,
        ISyncDatabase database,
        ISyncFilter filter,
        IConflictResolver conflictResolver,
        ILogger<SyncEngine>? logger = null,
        int maxParallelism = 4,
        bool useChecksums = false,
        TimeSpan? changeDetectionWindow = null
    ) {
        ArgumentNullException.ThrowIfNull(localStorage);
        ArgumentNullException.ThrowIfNull(remoteStorage);
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(conflictResolver);

        _localStorage = localStorage;
        _remoteStorage = remoteStorage;
        _database = database;
        _filter = filter;
        _conflictResolver = conflictResolver;
        _logger = logger ?? NullLogger<SyncEngine>.Instance;

        _maxParallelism = Math.Max(1, maxParallelism);
        _useChecksums = useChecksums;
        _changeDetectionWindow = changeDetectionWindow ?? TimeSpan.FromSeconds(2);

        _syncSemaphore = new SemaphoreSlim(1, 1);
    }


    /// <summary>
    /// Performs incremental synchronization between local and remote storage.
    /// </summary>
    /// <param name="options">Optional synchronization options including dry run mode and conflict resolution settings.</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
    /// <returns>A <see cref="SyncResult"/> containing synchronization statistics and any errors that occurred.</returns>
    /// <exception cref="ObjectDisposedException">Thrown when the sync engine has been disposed.</exception>
    /// <exception cref="InvalidOperationException">Thrown when synchronization is already in progress.</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled.</exception>
    public async Task<SyncResult> SynchronizeAsync(SyncOptions? options = null, CancellationToken cancellationToken = default) {
        if (_disposed) {
            throw new ObjectDisposedException(nameof(SyncEngine));
        }

        if (!await _syncSemaphore.WaitAsync(0, cancellationToken)) {
            throw new InvalidOperationException("Synchronization is already in progress");
        }

        try {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _currentSyncCts = linkedCts;
            _currentMaxBytesPerSecond = options?.MaxBytesPerSecond;
            _currentOptions = options;
            var syncToken = linkedCts.Token;
            var result = new SyncResult();
            var sw = Stopwatch.StartNew();

            // Set state to Running
            lock (_stateLock) {
                _state = SyncEngineState.Running;
                _pauseEvent.Set(); // Ensure not paused at start
            }

            try {
                // Phase 1: Fast change detection
                RaiseProgress(new SyncProgress { CurrentItem = "Detecting changes..." }, SyncOperation.Scanning);
                var changes = await DetectChangesAsync(options, syncToken);

                if (changes.TotalChanges == 0) {
                    result.Success = true;
                    result.Details = "No changes detected";
                    return result;
                }

                // Phase 2: Process changes (respecting dry run mode)
                RaiseProgress(new SyncProgress { TotalItems = changes.TotalChanges }, SyncOperation.Unknown);

                if (options?.DryRun != true) {
                    await ProcessChangesAsync(changes, options, result, syncToken);

                    // Phase 3: Update database state
                    await UpdateDatabaseStateAsync(changes, syncToken);
                } else {
                    // Dry run - just count what would be done
                    result.FilesSynchronized = changes.Additions.Count + changes.Modifications.Count;
                    result.FilesDeleted = changes.Deletions.Count;
                    result.Details = $"Dry run: Would sync {result.FilesSynchronized} files, delete {result.FilesDeleted}";
                }

                result.Success = true;
            } catch (OperationCanceledException) {
                result.Error = new InvalidOperationException("Synchronization was cancelled");
                throw;
            } catch (Exception ex) {
                result.Error = ex;
                result.Details = ex.Message;
            } finally {
                result.ElapsedTime = sw.Elapsed;
            }

            return result;
        } finally {
            // Reset state to Idle
            lock (_stateLock) {
                _state = SyncEngineState.Idle;
                _pauseEvent.Set(); // Ensure not paused
                _pausedProgress = null;
                _pauseCompletionSource?.TrySetResult(); // Complete any pending pause
                _pauseCompletionSource = null;
            }
            _currentSyncCts = null;
            _currentMaxBytesPerSecond = null;
            _currentOptions = null;
            _syncSemaphore.Release();
        }
    }

    /// <summary>
    /// Previews what would be synchronized without making any actual changes (dry run mode).
    /// </summary>
    /// <param name="options">Optional synchronization options. DryRun will be forced to true.</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
    /// <returns>A <see cref="SyncResult"/> containing information about what changes would be made.</returns>
    /// <remarks>
    /// This method is useful for showing users what changes will be made before actually synchronizing.
    /// It performs full change detection but does not modify any files or the sync state database.
    /// </remarks>
    public async Task<SyncResult> PreviewSyncAsync(SyncOptions? options = null, CancellationToken cancellationToken = default) {
        var previewOptions = options?.Clone() ?? new SyncOptions();
        previewOptions.DryRun = true;
        return await SynchronizeAsync(previewOptions, cancellationToken);
    }

    /// <summary>
    /// Gets a detailed plan of synchronization actions that will be performed.
    /// </summary>
    /// <param name="options">Optional synchronization options including dry run mode and conflict resolution settings.</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
    /// <returns>A detailed <see cref="SyncPlan"/> containing all planned actions with file-level details.</returns>
    /// <exception cref="ObjectDisposedException">Thrown when the sync engine has been disposed.</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled.</exception>
    /// <remarks>
    /// This method performs change detection and analyzes what actions would be taken during synchronization,
    /// without actually modifying any files. It returns a rich plan object that desktop clients can use to show
    /// users a detailed preview including file names, sizes, action types, and conflicts before synchronization begins.
    /// </remarks>
    public async Task<SyncPlan> GetSyncPlanAsync(SyncOptions? options = null, CancellationToken cancellationToken = default) {
        if (_disposed) {
            throw new ObjectDisposedException(nameof(SyncEngine));
        }

        // Immediately honor a pre-cancelled token
        cancellationToken.ThrowIfCancellationRequested();

        try {
            // Detect changes
            RaiseProgress(new SyncProgress { CurrentItem = "Analyzing changes..." }, SyncOperation.Scanning);
            var changes = await DetectChangesAsync(options, cancellationToken);

            // Check cancellation after detection
            cancellationToken.ThrowIfCancellationRequested();

            if (changes.TotalChanges == 0) {
                return new SyncPlan { Actions = Array.Empty<SyncPlanAction>() };
            }

            // Analyze and prioritize changes
            var actionGroups = AnalyzeAndPrioritizeChanges(changes);

            // Convert internal actions to public plan actions
            var planActions = new List<SyncPlanAction>();

            // Combine all action groups
            var allActions = actionGroups.Directories
                .Concat(actionGroups.SmallFiles)
                .Concat(actionGroups.LargeFiles)
                .Concat(actionGroups.Deletes)
                .Concat(actionGroups.Conflicts)
                .OrderByDescending(a => a.Priority);

            var willCreatePlaceholders = options?.CreateVirtualFilePlaceholders is true;

            foreach (var action in allActions) {
                cancellationToken.ThrowIfCancellationRequested();

                var item = action.LocalItem ?? action.RemoteItem;
                var isDownload = action.Type == SyncActionType.Download;
                var isFile = item?.IsDirectory is false;

                planActions.Add(new SyncPlanAction {
                    ActionType = action.Type,
                    Path = action.Path,
                    IsDirectory = item?.IsDirectory ?? false,
                    Size = item?.Size ?? 0,
                    LastModified = item?.LastModified,
                    ConflictType = action.Type == SyncActionType.Conflict ? action.ConflictType : null,
                    Priority = action.Priority,
                    WillCreateVirtualPlaceholder = willCreatePlaceholders && isDownload && isFile,
                    CurrentVirtualState = item?.VirtualState ?? VirtualFileState.None
                });
            }

            return new SyncPlan {
                Actions = planActions
            };
        } catch (OperationCanceledException) {
            throw;
        } catch (Exception) {
            // Return empty plan on error
            return new SyncPlan { Actions = Array.Empty<SyncPlanAction>() };
        }
    }

    /// <summary>
    /// Efficient change detection using database state
    /// </summary>
    private async Task<ChangeSet> DetectChangesAsync(SyncOptions? options, CancellationToken cancellationToken) {
        var changeSet = new ChangeSet();

        // Get all tracked items from database
        var trackedItems = (await _database.GetAllSyncStatesAsync(cancellationToken))
            .ToDictionary(s => s.Path, StringComparer.OrdinalIgnoreCase);

        // Scan local and remote in parallel
        var localScanTask = ScanStorageAsync(_localStorage, trackedItems, true, changeSet, cancellationToken);
        var remoteScanTask = ScanStorageAsync(_remoteStorage, trackedItems, false, changeSet, cancellationToken);

        await Task.WhenAll(localScanTask, remoteScanTask);

        // Detect deletions by comparing DB state with current storage state
        foreach (var tracked in trackedItems.Values) {
            if (tracked.Status == SyncStatus.Synced || tracked.Status == SyncStatus.LocalModified || tracked.Status == SyncStatus.RemoteModified) {
                var existsLocally = changeSet.LocalPaths.Contains(tracked.Path);
                var existsRemotely = changeSet.RemotePaths.Contains(tracked.Path);

                // If item was in DB but is now missing from one or both sides, it's a deletion
                if (!existsLocally || !existsRemotely) {
                    changeSet.Deletions.Add(new DeletionChange {
                        Path = tracked.Path,
                        DeletedLocally = !existsLocally,
                        DeletedRemotely = !existsRemotely,
                        TrackedState = tracked
                    });
                }
            }
        }

        // Handle DeleteExtraneous option - delete files that exist on remote but not locally
        if (options?.DeleteExtraneous == true) {
            await DetectExtraneousFilesAsync(changeSet, cancellationToken);
        }

        return changeSet;
    }

    /// <summary>
    /// Detects files that exist on remote but not locally (extraneous files) and marks them for deletion
    /// </summary>
    private async Task DetectExtraneousFilesAsync(ChangeSet changeSet, CancellationToken cancellationToken) {
        // Collect all local paths (both files and directories)
        var localPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var addition in changeSet.Additions.Where(a => a.IsLocal)) {
            localPaths.Add(addition.Path);
        }
        foreach (var modification in changeSet.Modifications.Where(m => m.IsLocal)) {
            localPaths.Add(modification.Path);
        }

        // Check all remote items to see if they exist locally
        foreach (var addition in changeSet.Additions.Where(a => !a.IsLocal).ToList()) {
            if (!localPaths.Contains(addition.Path)) {
                // Remote file exists but no corresponding local file - mark for deletion
                changeSet.Additions.Remove(addition);
                changeSet.Deletions.Add(new DeletionChange {
                    Path = addition.Path,
                    DeletedLocally = true,
                    DeletedRemotely = false,
                    TrackedState = new SyncState { Path = addition.Path, Status = SyncStatus.RemoteNew }
                });
            }
        }

        // Check remote modifications too
        foreach (var modification in changeSet.Modifications.Where(m => !m.IsLocal).ToList()) {
            if (!localPaths.Contains(modification.Path) && !await _localStorage.ExistsAsync(modification.Path, cancellationToken)) {
                // Remote file modified but no local file - mark for deletion
                changeSet.Modifications.Remove(modification);
                changeSet.Deletions.Add(new DeletionChange {
                    Path = modification.Path,
                    DeletedLocally = true,
                    DeletedRemotely = false,
                    TrackedState = modification.TrackedState
                });
            }
        }
    }

    /// <summary>
    /// Scans storage for changes compared to tracked state
    /// </summary>
    private async Task ScanStorageAsync(
        ISyncStorage storage,
        Dictionary<string, SyncState> trackedItems,
        bool isLocal,
        ChangeSet changeSet,
        CancellationToken cancellationToken
    ) {
        await ScanDirectoryRecursiveAsync(storage, "", trackedItems, isLocal, changeSet, cancellationToken);
    }

    private async Task ScanDirectoryRecursiveAsync(
        ISyncStorage storage,
        string dirPath,
        Dictionary<string, SyncState> trackedItems,
        bool isLocal,
        ChangeSet changeSet,
        CancellationToken cancellationToken
    ) {
        try {
            var items = await storage.ListItemsAsync(dirPath, cancellationToken);
            var tasks = new List<Task>();

            foreach (var item in items) {
                if (!_filter.ShouldSync(item.Path)) {
                    continue;
                }

                changeSet.ProcessedPaths.Add(item.Path);

                // Track which side the item exists on
                if (isLocal) {
                    changeSet.LocalPaths.Add(item.Path);
                } else {
                    changeSet.RemotePaths.Add(item.Path);
                }

                // Check if item is tracked
                if (trackedItems.TryGetValue(item.Path, out var tracked)) {
                    // Check for modifications
                    if (await HasChangedAsync(storage, item, tracked, isLocal, cancellationToken)) {
                        changeSet.Modifications.Add(new ModificationChange {
                            Path = item.Path,
                            Item = item,
                            IsLocal = isLocal,
                            TrackedState = tracked
                        });
                    }
                } else {
                    // New item
                    changeSet.Additions.Add(new AdditionChange {
                        Path = item.Path,
                        Item = item,
                        IsLocal = isLocal
                    });
                }

                // Recursively scan directories
                if (item.IsDirectory) {
                    tasks.Add(ScanDirectoryRecursiveAsync(storage, item.Path, trackedItems, isLocal, changeSet, cancellationToken));
                }
            }

            if (tasks.Count != 0) {
                await Task.WhenAll(tasks);
            }
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            // Log error but continue scanning other directories
            _logger.DirectoryScanError(ex, dirPath);
        }
    }

    /// <summary>
    /// Determines if an item has changed compared to tracked state
    /// </summary>
    private async Task<bool> HasChangedAsync(
        ISyncStorage storage,
        SyncItem item,
        SyncState tracked,
        bool isLocal,
        CancellationToken cancellationToken
    ) {
        if (isLocal) {
            // Check local changes
            if (tracked.LocalModified is null) {
                return true; // Was deleted, now exists
            }

            // Use ETag if available (fast)
            if (!string.IsNullOrEmpty(item.ETag) && item.ETag == tracked.LocalHash) {
                return false;
            }

            // Check modification time (considering detection window)
            if (Math.Abs((item.LastModified - tracked.LocalModified.Value).TotalMilliseconds) > _changeDetectionWindow.TotalMilliseconds) {
                return true;
            }

            // Check size
            if (item.Size != tracked.LocalSize) {
                return true;
            }

            // If using checksums, compute and compare
            if (_useChecksums && !item.IsDirectory) {
                var hash = await storage.ComputeHashAsync(item.Path, cancellationToken);
                return hash != tracked.LocalHash;
            }

            return false;
        } else {
            // Check remote changes
            if (tracked.RemoteModified is null) {
                return true; // Was deleted, now exists
            }

            // Use ETag if available (fast)
            if (!string.IsNullOrEmpty(item.ETag) && item.ETag == tracked.RemoteHash) {
                return false;
            }

            // Check modification time
            if (Math.Abs((item.LastModified - tracked.RemoteModified.Value).TotalMilliseconds) > _changeDetectionWindow.TotalMilliseconds) {
                return true;
            }

            // Check size
            if (item.Size != tracked.RemoteSize) {
                return true;
            }

            // If using checksums, compute and compare
            if (_useChecksums && !item.IsDirectory) {
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
    private async Task ProcessChangesAsync(ChangeSet changes, SyncOptions? options, SyncResult result, CancellationToken cancellationToken) {
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
    private static ActionGroups AnalyzeAndPrioritizeChanges(ChangeSet changes) {
        const long LargeFileThreshold = 10 * 1024 * 1024; // 10MB

        var groups = new ActionGroups();

        // Process additions
        foreach (var addition in changes.Additions) {
            var action = new SyncAction {
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
        foreach (var pathMods in modsByPath) {
            var mods = pathMods.ToList();
            if (mods.Count > 1) {
                // Both modified - conflict
                var local = mods.First(m => m.IsLocal);
                var remote = mods.First(m => !m.IsLocal);

                groups.Conflicts.Add(new SyncAction {
                    Type = SyncActionType.Conflict,
                    Path = pathMods.Key,
                    LocalItem = local.Item,
                    RemoteItem = remote.Item,
                    ConflictType = ConflictType.BothModified,
                    Priority = 1000 // High priority for conflicts
                });
            } else {
                // One-sided modification
                var mod = mods[0];
                var action = new SyncAction {
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
        foreach (var deletion in changes.Deletions) {
            if (deletion.DeletedLocally && deletion.DeletedRemotely) {
                // Both deleted - just update DB (will happen in UpdateDatabaseStateAsync)
                continue;
            }

            var action = CreateDeletionAction(deletion);
            if (action is not null) {
                groups.Deletes.Add(action);
            }
        }

        // Sort by priority (higher priority first)
        groups.SortByPriority();

        return groups;
    }

    private static void CategorizeAction(SyncAction action, ActionGroups groups, long largeFileThreshold) {
        var item = action.LocalItem ?? action.RemoteItem;

        if (item?.IsDirectory == true) {
            groups.Directories.Add(action);
        } else if (item?.Size >= largeFileThreshold) {
            groups.LargeFiles.Add(action);
        } else {
            groups.SmallFiles.Add(action);
        }
    }

    private static int CalculatePriority(SyncItem item) {
        // Higher number = higher priority
        int priority = 0;

        // Directories first
        if (item.IsDirectory) {
            priority += 1000;
        }

        // Smaller files first (within same category)
        priority += (int)(1000000 - Math.Min(item.Size / 1024, 999999));

        // Recently modified files first
        var ageHours = (DateTime.UtcNow - item.LastModified).TotalHours;
        priority += Math.Max(0, 100 - (int)ageHours);

        return priority;
    }

    private static SyncAction? CreateDeletionAction(DeletionChange deletion) {
        if (deletion.DeletedLocally) {
            // Deleted locally, still exists remotely -> delete remote
            // Check if remote was modified after last sync - potential conflict
            var remoteModified = deletion.TrackedState.RemoteModified;
            var localModified = deletion.TrackedState.LocalModified;

            if (remoteModified.HasValue && localModified.HasValue && remoteModified > localModified) {
                return new SyncAction {
                    Type = SyncActionType.Conflict,
                    Path = deletion.Path,
                    ConflictType = ConflictType.DeletedLocallyModifiedRemotely,
                    Priority = 1000 // High priority for conflicts
                };
            } else {
                return new SyncAction {
                    Type = SyncActionType.DeleteRemote,
                    Path = deletion.Path,
                    Priority = 500 // Medium priority for deletes
                };
            }
        } else if (deletion.DeletedRemotely) {
            // Deleted remotely, still exists locally -> delete local
            // Check if local was modified after last sync - potential conflict
            var localModified = deletion.TrackedState.LocalModified;
            var remoteModified = deletion.TrackedState.RemoteModified;

            if (localModified.HasValue && remoteModified.HasValue && localModified > remoteModified) {
                return new SyncAction {
                    Type = SyncActionType.Conflict,
                    Path = deletion.Path,
                    ConflictType = ConflictType.ModifiedLocallyDeletedRemotely,
                    Priority = 1000 // High priority for conflicts
                };
            } else {
                return new SyncAction {
                    Type = SyncActionType.DeleteLocal,
                    Path = deletion.Path,
                    Priority = 500 // Medium priority for deletes
                };
            }
        }

        // Both deleted or invalid state - no action needed
        return null;
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
        CancellationToken cancellationToken) {
        var allSmallActions = actionGroups.Directories.Concat(actionGroups.SmallFiles).ToList();

        if (allSmallActions.Count == 0) {
            return;
        }

        // Use high parallelism for small operations
        var parallelOptions = new ParallelOptions {
            MaxDegreeOfParallelism = _maxParallelism * 2, // Allow more concurrency for small files
            CancellationToken = cancellationToken
        };

        await Parallel.ForEachAsync(allSmallActions, parallelOptions, async (action, ct) => {
            try {
                // Check for pause point before processing each action
                var currentProgress = new SyncProgress {
                    ProcessedItems = progressCounter.Value,
                    TotalItems = totalChanges,
                    CurrentItem = action.Path
                };

                if (!await CheckPausePointAsync(ct, currentProgress)) {
                    ct.ThrowIfCancellationRequested();
                    return;
                }

                await ProcessActionAsync(action, result, ct);

                var newCount = progressCounter.Increment();

                // Report progress less frequently to reduce overhead
                if (newCount % 10 == 0 || action.LocalItem?.IsDirectory == true) {
                    RaiseProgress(new SyncProgress {
                        ProcessedItems = newCount,
                        TotalItems = totalChanges,
                        CurrentItem = action.Path
                    }, GetOperationType(action.Type));
                }
            } catch (Exception ex) when (ex is not OperationCanceledException) {
                result.IncrementFilesSkipped();
                _logger.ProcessingError(ex, action.Path);
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
        CancellationToken cancellationToken) {
        if (actionGroups.LargeFiles.Count == 0) {
            return;
        }

        // Use reduced parallelism for large files to avoid bandwidth saturation
        using var semaphore = new SemaphoreSlim(Math.Max(1, _maxParallelism / 2), Math.Max(1, _maxParallelism / 2));
        var tasks = new List<Task>();

        foreach (var action in actionGroups.LargeFiles) {
            tasks.Add(ProcessLargeFileAsync(action, result, progressCounter, totalChanges, semaphore, cancellationToken));
        }

        await Task.WhenAll(tasks);
    }

    private async Task ProcessLargeFileAsync(
        SyncAction action,
        ThreadSafeSyncResult result,
        ProgressCounter progressCounter,
        int totalChanges,
        SemaphoreSlim semaphore,
        CancellationToken cancellationToken) {
        await semaphore.WaitAsync(cancellationToken);
        try {
            // Check for pause point before processing large file
            var currentProgress = new SyncProgress {
                ProcessedItems = progressCounter.Value,
                TotalItems = totalChanges,
                CurrentItem = action.Path
            };

            if (!await CheckPausePointAsync(cancellationToken, currentProgress)) {
                cancellationToken.ThrowIfCancellationRequested();
                return;
            }

            // Report start of large file processing
            RaiseProgress(currentProgress, GetOperationType(action.Type));

            await ProcessActionAsync(action, result, cancellationToken);

            var newCount = progressCounter.Increment();

            // Always report progress for large files
            RaiseProgress(new SyncProgress {
                ProcessedItems = newCount,
                TotalItems = totalChanges,
                CurrentItem = action.Path
            }, GetOperationType(action.Type));
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            result.IncrementFilesSkipped();
            _logger.LargeFileProcessingError(ex, action.Path);
        } finally {
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
        CancellationToken cancellationToken) {
        // Process conflicts first (they might resolve to deletes)
        foreach (var action in actionGroups.Conflicts) {
            try {
                // Check for pause point before processing conflict
                var currentProgress = new SyncProgress {
                    ProcessedItems = progressCounter.Value,
                    TotalItems = totalChanges,
                    CurrentItem = action.Path
                };

                if (!await CheckPausePointAsync(cancellationToken, currentProgress)) {
                    cancellationToken.ThrowIfCancellationRequested();
                    return;
                }

                await ProcessActionAsync(action, result, cancellationToken);

                var newCount = progressCounter.Increment();
                RaiseProgress(new SyncProgress {
                    ProcessedItems = newCount,
                    TotalItems = totalChanges,
                    CurrentItem = action.Path
                }, SyncOperation.ResolvingConflict);
            } catch (Exception ex) when (ex is not OperationCanceledException) {
                result.IncrementFilesSkipped();
                _logger.ConflictResolutionError(ex, action.Path);
            }
        }

        // Process deletes last (in reverse depth order to delete children before parents)
        var sortedDeletes = actionGroups.Deletes
            .OrderByDescending(a => a.Path.Count(c => c == '/'))
            .ToList();

        foreach (var action in sortedDeletes) {
            try {
                // Check for pause point before processing delete
                var currentProgress = new SyncProgress {
                    ProcessedItems = progressCounter.Value,
                    TotalItems = totalChanges,
                    CurrentItem = action.Path
                };

                if (!await CheckPausePointAsync(cancellationToken, currentProgress)) {
                    cancellationToken.ThrowIfCancellationRequested();
                    return;
                }

                await ProcessActionAsync(action, result, cancellationToken);

                var newCount = progressCounter.Increment();
                RaiseProgress(new SyncProgress {
                    ProcessedItems = newCount,
                    TotalItems = totalChanges,
                    CurrentItem = action.Path
                }, SyncOperation.Deleting);
            } catch (Exception ex) when (ex is not OperationCanceledException) {
                result.IncrementFilesSkipped();
                _logger.DeletionError(ex, action.Path);
            }
        }
    }

    private async Task ProcessActionAsync(SyncAction action, ThreadSafeSyncResult result, CancellationToken cancellationToken) {
        switch (action.Type) {
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

    private async Task DownloadFileAsync(SyncAction action, ThreadSafeSyncResult result, CancellationToken cancellationToken) {
        if (action.RemoteItem!.IsDirectory) {
            await _localStorage.CreateDirectoryAsync(action.Path, cancellationToken);
        } else {
            using var remoteStream = await _remoteStorage.ReadFileAsync(action.Path, cancellationToken);
            var streamToRead = WrapWithThrottling(remoteStream);
            await _localStorage.WriteFileAsync(action.Path, streamToRead, cancellationToken);

            // Invoke virtual file callback if enabled
            await TryInvokeVirtualFileCallbackAsync(action.Path, action.RemoteItem, cancellationToken);
        }

        result.IncrementFilesSynchronized();
    }

    /// <summary>
    /// Invokes the virtual file callback if placeholders are enabled and callback is configured.
    /// </summary>
    private async Task TryInvokeVirtualFileCallbackAsync(string relativePath, SyncItem fileMetadata, CancellationToken cancellationToken) {
        if (_currentOptions?.CreateVirtualFilePlaceholders is not true || _currentOptions.VirtualFileCallback is null) {
            return;
        }

        try {
            // Construct the full local path from the storage root and relative path
            var localFullPath = Path.Combine(_localStorage.RootPath, relativePath.Replace('/', Path.DirectorySeparatorChar));

            await _currentOptions.VirtualFileCallback(relativePath, localFullPath, fileMetadata, cancellationToken);

            // Update the item's virtual state to indicate it's now a placeholder
            fileMetadata.VirtualState = VirtualFileState.Placeholder;
        } catch (Exception ex) {
            // Log but don't fail the sync - the file remains fully hydrated
            _logger.VirtualFileCallbackError(ex, relativePath);
        }
    }

    private async Task UploadFileAsync(SyncAction action, ThreadSafeSyncResult result, CancellationToken cancellationToken) {
        if (action.LocalItem!.IsDirectory) {
            await _remoteStorage.CreateDirectoryAsync(action.Path, cancellationToken);
        } else {
            using var localStream = await _localStorage.ReadFileAsync(action.Path, cancellationToken);
            var streamToRead = WrapWithThrottling(localStream);
            await _remoteStorage.WriteFileAsync(action.Path, streamToRead, cancellationToken);
        }

        result.IncrementFilesSynchronized();
    }

    private async Task DeleteLocalAsync(SyncAction action, ThreadSafeSyncResult result, CancellationToken cancellationToken) {
        await _localStorage.DeleteAsync(action.Path, cancellationToken);
        result.IncrementFilesDeleted();
    }

    private async Task DeleteRemoteAsync(SyncAction action, ThreadSafeSyncResult result, CancellationToken cancellationToken) {
        await _remoteStorage.DeleteAsync(action.Path, cancellationToken);
        result.IncrementFilesDeleted();
    }

    private async Task ResolveConflictAsync(SyncAction action, ThreadSafeSyncResult result, CancellationToken cancellationToken) {
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
        switch (resolution) {
            case ConflictResolution.UseLocal:
                if (action.LocalItem is not null) {
                    await UploadFileAsync(action, result, cancellationToken);
                }
                break;

            case ConflictResolution.UseRemote:
                if (action.RemoteItem is not null) {
                    await DownloadFileAsync(action, result, cancellationToken);
                }
                break;

            case ConflictResolution.Skip:
                result.IncrementFilesSkipped();
                break;

            case ConflictResolution.RenameLocal:
                if (action.LocalItem is not null && action.RemoteItem is not null) {
                    // Generate unique conflict name for local file using computer name
                    var conflictPath = await GenerateUniqueConflictNameAsync(action.Path, Environment.MachineName, _localStorage, cancellationToken);

                    // Move local file to conflict name
                    await _localStorage.MoveAsync(action.Path, conflictPath, cancellationToken);

                    // Download remote file to original path
                    await DownloadFileAsync(action, result, cancellationToken);

                    // Track the conflict file in database (exists locally, needs to be uploaded)
                    var conflictItem = await _localStorage.GetItemAsync(conflictPath, cancellationToken);
                    if (conflictItem is not null) {
                        var conflictState = new SyncState {
                            Path = conflictPath,
                            IsDirectory = conflictItem.IsDirectory,
                            Status = SyncStatus.LocalNew,
                            LastSyncTime = DateTime.UtcNow,
                            LocalHash = conflictItem.IsDirectory ? null : (conflictItem.ETag ?? await _localStorage.ComputeHashAsync(conflictPath, cancellationToken)),
                            LocalSize = conflictItem.Size,
                            LocalModified = conflictItem.LastModified
                        };
                        await _database.UpdateSyncStateAsync(conflictState, cancellationToken);
                    }
                } else {
                    result.IncrementFilesConflicted();
                }
                break;

            case ConflictResolution.RenameRemote:
                if (action.LocalItem is not null && action.RemoteItem is not null) {
                    // Generate unique conflict name for remote file using domain name
                    var conflictPath = await GenerateUniqueConflictNameAsync(action.Path, GetDomainFromUrl(_remoteStorage.RootPath), _remoteStorage, cancellationToken);

                    // Move remote file to conflict name
                    await _remoteStorage.MoveAsync(action.Path, conflictPath, cancellationToken);

                    // Upload local file to original path
                    await UploadFileAsync(action, result, cancellationToken);

                    // Track the conflict file in database (exists remotely, needs to be downloaded)
                    var conflictItem = await _remoteStorage.GetItemAsync(conflictPath, cancellationToken);
                    if (conflictItem is not null) {
                        var conflictState = new SyncState {
                            Path = conflictPath,
                            IsDirectory = conflictItem.IsDirectory,
                            Status = SyncStatus.RemoteNew,
                            LastSyncTime = DateTime.UtcNow,
                            RemoteHash = conflictItem.IsDirectory ? null : (conflictItem.ETag ?? await _remoteStorage.ComputeHashAsync(conflictPath, cancellationToken)),
                            RemoteSize = conflictItem.Size,
                            RemoteModified = conflictItem.LastModified
                        };
                        await _database.UpdateSyncStateAsync(conflictState, cancellationToken);
                    }
                } else {
                    result.IncrementFilesConflicted();
                }
                break;

            default:
                result.IncrementFilesConflicted();
                break;
        }
    }

    private static async Task<string> GenerateUniqueConflictNameAsync(string path, string sourceIdentifier, ISyncStorage storage, CancellationToken cancellationToken) {
        // Generate a unique conflict filename by inserting the source identifier before the extension
        // If a conflict with the same name already exists, append a number
        // Examples:
        //   "document.txt" -> "document (andre-vivobook).txt"
        //   If exists -> "document (andre-vivobook 2).txt"
        //   If exists -> "document (andre-vivobook 3).txt"
        var directory = Path.GetDirectoryName(path);
        var fileName = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);

        // Try base name first
        var conflictFileName = $"{fileName} ({sourceIdentifier}){extension}";
        var conflictPath = string.IsNullOrEmpty(directory)
            ? conflictFileName
            : Path.Combine(directory, conflictFileName);

        // Check if this path already exists
        if (!await storage.ExistsAsync(conflictPath, cancellationToken)) {
            return conflictPath;
        }

        // If it exists, try numbered versions
        for (int i = 2; i <= 100; i++) {
            conflictFileName = $"{fileName} ({sourceIdentifier} {i}){extension}";
            conflictPath = string.IsNullOrEmpty(directory)
                ? conflictFileName
                : Path.Combine(directory, conflictFileName);

            if (!await storage.ExistsAsync(conflictPath, cancellationToken)) {
                return conflictPath;
            }
        }

        // Fallback: use timestamp if we somehow have 100+ conflicts
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd-HHmmss");
        conflictFileName = $"{fileName} ({sourceIdentifier} {timestamp}){extension}";
        return string.IsNullOrEmpty(directory)
            ? conflictFileName
            : Path.Combine(directory, conflictFileName);
    }

    private static string GetDomainFromUrl(string url) {
        // Extract domain name from URL
        // Example: "https://disk.cx/remote.php/dav/files/user/" -> "disk.cx"
        try {
            var uri = new Uri(url);
            return uri.Host;
        } catch {
            // Fallback to "remote" if URL parsing fails
            return "remote";
        }
    }

    private async Task UpdateDatabaseStateAsync(ChangeSet changes, CancellationToken cancellationToken) {
        // Update database with new sync states
        var updates = new List<Task>();

        // Update additions
        foreach (var addition in changes.Additions) {
            var state = new SyncState {
                Path = addition.Path,
                IsDirectory = addition.Item.IsDirectory,
                Status = SyncStatus.Synced,
                LastSyncTime = DateTime.UtcNow
            };

            if (addition.IsLocal) {
                state.LocalHash = addition.Item.IsDirectory ? null : (addition.Item.ETag ?? await _localStorage.ComputeHashAsync(addition.Path, cancellationToken));
                state.LocalSize = addition.Item.Size;
                state.LocalModified = addition.Item.LastModified;
                state.RemoteHash = state.LocalHash;
                state.RemoteSize = state.LocalSize;
                state.RemoteModified = state.LocalModified;
            } else {
                state.RemoteHash = addition.Item.IsDirectory ? null : (addition.Item.ETag ?? await _remoteStorage.ComputeHashAsync(addition.Path, cancellationToken));
                state.RemoteSize = addition.Item.Size;
                state.RemoteModified = addition.Item.LastModified;
                state.LocalHash = state.RemoteHash;
                state.LocalSize = state.RemoteSize;
                state.LocalModified = state.RemoteModified;
            }

            updates.Add(_database.UpdateSyncStateAsync(state, cancellationToken));
        }

        // Update modifications
        foreach (var mod in changes.Modifications) {
            var state = mod.TrackedState;
            state.Status = SyncStatus.Synced;
            state.LastSyncTime = DateTime.UtcNow;

            if (mod.IsLocal) {
                state.LocalHash = mod.Item.IsDirectory ? null : (mod.Item.ETag ?? await _localStorage.ComputeHashAsync(mod.Path, cancellationToken));
                state.LocalSize = mod.Item.Size;
                state.LocalModified = mod.Item.LastModified;
                state.RemoteHash = state.LocalHash;
                state.RemoteSize = state.LocalSize;
                state.RemoteModified = state.LocalModified;
            } else {
                state.RemoteHash = mod.Item.IsDirectory ? null : (mod.Item.ETag ?? await _remoteStorage.ComputeHashAsync(mod.Path, cancellationToken));
                state.RemoteSize = mod.Item.Size;
                state.RemoteModified = mod.Item.LastModified;
                state.LocalHash = state.RemoteHash;
                state.LocalSize = state.RemoteSize;
                state.LocalModified = state.RemoteModified;
            }

            updates.Add(_database.UpdateSyncStateAsync(state, cancellationToken));
        }

        // Update deletions
        foreach (var deletion in changes.Deletions) {
            if (deletion.DeletedLocally && deletion.DeletedRemotely) {
                // Both deleted - remove from DB
                updates.Add(_database.DeleteSyncStateAsync(deletion.Path, cancellationToken));
            } else {
                // One-sided deletion that was synced
                updates.Add(_database.DeleteSyncStateAsync(deletion.Path, cancellationToken));
            }
        }

        await Task.WhenAll(updates);
    }

    private static SyncOperation GetOperationType(SyncActionType actionType) => actionType switch {
        SyncActionType.Download => SyncOperation.Downloading,
        SyncActionType.Upload => SyncOperation.Uploading,
        SyncActionType.DeleteLocal or SyncActionType.DeleteRemote => SyncOperation.Deleting,
        SyncActionType.Conflict => SyncOperation.ResolvingConflict,
        _ => SyncOperation.Unknown
    };

    /// <summary>
    /// Wraps the provided stream with a throttled stream if bandwidth limiting is enabled.
    /// </summary>
    /// <param name="stream">The stream to potentially wrap.</param>
    /// <returns>The original stream or a throttled wrapper.</returns>
    private Stream WrapWithThrottling(Stream stream) {
        if (_currentMaxBytesPerSecond.HasValue && _currentMaxBytesPerSecond.Value > 0) {
            return new ThrottledStream(stream, _currentMaxBytesPerSecond.Value);
        }

        return stream;
    }

    private void RaiseProgress(SyncProgress progress, SyncOperation operation) {
        ProgressChanged?.Invoke(this, new SyncProgressEventArgs(progress, progress.CurrentItem, operation));
    }

    /// <summary>
    /// Gets synchronization database statistics including file counts and sizes.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
    /// <returns>Database statistics including total files, directories, and sizes.</returns>
    public async Task<DatabaseStats> GetStatsAsync(CancellationToken cancellationToken = default) {
        return await _database.GetStatsAsync(cancellationToken);
    }

    /// <summary>
    /// Resets the synchronization state by clearing all tracked file information from the database.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
    /// <remarks>
    /// Use this method with caution as it will force a full resynchronization on the next sync operation.
    /// This is useful when the sync state becomes corrupted or when starting fresh.
    /// </remarks>
    public async Task ResetSyncStateAsync(CancellationToken cancellationToken = default) {
        await _database.ClearAsync(cancellationToken);
    }

    /// <summary>
    /// Pauses the current synchronization operation gracefully
    /// </summary>
    /// <returns>A task that completes when the engine has entered the paused state</returns>
    /// <remarks>
    /// The pause is graceful - the engine will complete the current file operation
    /// before entering the paused state. If no synchronization is in progress, returns immediately.
    /// </remarks>
    public Task PauseAsync() {
        if (_disposed) {
            throw new ObjectDisposedException(nameof(SyncEngine));
        }

        lock (_stateLock) {
            // Can only pause if currently running
            if (_state != SyncEngineState.Running) {
                return Task.CompletedTask;
            }

            _state = SyncEngineState.Paused;
            _pauseCompletionSource = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            // Reset the event to block waiting threads
            _pauseEvent.Reset();

            _logger.SyncPausing();
        }

        // Return a task that completes when we've actually reached a pause point
        return _pauseCompletionSource.Task;
    }

    /// <summary>
    /// Resumes a paused synchronization operation
    /// </summary>
    /// <returns>A task that completes when the engine has resumed</returns>
    /// <remarks>
    /// If the engine is not paused, this method returns immediately.
    /// After resuming, synchronization continues from where it was paused.
    /// </remarks>
    public Task ResumeAsync() {
        if (_disposed) {
            throw new ObjectDisposedException(nameof(SyncEngine));
        }

        lock (_stateLock) {
            // Can only resume if currently paused
            if (_state != SyncEngineState.Paused) {
                return Task.CompletedTask;
            }

            _state = SyncEngineState.Running;
            _pausedProgress = null;

            // Set the event to allow waiting threads to continue
            _pauseEvent.Set();

            _logger.SyncResuming();
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Checks if the engine should pause and waits if necessary.
    /// Call this at safe pause points (between file operations).
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <param name="currentProgress">Current progress to report while paused</param>
    /// <returns>True if should continue, false if cancelled</returns>
    private bool CheckPausePoint(CancellationToken cancellationToken, SyncProgress? currentProgress = null) {
        // Fast path - if not paused and not cancelled, continue immediately
        if (_pauseEvent.IsSet && !cancellationToken.IsCancellationRequested) {
            return true;
        }

        // Check for cancellation first
        if (cancellationToken.IsCancellationRequested) {
            return false;
        }

        // We're paused - signal that we've reached a pause point
        TaskCompletionSource? tcs;
        lock (_stateLock) {
            tcs = _pauseCompletionSource;
            _pausedProgress = currentProgress;
        }

        // Signal that pause point was reached
        tcs?.TrySetResult();

        // Report paused state via progress
        if (currentProgress is not null) {
            RaiseProgress(currentProgress, SyncOperation.Paused);
        }

        _logger.SyncPaused();

        // Wait for resume or cancellation
        try {
            // Use WaitHandle.WaitAny to wait for either the pause event or cancellation
            WaitHandle.WaitAny([_pauseEvent.WaitHandle, cancellationToken.WaitHandle]);

            if (cancellationToken.IsCancellationRequested) {
                return false;
            }

            _logger.SyncResumed();
            return true;
        } catch (OperationCanceledException) {
            return false;
        }
    }

    /// <summary>
    /// Async version of pause point check for use in async contexts
    /// </summary>
    private async Task<bool> CheckPausePointAsync(CancellationToken cancellationToken, SyncProgress? currentProgress = null) {
        // Fast path - if not paused and not cancelled, continue immediately
        if (_pauseEvent.IsSet && !cancellationToken.IsCancellationRequested) {
            return true;
        }

        // Run the blocking wait on a thread pool thread to not block async context
        return await Task.Run(() => CheckPausePoint(cancellationToken, currentProgress), cancellationToken);
    }

    /// <summary>
    /// Releases all resources used by the sync engine
    /// </summary>
    /// <remarks>
    /// Cancels any ongoing synchronization operation and disposes of the synchronization semaphore.
    /// This method can be called multiple times safely. After disposal, the sync engine cannot be reused.
    /// </remarks>
    public void Dispose() {
        if (!_disposed) {
            // Resume any paused operation so it can exit cleanly
            _pauseEvent.Set();
            _currentSyncCts?.Cancel();
            _syncSemaphore?.Dispose();
            _pauseEvent?.Dispose();
            _disposed = true;
        }
    }
}
