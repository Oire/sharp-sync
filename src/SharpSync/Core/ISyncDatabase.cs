namespace Oire.SharpSync.Core;

/// <summary>
/// Interface for sync state database operations
/// </summary>
public interface ISyncDatabase: IDisposable {
    /// <summary>
    /// Initializes the database
    /// </summary>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets sync state for a specific path
    /// </summary>
    Task<SyncState?> GetSyncStateAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates sync state for a path
    /// </summary>
    Task UpdateSyncStateAsync(SyncState state, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes sync state for a path
    /// </summary>
    Task DeleteSyncStateAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all sync states
    /// </summary>
    Task<IEnumerable<SyncState>> GetAllSyncStatesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all sync states for paths matching a given prefix.
    /// Used for efficient folder-scoped queries in selective sync operations.
    /// </summary>
    /// <param name="pathPrefix">The path prefix to match (e.g., "Documents/Projects" matches all items under that folder)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>All sync states where the path starts with the given prefix</returns>
    Task<IEnumerable<SyncState>> GetSyncStatesByPrefixAsync(string pathPrefix, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets sync states that need synchronization
    /// </summary>
    Task<IEnumerable<SyncState>> GetPendingSyncStatesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Begins a transaction
    /// </summary>
    Task<ISyncTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears all sync states
    /// </summary>
    Task ClearAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets database statistics
    /// </summary>
    Task<DatabaseStats> GetStatsAsync(CancellationToken cancellationToken = default);
}
