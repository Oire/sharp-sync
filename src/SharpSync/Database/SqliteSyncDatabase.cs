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
        await _connection.ExecuteAsync("""
            CREATE INDEX IF NOT EXISTS idx_syncstates_status 
            ON SyncStates(Status)
            """);
        await _connection.ExecuteAsync("""
            CREATE INDEX IF NOT EXISTS idx_syncstates_lastsync 
            ON SyncStates(LastSyncTime)
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

    public async Task DeleteSyncStateAsync(string path, CancellationToken cancellationToken = default) {
        EnsureInitialized();
        await _connection!.Table<SyncState>()
            .Where(s => s.Path == path)
            .DeleteAsync();
    }

    public async Task<IEnumerable<SyncState>> GetAllSyncStatesAsync(CancellationToken cancellationToken = default) {
        EnsureInitialized();
        return await _connection!.Table<SyncState>().ToListAsync();
    }

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

    public async Task ClearAsync(CancellationToken cancellationToken = default) {
        EnsureInitialized();
        await _connection!.DeleteAllAsync<SyncState>();
    }

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
            .Where(s => s.LastSyncTime is not null)
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

    private void EnsureInitialized() {
        if (_connection is null) {
            throw new InvalidOperationException("Database not initialized. Call InitializeAsync first.");
        }
    }

    public void Dispose() {
        if (!_disposed) {
            _connection?.CloseAsync().Wait();
            _connection = null;
            _disposed = true;
        }
    }
}
