// =============================================================================
// SharpSync Basic Usage Example
// =============================================================================
// This file demonstrates how to use SharpSync for file synchronization.
// Copy this code into your own project that references the SharpSync NuGet package.
//
// Required NuGet packages:
//   - Oire.SharpSync
//   - Microsoft.Extensions.Logging.Console (optional, for logging)
// =============================================================================

using Microsoft.Extensions.Logging;
using Oire.SharpSync.Core;
using Oire.SharpSync.Database;
using Oire.SharpSync.Storage;
using Oire.SharpSync.Sync;

namespace YourApp;

public class SyncExample {
    /// <summary>
    /// Basic example: Sync local folder with a remote storage.
    /// </summary>
    public static async Task BasicSyncAsync() {
        // 1. Create storage instances
        var localStorage = new LocalFileStorage("/path/to/local/folder");

        // For remote storage, choose one:
        // - WebDavStorage for Nextcloud/ownCloud/WebDAV servers
        // - SftpStorage for SFTP servers
        // - FtpStorage for FTP/FTPS servers
        // - S3Storage for AWS S3 or S3-compatible storage (MinIO, etc.)
        var remoteStorage = new LocalFileStorage("/path/to/remote/folder"); // Demo only

        // 2. Create and initialize sync database
        var database = new SqliteSyncDatabase("/path/to/sync.db");
        await database.InitializeAsync();

        // 3. Create filter for selective sync (optional)
        var filter = new SyncFilter();
        filter.AddExcludePattern("*.tmp");
        filter.AddExcludePattern("*.log");
        filter.AddExcludePattern(".git/**");
        filter.AddExcludePattern("node_modules/**");

        // 4. Create conflict resolver
        var conflictResolver = new DefaultConflictResolver(ConflictResolution.UseRemote);

        // 5. Create sync engine
        using var syncEngine = new SyncEngine(
            localStorage,
            remoteStorage,
            database,
            filter,
            conflictResolver);

        // 6. Wire up events for UI updates
        syncEngine.ProgressChanged += (sender, e) => {
            Console.WriteLine($"[{e.Progress.Percentage:F0}%] {e.Operation}: {e.Progress.CurrentItem}");
        };

        // Per-file byte-level progress for large file transfers
        syncEngine.FileProgressChanged += (sender, e) => {
            Console.WriteLine($"  {e.Operation}: {e.Path} - {e.PercentComplete}% ({e.BytesTransferred}/{e.TotalBytes} bytes)");
        };

        syncEngine.ConflictDetected += (sender, e) => {
            Console.WriteLine($"Conflict: {e.Path}");
        };

        // 7. Run synchronization
        var result = await syncEngine.SynchronizeAsync();

        Console.WriteLine($"Sync completed: {result.FilesSynchronized} files synchronized");
    }

    /// <summary>
    /// Preview changes before syncing.
    /// </summary>
    public static async Task PreviewSyncAsync(ISyncEngine syncEngine) {
        var plan = await syncEngine.GetSyncPlanAsync();

        Console.WriteLine($"Uploads planned: {plan.Uploads.Count}");
        foreach (var upload in plan.Uploads) {
            Console.WriteLine($"  + {upload.Path} ({upload.Size} bytes)");
        }

        Console.WriteLine($"Downloads planned: {plan.Downloads.Count}");
        foreach (var download in plan.Downloads) {
            Console.WriteLine($"  - {download.Path} ({download.Size} bytes)");
        }

        Console.WriteLine($"Conflicts: {plan.Conflicts.Count}");
    }

    /// <summary>
    /// Display activity history (recent operations).
    /// </summary>
    public static async Task ShowActivityHistoryAsync(ISyncEngine syncEngine) {
        // Get last 50 operations
        var recentOps = await syncEngine.GetRecentOperationsAsync(limit: 50);

        Console.WriteLine("=== Recent Sync Activity ===");
        foreach (var op in recentOps) {
            var icon = op.ActionType switch {
                SyncActionType.Upload => "↑",
                SyncActionType.Download => "↓",
                SyncActionType.DeleteLocal or SyncActionType.DeleteRemote => "×",
                SyncActionType.Conflict => "!",
                _ => "?"
            };
            var status = op.Success ? "✓" : "✗";
            Console.WriteLine($"{status} {icon} {op.Path} ({op.Duration.TotalSeconds:F1}s)");
        }

        // Get operations from last hour only
        var lastHour = await syncEngine.GetRecentOperationsAsync(
            limit: 100,
            since: DateTime.UtcNow.AddHours(-1));
        Console.WriteLine($"\nOperations in last hour: {lastHour.Count}");

        // Cleanup old history (e.g., on app startup)
        var deleted = await syncEngine.ClearOperationHistoryAsync(DateTime.UtcNow.AddDays(-30));
        Console.WriteLine($"Cleaned up {deleted} old operation records");
    }

