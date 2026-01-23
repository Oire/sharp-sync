using SQLite;
using Oire.SharpSync.Core;

namespace Oire.SharpSync.Database;

/// <summary>
/// SQLite implementation of the sync database for tracking file synchronization state.
/// </summary>
/// <remarks>
/// This class provides persistent storage for sync state using SQLite. It tracks file metadata,
/// sync status, and conflict information. The database is automatically created if it doesn't exist.
/// </remarks>
public class SqliteSyncDatabase: ISyncDatabase {
    private readonly string _databasePath;
    private SQLiteAsyncConnection? _connection;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="SqliteSyncDatabase"/> class.
    /// </summary>
    /// <param name="databasePath">The full path to the SQLite database file.</param>
    /// <exception cref="ArgumentNullException">Thrown when databasePath is null.</exception>
    public SqliteSyncDatabase(string databasePath) {
        ArgumentNullException.ThrowIfNull(databasePath);
        _databasePath = databasePath;
    }

    /// <summary>
    /// Initializes the database connection and creates necessary tables and indexes.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
    /// <remarks>
    /// This method must be called before using any other database operations.
    /// It creates the directory if it doesn't exist and sets up the database schema.
    /// </remarks>
    public async Task InitializeAsync(CancellationToken cancellationToken = default) {
        var directory = Path.GetDirectoryName(_databasePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory)) {
            Directory.CreateDirectory(directory);
        }

        _connection = new SQLiteAsyncConnection(_databasePath);

