namespace Oire.SharpSync.Core;

/// <summary>
/// Rich analysis of a file conflict for UI decision making
/// </summary>
public record ConflictAnalysis {
    /// <summary>
    /// Path to the conflicted file
    /// </summary>
    public required string FilePath { get; init; }

    /// <summary>
    /// Type of conflict detected
    /// </summary>
    public required ConflictType ConflictType { get; init; }

    /// <summary>
    /// Local file information
    /// </summary>
    public SyncItem? LocalItem { get; init; }

    /// <summary>
    /// Remote file information
    /// </summary>
    public SyncItem? RemoteItem { get; init; }

    /// <summary>
    /// Recommended resolution based on analysis
    /// </summary>
    public ConflictResolution RecommendedResolution { get; init; }

    /// <summary>
    /// Local file size in bytes
    /// </summary>
    public long LocalSize { get; init; }

    /// <summary>
    /// Remote file size in bytes
    /// </summary>
    public long RemoteSize { get; init; }

    /// <summary>
    /// Absolute difference in file sizes
    /// </summary>
    public long SizeDifference { get; init; }

    /// <summary>
    /// Local file modification time
    /// </summary>
    public DateTime? LocalModified { get; init; }

    /// <summary>
    /// Remote file modification time
    /// </summary>
    public DateTime? RemoteModified { get; init; }

    /// <summary>
    /// Absolute difference in modification times
    /// </summary>
    public TimeSpan TimeDifference { get; init; }

    /// <summary>
    /// Which version appears to be newer ("Local", "Remote", or null if unclear)
    /// </summary>
    public string? NewerVersion { get; init; }

    /// <summary>
    /// Whether the file is likely binary (affects merge possibilities)
    /// </summary>
    public bool IsLikelyBinary { get; init; }

    /// <summary>
    /// Whether the file is likely text (merge might be possible)
    /// </summary>
    public bool IsLikelyTextFile { get; init; }
}
