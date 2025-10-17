namespace Oire.SharpSync.Core;

/// <summary>
/// Result of a synchronization operation
/// </summary>
public class SyncResult
{
    /// <summary>
    /// Gets or sets whether the synchronization was successful
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Gets or sets the number of files that were synchronized
    /// </summary>
    public long FilesSynchronized { get; set; }

    /// <summary>
    /// Gets or sets the number of files that were skipped
    /// </summary>
    public long FilesSkipped { get; set; }

    /// <summary>
    /// Gets or sets the number of files that had conflicts
    /// </summary>
    public long FilesConflicted { get; set; }

    /// <summary>
    /// Gets or sets the number of files that were deleted
    /// </summary>
    public long FilesDeleted { get; set; }

    /// <summary>
    /// Gets or sets the total elapsed time for the operation
    /// </summary>
    public TimeSpan ElapsedTime { get; set; }

    /// <summary>
    /// Gets or sets any error that occurred during synchronization
    /// </summary>
    public Exception? Error { get; set; }

    /// <summary>
    /// Gets or sets additional details about the synchronization
    /// </summary>
    public string Details { get; set; } = string.Empty;

    /// <summary>
    /// Gets the total number of files processed
    /// </summary>
    public long TotalFilesProcessed => FilesSynchronized + FilesSkipped + FilesConflicted;
}