        await _connection.CreateTableAsync<SyncState>();
        await _connection.CreateTableAsync<OperationHistory>();
        await _connection.ExecuteAsync("""
            CREATE INDEX IF NOT EXISTS idx_syncstates_status
            ON SyncStates(Status)
            """);
        await _connection.ExecuteAsync("""
            CREATE INDEX IF NOT EXISTS idx_syncstates_lastsync
            ON SyncStates(LastSyncTime)
            """);
        await _connection.ExecuteAsync("""
            CREATE INDEX IF NOT EXISTS idx_operationhistory_completedat
            ON OperationHistory(CompletedAtTicks DESC)
            """);
    }

    /// <summary>
    /// Gets the synchronization state for a specific file path.
    /// </summary>
    /// <param name="path">The file path to look up.</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
    /// <returns>The sync state if found; otherwise, null.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the database is not initialized.</exception>
    public async Task<SyncState?> GetSyncStateAsync(string path, CancellationToken cancellationToken = default) {
        EnsureInitialized();
        return await _connection!.Table<SyncState>()
            .Where(s => s.Path == path)
            .FirstOrDefaultAsync();
    }

    /// <summary>
    /// Updates or inserts a synchronization state record.
    /// </summary>
    /// <param name="state">The sync state to update or insert.</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
    /// <remarks>
    /// If the state has Id = 0, it will be inserted as a new record.
    /// Otherwise, the existing record will be updated.
    /// </remarks>
    /// <exception cref="InvalidOperationException">Thrown when the database is not initialized.</exception>
    public async Task UpdateSyncStateAsync(SyncState state, CancellationToken cancellationToken = default) {
        EnsureInitialized();

        if (state.Id == 0) {
            await _connection!.InsertAsync(state);
        } else {
            await _connection!.UpdateAsync(state);
        }
    }

    /// <summary>
    /// Deletes the synchronization state for a specific file path
    /// </summary>
    /// <param name="path">The file path whose sync state should be deleted</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation</param>
    /// <exception cref="InvalidOperationException">Thrown when the database is not initialized</exception>
    public async Task DeleteSyncStateAsync(string path, CancellationToken cancellationToken = default) {
        EnsureInitialized();
        await _connection!.Table<SyncState>()
            .Where(s => s.Path == path)
            .DeleteAsync();
    }

    /// <summary>
    /// Retrieves all synchronization states from the database
    /// </summary>
    /// <param name="cancellationToken">Cancellation token to cancel the operation</param>
    /// <returns>A collection of all sync states</returns>
    /// <exception cref="InvalidOperationException">Thrown when the database is not initialized</exception>
    public async Task<IEnumerable<SyncState>> GetAllSyncStatesAsync(CancellationToken cancellationToken = default) {
        EnsureInitialized();
        return await _connection!.Table<SyncState>().ToListAsync();
    }

    /// <summary>
    /// Retrieves all synchronization states for paths matching a given prefix.
    /// Used for efficient folder-scoped queries in selective sync operations.
    /// </summary>
    /// <param name="pathPrefix">The path prefix to match (e.g., "Documents/Projects")</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation</param>
    /// <returns>All sync states where the path starts with the given prefix</returns>
    /// <exception cref="InvalidOperationException">Thrown when the database is not initialized</exception>
    public async Task<IEnumerable<SyncState>> GetSyncStatesByPrefixAsync(string pathPrefix, CancellationToken cancellationToken = default) {
        EnsureInitialized();

        // Normalize the prefix - ensure it ends without a slash for consistent matching
        var normalizedPrefix = pathPrefix.TrimEnd('/');

        // Use SQL LIKE for prefix matching - this is efficient with the Path index
        // Match exact folder or any path under the folder
        return await _connection!.QueryAsync<SyncState>(
            "SELECT * FROM SyncStates WHERE Path = ? OR Path LIKE ?",
            normalizedPrefix,
            normalizedPrefix + "/%");
    }

    /// <summary>
    /// Retrieves all synchronization states that require action (not synced or ignored)
    /// </summary>
    /// <param name="cancellationToken">Cancellation token to cancel the operation</param>
    /// <returns>A collection of sync states that are pending synchronization</returns>
    /// <exception cref="InvalidOperationException">Thrown when the database is not initialized</exception>
    public async Task<IEnumerable<SyncState>> GetPendingSyncStatesAsync(CancellationToken cancellationToken = default) {
        EnsureInitialized();
        return await _connection!.Table<SyncState>()
            .Where(s => s.Status != SyncStatus.Synced && s.Status != SyncStatus.Ignored)
            .ToListAsync();
    }

    /// <summary>
    /// Begins a database transaction for atomic operations.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
    /// <returns>A transaction object that can be used to commit or rollback changes.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the database is not initialized.</exception>
    public async Task<ISyncTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default) {
        EnsureInitialized();
        await Task.CompletedTask; // This method returns immediately but needs to be async for interface consistency
        return new SqliteSyncTransaction(_connection!);
    }

    /// <summary>
    /// Clears all synchronization state records from the database
    /// </summary>
    /// <param name="cancellationToken">Cancellation token to cancel the operation</param>
    /// <exception cref="InvalidOperationException">Thrown when the database is not initialized</exception>
    public async Task ClearAsync(CancellationToken cancellationToken = default) {
        EnsureInitialized();
        await _connection!.DeleteAllAsync<SyncState>();
    }

    /// <summary>
    /// Gets statistical information about the synchronization database
    /// </summary>
    /// <param name="cancellationToken">Cancellation token to cancel the operation</param>
    /// <returns>Database statistics including item counts, last sync time, and database size</returns>
    /// <exception cref="InvalidOperationException">Thrown when the database is not initialized</exception>
    public async Task<DatabaseStats> GetStatsAsync(CancellationToken cancellationToken = default) {
        EnsureInitialized();

        var totalItems = await _connection!.Table<SyncState>().CountAsync();
        var syncedItems = await _connection!.Table<SyncState>()
            .Where(s => s.Status == SyncStatus.Synced).CountAsync();
        var conflictedItems = await _connection!.Table<SyncState>()
            .Where(s => s.Status == SyncStatus.Conflict).CountAsync();
        var errorItems = await _connection!.Table<SyncState>()
            .Where(s => s.Status == SyncStatus.Error).CountAsync();
        var pendingItems = await _connection!.Table<SyncState>()
            .Where(s => s.Status == SyncStatus.LocalNew ||
                       s.Status == SyncStatus.RemoteNew ||
                       s.Status == SyncStatus.LocalModified ||
                       s.Status == SyncStatus.RemoteModified ||
                       s.Status == SyncStatus.LocalDeleted ||
                       s.Status == SyncStatus.RemoteDeleted).CountAsync();

        var lastSyncState = await _connection!.Table<SyncState>()
            .Where(s => s.LastSyncTime != null)
            .OrderByDescending(s => s.LastSyncTime)
            .FirstOrDefaultAsync();

        var fileInfo = new FileInfo(_databasePath);

        return new DatabaseStats {
            TotalItems = totalItems,
            SyncedItems = syncedItems,
            PendingItems = pendingItems,
            ConflictedItems = conflictedItems,
            ErrorItems = errorItems,
            DatabaseSize = fileInfo.Exists ? fileInfo.Length : 0,
            LastSyncTime = lastSyncState?.LastSyncTime
        };
    }

    /// <summary>
    /// Logs a completed synchronization operation for activity history.
    /// </summary>
    public async Task LogOperationAsync(
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
        CancellationToken cancellationToken = default) {
        EnsureInitialized();

        var record = OperationHistory.FromOperation(
            path,
            actionType,
            isDirectory,
            size,
            source,
            startedAt,
            completedAt,
            success,
            errorMessage,
            renamedFrom,
            renamedTo);

        await _connection!.InsertAsync(record);
    }

    /// <summary>
    /// Gets recent completed operations for activity history display.
    /// </summary>
    public async Task<IReadOnlyList<CompletedOperation>> GetRecentOperationsAsync(
        int limit = 100,
        DateTime? since = null,
        CancellationToken cancellationToken = default) {
        EnsureInitialized();

        List<OperationHistory> records;

        if (since.HasValue) {
            var sinceTicks = since.Value.Ticks;
            records = await _connection!.QueryAsync<OperationHistory>(
                "SELECT * FROM OperationHistory WHERE CompletedAtTicks > ? ORDER BY CompletedAtTicks DESC LIMIT ?",
                sinceTicks,
                limit);
        } else {
            records = await _connection!.QueryAsync<OperationHistory>(
                "SELECT * FROM OperationHistory ORDER BY CompletedAtTicks DESC LIMIT ?",
                limit);
        }

        return records.Select(r => r.ToCompletedOperation()).ToList();
    }

    /// <summary>
    /// Clears operation history older than the specified date.
    /// </summary>
    public async Task<int> ClearOperationHistoryAsync(DateTime olderThan, CancellationToken cancellationToken = default) {
        EnsureInitialized();

        var olderThanTicks = olderThan.Ticks;
        return await _connection!.ExecuteAsync(
            "DELETE FROM OperationHistory WHERE CompletedAtTicks < ?",
            olderThanTicks);
    }

    private void EnsureInitialized() {
        if (_connection is null) {
            throw new InvalidOperationException("Database not initialized. Call InitializeAsync first.");
        }
    }

    /// <summary>
    /// Releases all resources used by the sync database
    /// </summary>
    /// <remarks>
    /// Closes the database connection and disposes of all resources.
    /// This method can be called multiple times safely. After disposal, the database instance cannot be reused.
    /// </remarks>
    public void Dispose() {
        if (!_disposed) {
            _connection?.CloseAsync().Wait();
            _connection = null;
            _disposed = true;
        }
    }
}
