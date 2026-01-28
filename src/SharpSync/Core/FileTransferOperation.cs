namespace Oire.SharpSync.Core;

/// <summary>
/// Type of file transfer operation for per-file progress reporting.
/// </summary>
public enum FileTransferOperation {
    /// <summary>
    /// Uploading a file to remote storage.
    /// </summary>
    Upload,

    /// <summary>
    /// Downloading a file from remote storage.
    /// </summary>
    Download
}
