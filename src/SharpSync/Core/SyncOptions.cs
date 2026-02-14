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
    public IList<string> ExcludePatterns { get; set; } = [];

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
    /// Gets or sets whether to create virtual file placeholders after downloading files.
    /// </summary>
    /// <remarks>
    /// When enabled, the <see cref="VirtualFileCallback"/> will be invoked after each
    /// successful file download. This allows desktop clients to integrate with
    /// platform-specific virtual file systems like Windows Cloud Files API.
    /// </remarks>
    public bool CreateVirtualFilePlaceholders { get; set; }

    /// <summary>
    /// Gets or sets the callback invoked after a file is downloaded to create a virtual file placeholder.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This callback is only invoked when <see cref="CreateVirtualFilePlaceholders"/> is true
    /// and a file (not directory) is successfully downloaded to local storage.
    /// </para>
    /// <para>
    /// The callback receives the relative path, full local path, and file metadata,
    /// allowing the application to convert the file to a cloud files placeholder.
    /// </para>
    /// <para>
    /// If the callback throws an exception, the error is logged but sync continues.
    /// The file will remain fully hydrated (not converted to placeholder).
    /// </para>
    /// </remarks>
    public VirtualFileCallbackDelegate? VirtualFileCallback { get; set; }

    /// <summary>
    /// Creates a deep copy of the sync options.
    /// </summary>
    /// <returns>A new SyncOptions instance with the same values.</returns>
    public SyncOptions Clone() {
        var clone = (SyncOptions)MemberwiseClone();
        clone.ExcludePatterns = [.. ExcludePatterns];
        return clone;
    }
}

