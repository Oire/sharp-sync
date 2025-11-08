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
    /// This method performs change detection and returns a detailed plan of what will happen during synchronization,
    /// without actually modifying any files. Desktop clients can use this to show users a detailed preview with
    /// file-by-file information, sizes, and action types before synchronization begins.
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
}
