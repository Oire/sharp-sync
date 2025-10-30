namespace Oire.SharpSync.Core;

/// <summary>
/// Storage space information
/// </summary>
public record StorageInfo {
    /// <summary>
    /// Total space in bytes
    /// </summary>
    public required long TotalSpace { get; init; }

    /// <summary>
    /// Used space in bytes
    /// </summary>
    public required long UsedSpace { get; init; }

    /// <summary>
    /// Available space in bytes
    /// </summary>
    public long AvailableSpace => TotalSpace - UsedSpace;

    /// <summary>
    /// Storage quota if applicable
    /// </summary>
    public long? Quota { get; init; }
}
