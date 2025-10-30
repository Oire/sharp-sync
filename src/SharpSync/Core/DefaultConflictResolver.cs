namespace Oire.SharpSync.Core;

/// <summary>
/// Default implementation of IConflictResolver that always returns a predetermined resolution
/// </summary>
public class DefaultConflictResolver: IConflictResolver {
    /// <summary>
    /// Gets the default resolution strategy
    /// </summary>
    public ConflictResolution DefaultResolution { get; }

    /// <summary>
    /// Initializes a new instance of DefaultConflictResolver with the specified default resolution
    /// </summary>
    /// <param name="defaultResolution">The resolution to use for all conflicts</param>
    public DefaultConflictResolver(ConflictResolution defaultResolution) {
        DefaultResolution = defaultResolution;
    }

    /// <summary>
    /// Resolves a conflict using the predetermined resolution strategy
    /// </summary>
    /// <param name="conflict">The conflict event arguments</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The configured default resolution</returns>
    public async Task<ConflictResolution> ResolveConflictAsync(FileConflictEventArgs conflict, CancellationToken cancellationToken = default) {
        cancellationToken.ThrowIfCancellationRequested();

        // For this implementation, we always return the default resolution
        // regardless of the conflict details
        await Task.CompletedTask; // Make it truly async for interface compliance

        return DefaultResolution;
    }
}
