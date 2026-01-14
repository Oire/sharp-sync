namespace Oire.SharpSync.Core;

/// <summary>
/// Synchronization options for controlling the behavior of file sync operations
/// </summary>
public class SyncOptions {
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
    public bool FollowSymlinks { get; set; }

    /// <summary>
    /// Gets or sets whether to perform a dry run (no actual changes)
    /// </summary>
    public bool DryRun { get; set; }

    /// <summary>
    /// Gets or sets whether to enable verbose logging
    /// </summary>
    public bool Verbose { get; set; }

    /// <summary>
    /// Gets or sets whether to use checksum-only comparison (ignores timestamps)
    /// </summary>
    public bool ChecksumOnly { get; set; }

    /// <summary>
    /// Gets or sets whether to use size-only comparison (ignores timestamps and checksums)
    /// </summary>
    public bool SizeOnly { get; set; }

    /// <summary>
    /// Gets or sets whether to delete files in the target that don't exist in the source
    /// </summary>
    public bool DeleteExtraneous { get; set; }

    /// <summary>
    /// Gets or sets whether to update existing files (if false, skips existing files)
    /// </summary>
    public bool UpdateExisting { get; set; } = true;

    /// <summary>
    /// Gets or sets the conflict resolution strategy
    /// </summary>
    public ConflictResolution ConflictResolution { get; set; } = ConflictResolution.Ask;

    /// <summary>
    /// Gets or sets the synchronization timeout in seconds (0 = no timeout)
    /// </summary>
    public int TimeoutSeconds { get; set; }

    /// <summary>
    /// Gets or sets file patterns to exclude from synchronization
    /// </summary>
    public List<string> ExcludePatterns { get; set; } = new List<string>();

    /// <summary>
    /// Gets or sets the maximum transfer rate in bytes per second.
    /// Set to 0 or null for unlimited bandwidth.
    /// </summary>
    /// <remarks>
    /// This setting applies to both upload and download operations.
    /// Useful for preventing network saturation on shared connections.
    /// Example values:
    /// - 1_048_576 (1 MB/s)
    /// - 10_485_760 (10 MB/s)
    /// - 104_857_600 (100 MB/s)
    /// </remarks>
    public long? MaxBytesPerSecond { get; set; }

    /// <summary>
    /// Creates a copy of the sync options
    /// </summary>
    /// <returns>A new SyncOptions instance with the same values</returns>
    public SyncOptions Clone() {
        return new SyncOptions {
            PreservePermissions = PreservePermissions,
            PreserveTimestamps = PreserveTimestamps,
            FollowSymlinks = FollowSymlinks,
            DryRun = DryRun,
            Verbose = Verbose,
            ChecksumOnly = ChecksumOnly,
            SizeOnly = SizeOnly,
            DeleteExtraneous = DeleteExtraneous,
            UpdateExisting = UpdateExisting,
            ConflictResolution = ConflictResolution,
            TimeoutSeconds = TimeoutSeconds,
            ExcludePatterns = new List<string>(ExcludePatterns),
            MaxBytesPerSecond = MaxBytesPerSecond
        };
    }
}

