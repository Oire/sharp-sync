namespace Oire.SharpSync.Core;

/// <summary>
/// Interface for filtering files during synchronization
/// </summary>
public interface ISyncFilter {
    /// <summary>
    /// Determines whether a file or directory should be synchronized
    /// </summary>
    /// <param name="path">The relative path of the item</param>
    /// <returns>True if the item should be synced, false otherwise</returns>
    bool ShouldSync(string path);

    /// <summary>
    /// Adds an exclusion pattern
    /// </summary>
    /// <param name="pattern">Pattern to exclude (supports wildcards)</param>
    void AddExclusionPattern(string pattern);

    /// <summary>
    /// Adds an inclusion pattern
    /// </summary>
    /// <param name="pattern">Pattern to include (supports wildcards)</param>
    void AddInclusionPattern(string pattern);

    /// <summary>
    /// Clears all patterns
    /// </summary>
    void Clear();
}
