namespace Oire.SharpSync.Core;

/// <summary>
/// Interface for resolving file conflicts during synchronization
/// </summary>
public interface IConflictResolver {
    /// <summary>
    /// Resolves a file conflict asynchronously
    /// </summary>
    /// <param name="conflict">The conflict to resolve</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The resolution strategy</returns>
    Task<ConflictResolution> ResolveConflictAsync(FileConflictEventArgs conflict, CancellationToken cancellationToken = default);
}
