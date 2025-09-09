namespace SharpSync;

/// <summary>
/// Synchronization options for controlling the behavior of file sync operations
/// </summary>
public class SyncOptions
{
    /// <summary>
    /// Gets or sets whether to preserve file permissions during sync
    /// </summary>
    public bool PreservePermissions { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to preserve file timestamps during sync
    /// </summary>
    public bool PreserveTimestamps { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to follow symbolic links during sync
    /// </summary>
    public bool FollowSymlinks { get; set; } = false;

    /// <summary>
    /// Gets or sets whether to perform a dry run (no actual changes)
    /// </summary>
    public bool DryRun { get; set; } = false;

    /// <summary>
    /// Gets or sets whether to enable verbose logging
    /// </summary>
    public bool Verbose { get; set; } = false;

    /// <summary>
    /// Gets or sets whether to use checksum-only comparison (ignores timestamps)
    /// </summary>
    public bool ChecksumOnly { get; set; } = false;

    /// <summary>
    /// Gets or sets whether to use size-only comparison (ignores timestamps and checksums)
    /// </summary>
    public bool SizeOnly { get; set; } = false;

    /// <summary>
    /// Gets or sets whether to delete files in the target that don't exist in the source
    /// </summary>
    public bool DeleteExtraneous { get; set; } = false;

    /// <summary>
    /// Gets or sets whether to update existing files (if false, skips existing files)
    /// </summary>
    public bool UpdateExisting { get; set; } = true;

    /// <summary>
    /// Gets or sets the conflict resolution strategy
    /// </summary>
    public ConflictResolution ConflictResolution { get; set; } = ConflictResolution.Ask;

    /// <summary>
    /// Creates a copy of the sync options
    /// </summary>
    /// <returns>A new SyncOptions instance with the same values</returns>
    public SyncOptions Clone()
    {
        return new SyncOptions
        {
            PreservePermissions = PreservePermissions,
            PreserveTimestamps = PreserveTimestamps,
            FollowSymlinks = FollowSymlinks,
            DryRun = DryRun,
            Verbose = Verbose,
            ChecksumOnly = ChecksumOnly,
            SizeOnly = SizeOnly,
            DeleteExtraneous = DeleteExtraneous,
            UpdateExisting = UpdateExisting,
            ConflictResolution = ConflictResolution
        };
    }
}

/// <summary>
/// Conflict resolution strategies
/// </summary>
public enum ConflictResolution
{
    /// <summary>
    /// Ask for user input when conflicts occur (default)
    /// </summary>
    Ask,

    /// <summary>
    /// Always use the source file when conflicts occur
    /// </summary>
    UseSource,

    /// <summary>
    /// Always use the target file when conflicts occur
    /// </summary>
    UseTarget,

    /// <summary>
    /// Skip conflicted files (leave unchanged)
    /// </summary>
    Skip,

    /// <summary>
    /// Merge files when possible, otherwise ask
    /// </summary>
    Merge
}

/// <summary>
/// Progress information for synchronization operations
/// </summary>
public class SyncProgress
{
    /// <summary>
    /// Gets or sets the current file number being processed
    /// </summary>
    public long CurrentFile { get; set; }

    /// <summary>
    /// Gets or sets the total number of files to process
    /// </summary>
    public long TotalFiles { get; set; }

    /// <summary>
    /// Gets or sets the current filename being processed
    /// </summary>
    public string CurrentFileName { get; set; } = string.Empty;

    /// <summary>
    /// Gets the progress percentage (0-100)
    /// </summary>
    public double Percentage => TotalFiles > 0 ? (double)CurrentFile / TotalFiles * 100.0 : 0.0;

    /// <summary>
    /// Gets or sets whether the operation has been cancelled
    /// </summary>
    public bool IsCancelled { get; set; }

    /// <summary>
    /// Creates a copy of the progress information
    /// </summary>
    /// <returns>A new SyncProgress instance with the same values</returns>
    public SyncProgress Clone()
    {
        return new SyncProgress
        {
            CurrentFile = CurrentFile,
            TotalFiles = TotalFiles,
            CurrentFileName = CurrentFileName,
            IsCancelled = IsCancelled
        };
    }
}

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