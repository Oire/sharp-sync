namespace Oire.SharpSync.Storage;

/// <summary>
/// Storage operation type
/// </summary>
public enum StorageOperation {
    /// <summary>
    /// Uploading a file
    /// </summary>
    Upload,

    /// <summary>
    /// Downloading a file
    /// </summary>
    Download,

    /// <summary>
    /// Deleting a file or directory
    /// </summary>
    Delete,

    /// <summary>
    /// Moving/renaming a file or directory
    /// </summary>
    Move
}
