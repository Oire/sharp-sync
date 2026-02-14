namespace Oire.SharpSync.Storage;

/// <summary>
/// Storage operation progress event arguments
/// </summary>
public class StorageProgressEventArgs: EventArgs {
    /// <summary>
    /// Path of the file being processed
    /// </summary>
    public string Path { get; init; } = string.Empty;

    /// <summary>
    /// Number of bytes transferred so far
    /// </summary>
    public long BytesTransferred { get; init; }

    /// <summary>
    /// Total number of bytes to transfer
    /// </summary>
    public long TotalBytes { get; init; }

    /// <summary>
    /// Operation being performed
    /// </summary>
    public StorageOperation Operation { get; init; }

    /// <summary>
    /// Percentage complete (0-100)
    /// </summary>
    public int PercentComplete { get; init; }
}
