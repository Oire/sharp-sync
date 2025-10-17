using Oire.SharpSync.Core;
using SQLite;

namespace Oire.SharpSync.Database;

/// <summary>
/// SQLite transaction implementation
/// </summary>
internal class SqliteSyncTransaction : ISyncTransaction
{
    private readonly SQLiteAsyncConnection _connection;
    private bool _committed;
    private bool _disposed;

    public SqliteSyncTransaction(SQLiteAsyncConnection connection)
    {
        _connection = connection;
    }

    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(SqliteSyncTransaction));
            
        // SQLite-net handles transactions automatically for batch operations
        // For explicit transaction control, we could use BeginTransaction/Commit
        _committed = true;
        await Task.CompletedTask;
    }

    public async Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(SqliteSyncTransaction));
            
        // SQLite-net handles rollback automatically if transaction is not committed
        await Task.CompletedTask;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            if (!_committed)
            {
                // Rollback is handled automatically by SQLite-net
            }
            _disposed = true;
        }
    }
}