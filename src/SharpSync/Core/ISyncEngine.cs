namespace Oire.SharpSync.Core;

/// <summary>
/// Interface for the sync engine that orchestrates synchronization between storages
/// </summary>
public interface ISyncEngine : IDisposable
{
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
    /// Gets the current sync statistics
    /// </summary>
    Task<DatabaseStats> GetStatsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Resets all sync state (forces full rescan)
    /// </summary>
    Task ResetSyncStateAsync(CancellationToken cancellationToken = default);
}