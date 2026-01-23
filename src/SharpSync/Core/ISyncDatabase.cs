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

    /// <summary>
    /// Logs a completed synchronization operation for activity history.
    /// </summary>
    /// <param name="path">The relative path of the file or directory</param>
    /// <param name="actionType">The type of operation that was performed</param>
    /// <param name="isDirectory">Whether the item is a directory</param>
    /// <param name="size">The size of the file in bytes (0 for directories)</param>
    /// <param name="source">The source of the change that triggered this operation</param>
    /// <param name="startedAt">When the operation started</param>
    /// <param name="completedAt">When the operation completed</param>
    /// <param name="success">Whether the operation completed successfully</param>
    /// <param name="errorMessage">Error message if the operation failed</param>
    /// <param name="renamedFrom">Original path for rename operations</param>
    /// <param name="renamedTo">New path for rename operations</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation</param>
    Task LogOperationAsync(
        string path,
        SyncActionType actionType,
        bool isDirectory,
        long size,
        ChangeSource source,
        DateTime startedAt,
        DateTime completedAt,
        bool success,
        string? errorMessage = null,
        string? renamedFrom = null,
        string? renamedTo = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets recent completed operations for activity history display.
    /// </summary>
    /// <param name="limit">Maximum number of operations to return (default: 100)</param>
    /// <param name="since">Only return operations completed after this time (optional)</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation</param>
    /// <returns>A collection of completed operations ordered by completion time descending</returns>
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
    Task<int> ClearOperationHistoryAsync(DateTime olderThan, CancellationToken cancellationToken = default);
}
