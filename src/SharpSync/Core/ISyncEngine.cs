namespace Oire.SharpSync.Core;

/// <summary>
/// Interface for the sync engine that orchestrates synchronization between storages
/// </summary>
public interface ISyncEngine: IDisposable {
    /// <summary>
    /// Event raised to report synchronization progress
    /// </summary>
    event EventHandler<SyncProgressEventArgs>? ProgressChanged;

    /// <summary>
    /// Event raised when a file conflict is detected
    /// </summary>
    event EventHandler<FileConflictEventArgs>? ConflictDetected;

    /// <summary>
    /// Gets whether the engine is currently synchronizing
    /// </summary>
    bool IsSynchronizing { get; }

    /// <summary>
    /// Gets whether the engine is currently paused
    /// </summary>
    bool IsPaused { get; }

    /// <summary>
    /// Gets the current state of the sync engine
    /// </summary>
    SyncEngineState State { get; }

    /// <summary>
    /// Synchronizes files between local and remote storage
    /// </summary>
    Task<SyncResult> SynchronizeAsync(SyncOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Performs a dry run to preview changes without applying them
    /// </summary>
    Task<SyncResult> PreviewSyncAsync(SyncOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a detailed plan of synchronization actions that will be performed
    /// </summary>
    /// <param name="options">Optional synchronization options</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation</param>
    /// <returns>A detailed sync plan with all planned actions</returns>
    /// <remarks>
    /// <para>
    /// This method performs change detection and returns a detailed plan of what will happen during synchronization,
    /// without actually modifying any files. Desktop clients can use this to show users a detailed preview with
    /// file-by-file information, sizes, and action types before synchronization begins.
    /// </para>
    /// <para>
    /// The plan incorporates pending changes from <see cref="NotifyLocalChangeAsync"/>,
    /// <see cref="NotifyLocalChangesAsync"/>, and <see cref="NotifyLocalRenameAsync"/> calls,
    /// giving priority to these tracked changes over full storage scans for better performance.
    /// </para>
    /// </remarks>
    Task<SyncPlan> GetSyncPlanAsync(SyncOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the current sync statistics
    /// </summary>
    Task<DatabaseStats> GetStatsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Resets all sync state (forces full rescan)
    /// </summary>
    Task ResetSyncStateAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Pauses the current synchronization operation
    /// </summary>
    /// <remarks>
    /// <para>
    /// The pause is graceful - the engine will complete the current file operation
    /// before entering the paused state. This ensures no partial file transfers occur.
    /// </para>
    /// <para>
    /// If no synchronization is in progress, this method returns immediately.
    /// </para>
    /// <para>
    /// While paused, the <see cref="ProgressChanged"/> event will fire with
    /// <see cref="SyncOperation.Paused"/> to indicate the paused state.
    /// </para>
    /// </remarks>
    /// <returns>A task that completes when the engine has entered the paused state</returns>
    Task PauseAsync();

    /// <summary>
    /// Resumes a paused synchronization operation
    /// </summary>
    /// <remarks>
    /// <para>
    /// If the engine is not paused, this method returns immediately.
    /// </para>
    /// <para>
    /// After resuming, synchronization continues from where it was paused,
    /// processing any remaining files in the sync queue.
    /// </para>
    /// </remarks>
    /// <returns>A task that completes when the engine has resumed</returns>
    Task ResumeAsync();

    /// <summary>
    /// Synchronizes a specific folder without performing a full scan.
    /// </summary>
    /// <param name="folderPath">The relative path of the folder to synchronize (e.g., "Documents/Projects")</param>
    /// <param name="options">Optional synchronization options</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation</param>
    /// <returns>A <see cref="SyncResult"/> containing synchronization statistics for the folder</returns>
    /// <remarks>
    /// <para>
    /// This method performs a targeted synchronization of a specific folder and its contents,
    /// without scanning the entire storage. This is more efficient than a full sync when you
    /// only need to synchronize a portion of the directory tree.
    /// </para>
    /// <para>
    /// Desktop clients can use this method to:
    /// <list type="bullet">
    /// <item>Sync a specific folder on user request (e.g., right-click "Sync Now")</item>
    /// <item>Prioritize synchronization of actively used folders</item>
    /// <item>Implement folder-level selective sync features</item>
    /// </list>
    /// </para>
    /// </remarks>
    /// <exception cref="ObjectDisposedException">Thrown when the sync engine has been disposed</exception>
    /// <exception cref="InvalidOperationException">Thrown when synchronization is already in progress</exception>
    Task<SyncResult> SyncFolderAsync(string folderPath, SyncOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Synchronizes specific files on demand without performing a full scan.
    /// </summary>
    /// <param name="filePaths">The relative paths of files to synchronize</param>
    /// <param name="options">Optional synchronization options</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation</param>
    /// <returns>A <see cref="SyncResult"/> containing synchronization statistics for the files</returns>
    /// <remarks>
    /// <para>
    /// This method performs a targeted synchronization of specific files without scanning
    /// the entire storage. This is the most efficient option when you know exactly which
    /// files need to be synchronized.
    /// </para>
    /// <para>
    /// Desktop clients can use this method to:
    /// <list type="bullet">
    /// <item>Sync files that were just modified locally (detected via FileSystemWatcher)</item>
    /// <item>Force sync of specific files on user request</item>
    /// <item>Implement "sync on open" for cloud placeholders</item>
    /// </list>
    /// </para>
    /// </remarks>
    /// <exception cref="ObjectDisposedException">Thrown when the sync engine has been disposed</exception>
    /// <exception cref="InvalidOperationException">Thrown when synchronization is already in progress</exception>
    Task<SyncResult> SyncFilesAsync(IEnumerable<string> filePaths, SyncOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Notifies the sync engine of a local file system change for incremental sync detection.
    /// </summary>
    /// <param name="path">The relative path that changed</param>
    /// <param name="changeType">The type of change that occurred</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation</param>
    /// <remarks>
    /// <para>
    /// This method allows desktop clients to feed FileSystemWatcher events directly to the
    /// sync engine for efficient incremental change detection, avoiding the need for full scans.
    /// </para>
    /// <para>
    /// The sync engine will update its internal change tracking state. Actual synchronization
    /// still requires calling <see cref="SynchronizeAsync"/>, <see cref="SyncFolderAsync"/>,
    /// or <see cref="SyncFilesAsync"/>.
    /// </para>
    /// <para>
    /// For rename operations, use <see cref="NotifyLocalRenameAsync"/> instead to properly
    /// track the old and new paths.
    /// </para>
    /// <para>
    /// Example integration with FileSystemWatcher:
    /// <code>
    /// watcher.Changed += async (s, e) =>
    ///     await engine.NotifyLocalChangeAsync(
    ///         GetRelativePath(e.FullPath),
    ///         ChangeType.Changed,
    ///         cancellationToken);
    /// </code>
    /// </para>
    /// </remarks>
    /// <exception cref="ObjectDisposedException">Thrown when the sync engine has been disposed</exception>
    Task NotifyLocalChangeAsync(string path, ChangeType changeType, CancellationToken cancellationToken = default);

    /// <summary>
    /// Notifies the sync engine of multiple local file system changes in a batch.
    /// </summary>
    /// <param name="changes">Collection of path and change type pairs</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation</param>
    /// <remarks>
    /// <para>
    /// This method is more efficient than calling <see cref="NotifyLocalChangeAsync"/> multiple times
    /// when handling bursts of FileSystemWatcher events. Changes are coalesced internally.
    /// </para>
    /// <para>
    /// Example usage with debounced FileSystemWatcher events:
    /// <code>
    /// var changes = new List&lt;(string, ChangeType)&gt;();
    /// // ... collect changes over a short time window ...
    /// await engine.NotifyLocalChangesAsync(changes, cancellationToken);
    /// </code>
    /// </para>
    /// </remarks>
    /// <exception cref="ObjectDisposedException">Thrown when the sync engine has been disposed</exception>
    Task NotifyLocalChangesAsync(IEnumerable<(string Path, ChangeType ChangeType)> changes, CancellationToken cancellationToken = default);

    /// <summary>
    /// Notifies the sync engine of a local file or directory rename.
    /// </summary>
    /// <param name="oldPath">The previous relative path before the rename</param>
    /// <param name="newPath">The new relative path after the rename</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation</param>
    /// <remarks>
    /// <para>
    /// This method properly tracks rename operations by recording both the deletion of the
    /// old path and the creation of the new path. This allows the sync engine to optimize
    /// the operation as a server-side move/rename when possible, rather than a delete + upload.
    /// </para>
    /// <para>
    /// Example integration with FileSystemWatcher:
    /// <code>
    /// watcher.Renamed += async (s, e) =>
    ///     await engine.NotifyLocalRenameAsync(
    ///         GetRelativePath(e.OldFullPath),
    ///         GetRelativePath(e.FullPath),
    ///         cancellationToken);
    /// </code>
    /// </para>
    /// </remarks>
    /// <exception cref="ObjectDisposedException">Thrown when the sync engine has been disposed</exception>
    Task NotifyLocalRenameAsync(string oldPath, string newPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the list of pending operations that would be performed on the next sync.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token to cancel the operation</param>
    /// <returns>A collection of pending sync operations</returns>
    /// <remarks>
    /// <para>
    /// This method returns the current queue of pending operations based on tracked changes.
    /// Desktop clients can use this to:
    /// <list type="bullet">
    /// <item>Display pending changes in a status UI (e.g., "3 files waiting to upload")</item>
    /// <item>Show a detailed list of pending operations before sync</item>
    /// <item>Allow users to inspect what will be synchronized</item>
    /// </list>
    /// </para>
    /// <para>
    /// Note: This returns operations based on currently tracked changes from
    /// <see cref="NotifyLocalChangeAsync"/> calls. For a complete sync plan including
    /// remote changes, use <see cref="GetSyncPlanAsync"/> instead.
    /// </para>
    /// </remarks>
    /// <exception cref="ObjectDisposedException">Thrown when the sync engine has been disposed</exception>
    Task<IReadOnlyList<PendingOperation>> GetPendingOperationsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears all pending changes that were tracked via <see cref="NotifyLocalChangeAsync"/>,
    /// <see cref="NotifyLocalChangesAsync"/>, or <see cref="NotifyLocalRenameAsync"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Use this method to discard pending notifications without performing synchronization.
    /// This is useful when:
    /// <list type="bullet">
    /// <item>The user cancels a batch of pending changes</item>
    /// <item>Resetting state after an error</item>
    /// <item>Clearing stale notifications after reconnecting</item>
    /// </list>
    /// </para>
    /// <para>
    /// This method does not affect the database sync state, only the in-memory pending changes queue.
    /// </para>
    /// </remarks>
    /// <exception cref="ObjectDisposedException">Thrown when the sync engine has been disposed</exception>
    void ClearPendingChanges();

    /// <summary>
    /// Gets recent completed operations for activity history display.
    /// </summary>
    /// <param name="limit">Maximum number of operations to return (default: 100)</param>
    /// <param name="since">Only return operations completed after this time (optional)</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation</param>
    /// <returns>A collection of completed operations ordered by completion time descending</returns>
    /// <remarks>
    /// <para>
    /// Desktop clients can use this method to:
    /// <list type="bullet">
    /// <item>Display an activity feed showing recent sync operations</item>
    /// <item>Show users what files were recently uploaded, downloaded, or deleted</item>
    /// <item>Build a sync history view with filtering by time</item>
    /// <item>Detect failed operations that may need attention</item>
    /// </list>
    /// </para>
    /// <para>
    /// Operations are logged automatically during synchronization. Both successful and failed
    /// operations are recorded to provide a complete activity history.
    /// </para>
    /// </remarks>
    /// <exception cref="ObjectDisposedException">Thrown when the sync engine has been disposed</exception>
    Task<IReadOnlyList<CompletedOperation>> GetRecentOperationsAsync(
        int limit = 100,
        DateTime? since = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears operation history older than the specified date.
    /// </summary>
    /// <param name="olderThan">Delete operations completed before this date</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation</param>
    /// <returns>The number of operations deleted</returns>
    /// <remarks>
    /// Use this method periodically to prevent the operation history from growing indefinitely.
    /// For example, you might clear operations older than 30 days.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">Thrown when the sync engine has been disposed</exception>
    Task<int> ClearOperationHistoryAsync(DateTime olderThan, CancellationToken cancellationToken = default);
}
