using SQLite;
using Oire.SharpSync.Core;

namespace Oire.SharpSync.Database;

/// <summary>
/// SQLite implementation of the sync database
/// </summary>
public class SqliteSyncDatabase : ISyncDatabase
{
    private readonly string _databasePath;
    private SQLiteAsyncConnection? _connection;
    private bool _disposed;

    public SqliteSyncDatabase(string databasePath)
    {
        _databasePath = databasePath ?? throw new ArgumentNullException(nameof(databasePath));
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(_databasePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            Directory.CreateDirectory(directory);

        _connection = new SQLiteAsyncConnection(_databasePath);
        
        await _connection.CreateTableAsync<SyncState>();
        await _connection.ExecuteAsync(@"
            CREATE INDEX IF NOT EXISTS idx_syncstates_status 
            ON SyncStates(Status)");
        await _connection.ExecuteAsync(@"
            CREATE INDEX IF NOT EXISTS idx_syncstates_lastsync 
            ON SyncStates(LastSyncTime)");
    }

    public async Task<SyncState?> GetSyncStateAsync(string path, CancellationToken cancellationToken = default)
    {
        EnsureInitialized();
        return await _connection!.Table<SyncState>()
            .Where(s => s.Path == path)
            .FirstOrDefaultAsync();
    }

    public async Task UpdateSyncStateAsync(SyncState state, CancellationToken cancellationToken = default)
    {
        EnsureInitialized();
        
        if (state.Id == 0)
        {
            await _connection!.InsertAsync(state);
        }
        else
        {
            await _connection!.UpdateAsync(state);
        }
    }

    public async Task DeleteSyncStateAsync(string path, CancellationToken cancellationToken = default)
    {
        EnsureInitialized();
        await _connection!.Table<SyncState>()
            .Where(s => s.Path == path)
            .DeleteAsync();
    }

    public async Task<IEnumerable<SyncState>> GetAllSyncStatesAsync(CancellationToken cancellationToken = default)
    {
        EnsureInitialized();
        return await _connection!.Table<SyncState>().ToListAsync();
    }

    public async Task<IEnumerable<SyncState>> GetPendingSyncStatesAsync(CancellationToken cancellationToken = default)
    {
        EnsureInitialized();
        return await _connection!.Table<SyncState>()
            .Where(s => s.Status != SyncStatus.Synced && s.Status != SyncStatus.Ignored)
            .ToListAsync();
    }

    public async Task<ISyncTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        EnsureInitialized();
        return new SqliteSyncTransaction(_connection!);
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        EnsureInitialized();
        await _connection!.DeleteAllAsync<SyncState>();
    }

    public async Task<DatabaseStats> GetStatsAsync(CancellationToken cancellationToken = default)
    {
        EnsureInitialized();
        
        var totalItems = await _connection!.Table<SyncState>().CountAsync();
        var syncedItems = await _connection!.Table<SyncState>()
            .Where(s => s.Status == SyncStatus.Synced).CountAsync();
        var conflictedItems = await _connection!.Table<SyncState>()
            .Where(s => s.Status == SyncStatus.Conflict).CountAsync();
        var errorItems = await _connection!.Table<SyncState>()
            .Where(s => s.Status == SyncStatus.Error).CountAsync();
        var pendingItems = totalItems - syncedItems;
        
        var lastSyncState = await _connection!.Table<SyncState>()
            .Where(s => s.LastSyncTime != null)
            .OrderByDescending(s => s.LastSyncTime)
            .FirstOrDefaultAsync();

        var fileInfo = new FileInfo(_databasePath);
        
        return new DatabaseStats
        {
            TotalItems = totalItems,
            SyncedItems = syncedItems,
            PendingItems = pendingItems,
            ConflictedItems = conflictedItems,
            ErrorItems = errorItems,
            DatabaseSize = fileInfo.Exists ? fileInfo.Length : 0,
            LastSyncTime = lastSyncState?.LastSyncTime
        };
    }

    private void EnsureInitialized()
    {
        if (_connection == null)
            throw new InvalidOperationException("Database not initialized. Call InitializeAsync first.");
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _connection?.CloseAsync().Wait();
            _connection = null;
            _disposed = true;
        }
    }
}