    /// <summary>
    /// Integrate with FileSystemWatcher for real-time sync.
    /// </summary>
    public static void SetupFileSystemWatcher(ISyncEngine syncEngine, string localPath) {
        var watcher = new FileSystemWatcher(localPath) {
            IncludeSubdirectories = true,
            EnableRaisingEvents = true
        };

        watcher.Created += async (s, e) => {
            var relativePath = Path.GetRelativePath(localPath, e.FullPath);
            await syncEngine.NotifyLocalChangeAsync(relativePath, ChangeType.Created);
        };

        watcher.Changed += async (s, e) => {
            var relativePath = Path.GetRelativePath(localPath, e.FullPath);
            await syncEngine.NotifyLocalChangeAsync(relativePath, ChangeType.Changed);
        };

        watcher.Deleted += async (s, e) => {
            var relativePath = Path.GetRelativePath(localPath, e.FullPath);
            await syncEngine.NotifyLocalChangeAsync(relativePath, ChangeType.Deleted);
        };

        watcher.Renamed += async (s, e) => {
            var oldRelativePath = Path.GetRelativePath(localPath, e.OldFullPath);
            var newRelativePath = Path.GetRelativePath(localPath, e.FullPath);
            await syncEngine.NotifyLocalRenameAsync(oldRelativePath, newRelativePath);
        };

        // Check pending operations
        Task.Run(async () => {
            var pending = await syncEngine.GetPendingOperationsAsync();
            Console.WriteLine($"Pending operations: {pending.Count}");
        });
    }

    /// <summary>
    /// Sync specific files on demand.
    /// </summary>
    public static async Task SyncSpecificFilesAsync(ISyncEngine syncEngine) {
        // Sync a specific folder
        var folderResult = await syncEngine.SyncFolderAsync("Documents/Important");
        Console.WriteLine($"Folder sync: {folderResult.FilesSynchronized} files");

        // Sync specific files
        var fileResult = await syncEngine.SyncFilesAsync(new[] {
            "config.json",
            "data/settings.xml"
        });
        Console.WriteLine($"File sync: {fileResult.FilesSynchronized} files");
    }

    /// <summary>
    /// Pause and resume sync operations.
    /// </summary>
    public static async Task PauseResumeDemoAsync(ISyncEngine syncEngine, CancellationToken ct) {
        // Start sync in background
        var syncTask = syncEngine.SynchronizeAsync(cancellationToken: ct);

        // Pause after some time
        await Task.Delay(1000);
        if (syncEngine.State == SyncEngineState.Running) {
            await syncEngine.PauseAsync();
            Console.WriteLine($"Sync paused. State: {syncEngine.State}");

            // Do something while paused...
            await Task.Delay(2000);

            // Resume
            await syncEngine.ResumeAsync();
            Console.WriteLine($"Sync resumed. State: {syncEngine.State}");
        }

        await syncTask;
    }

    /// <summary>
    /// Configure bandwidth throttling.
    /// </summary>
    public static async Task ThrottledSyncAsync(ISyncEngine syncEngine) {
        var options = new SyncOptions {
            // Limit to 1 MB/s
            MaxBytesPerSecond = 1024 * 1024
        };

        var result = await syncEngine.SynchronizeAsync(options);
        Console.WriteLine($"Throttled sync completed: {result.FilesSynchronized} files");
    }

