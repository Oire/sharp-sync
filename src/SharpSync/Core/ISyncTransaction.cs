namespace Oire.SharpSync.Core;

/// <summary>
/// Represents a transaction for batch operations
/// </summary>
public interface ISyncTransaction : IDisposable
{
    /// <summary>
    /// Commits the transaction
    /// </summary>
    Task CommitAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Rolls back the transaction
    /// </summary>
    Task RollbackAsync(CancellationToken cancellationToken = default);
}