namespace Oire.SharpSync.Storage;

/// <summary>
/// Storage operation progress event arguments
/// </summary>
public class StorageProgressEventArgs: EventArgs {
    /// <summary>
    /// Path of the file being processed
    /// </summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// Number of bytes transferred so far
    /// </summary>
    public long BytesTransferred { get; set; }

    /// <summary>
    /// Total number of bytes to transfer
    /// </summary>
    public long TotalBytes { get; set; }

    /// <summary>
    /// Operation being performed
    /// </summary>
    public StorageOperation Operation { get; set; }

    /// <summary>
    /// Percentage complete (0-100)
    /// </summary>
    public int PercentComplete { get; set; }
}
