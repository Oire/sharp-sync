namespace Oire.SharpSync.Core;

/// <summary>
/// Represents the virtual file state for cloud file systems (e.g., Windows Cloud Files API)
/// </summary>
public enum VirtualFileState {
    /// <summary>
    /// Not a virtual file - the file is fully present on disk
    /// </summary>
    None,

    /// <summary>
    /// Placeholder file - only metadata is stored locally, content is on remote storage
    /// </summary>
    /// <remarks>
    /// The file appears in the file system but its content is not downloaded.
    /// Accessing the file will trigger on-demand download (hydration).
    /// </remarks>
    Placeholder,

    /// <summary>
    /// Hydrated file - content has been downloaded and is available locally
    /// </summary>
    /// <remarks>
    /// The file was previously a placeholder but has been fully downloaded.
    /// It may still be tracked by the cloud file system for dehydration.
    /// </remarks>
    Hydrated,

    /// <summary>
    /// Partially hydrated - some content is available locally (e.g., during streaming)
    /// </summary>
    Partial
}