    /// <summary>
    /// Configure sync options for fine-grained control over synchronization behavior.
    /// </summary>
    public static async Task SyncWithOptionsAsync(ISyncEngine syncEngine) {
        // Checksum-only mode: detect changes by file hash instead of timestamps.
        // Useful when timestamps are unreliable (e.g., after restoring from backup).
        var checksumOptions = new SyncOptions { ChecksumOnly = true };
        await syncEngine.SynchronizeAsync(checksumOptions);

        // Size-only mode: detect changes by file size only (fastest, least accurate).
        // Good for quick checks when only large content changes matter.
        var sizeOptions = new SyncOptions { SizeOnly = true };
        await syncEngine.SynchronizeAsync(sizeOptions);

        // Preserve timestamps and permissions across sync.
        // Timestamps are set on the target after each file transfer.
        // Permissions (Unix only) are preserved for Local and SFTP storage.
        var preserveOptions = new SyncOptions {
            PreserveTimestamps = true,
            PreservePermissions = true
        };
        await syncEngine.SynchronizeAsync(preserveOptions);

        // Skip symlink directories during sync.
        // When false (default), symlink directories are not followed.
        var symlinkOptions = new SyncOptions { FollowSymlinks = true };
        await syncEngine.SynchronizeAsync(symlinkOptions);

        // Per-sync exclude patterns (applied in addition to the engine-level SyncFilter).
        // Useful for one-off syncs that need extra filtering without modifying the filter.
        var excludeOptions = new SyncOptions {
            ExcludePatterns = new List<string> { "*.bak", "thumbs.db", "*.tmp" }
        };
        await syncEngine.SynchronizeAsync(excludeOptions);

        // Timeout: cancel sync if it exceeds the given number of seconds.
        var timeoutOptions = new SyncOptions { TimeoutSeconds = 300 };
        await syncEngine.SynchronizeAsync(timeoutOptions);

        // UpdateExisting=false: only sync new files, skip modifications to existing files.
        var newOnlyOptions = new SyncOptions { UpdateExisting = false };
        await syncEngine.SynchronizeAsync(newOnlyOptions);

        // Override conflict resolution per-sync via options.
        // This takes priority over the IConflictResolver passed to the engine constructor.
        // Set to ConflictResolution.Ask to delegate to the resolver instead.
        var conflictOptions = new SyncOptions {
            ConflictResolution = ConflictResolution.UseLocal
        };
        await syncEngine.SynchronizeAsync(conflictOptions);

        // Verbose logging: emits detailed Debug-level log messages for change detection,
        // action processing, and phase completion. Requires an ILogger<SyncEngine> to be
        // passed to the SyncEngine constructor.
        var verboseOptions = new SyncOptions { Verbose = true };
        await syncEngine.SynchronizeAsync(verboseOptions);

        // Options can be combined freely.
        var combinedOptions = new SyncOptions {
            ChecksumOnly = true,
            PreserveTimestamps = true,
            ExcludePatterns = new List<string> { "*.log" },
            TimeoutSeconds = 600,
            Verbose = true
        };
        var result = await syncEngine.SynchronizeAsync(combinedOptions);
        Console.WriteLine($"Combined sync completed: {result.FilesSynchronized} files");
    }

    /// <summary>
    /// Smart conflict resolution with UI callback.
    /// </summary>
    public static ISyncEngine CreateEngineWithSmartConflictResolver(
        ISyncStorage localStorage,
        ISyncStorage remoteStorage,
        ISyncDatabase database,
        ISyncFilter filter) {
        // SmartConflictResolver analyzes conflicts and can prompt the user
        var resolver = new SmartConflictResolver(
            conflictHandler: async (analysis, ct) => {
                // This callback is invoked for each conflict
                Console.WriteLine($"Conflict: {analysis.Path}");
                Console.WriteLine($"  Local: {analysis.LocalSize} bytes, modified {analysis.LocalModified}");
                Console.WriteLine($"  Remote: {analysis.RemoteSize} bytes, modified {analysis.RemoteModified}");
                Console.WriteLine($"  Recommendation: {analysis.Recommendation}");
                Console.WriteLine($"  Reason: {analysis.ReasonForRecommendation}");

                // In a real app, show a dialog and return user's choice
                // For this example, accept the recommendation
                return analysis.Recommendation;
            },
            defaultResolution: ConflictResolution.Ask);

        return new SyncEngine(localStorage, remoteStorage, database, filter, resolver);
    }
}
