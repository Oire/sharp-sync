namespace Oire.SharpSync.Core;

/// <summary>
/// Types of sync operations
/// </summary>
public enum SyncOperation {
    /// <summary>
    /// Unknown or unspecified operation
    /// </summary>
    Unknown,

    /// <summary>
    /// Scanning files and directories to detect changes
    /// </summary>
    Scanning,

    /// <summary>
    /// Uploading files to remote storage
    /// </summary>
    Uploading,

    /// <summary>
    /// Downloading files from remote storage
    /// </summary>
    Downloading,

    /// <summary>
    /// Deleting files or directories
    /// </summary>
    Deleting,

    /// <summary>
    /// Creating a directory in storage
    /// </summary>
    CreatingDirectory,

    /// <summary>
    /// Resolving a synchronization conflict
    /// </summary>
    ResolvingConflict
}
