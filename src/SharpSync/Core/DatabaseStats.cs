namespace Oire.SharpSync.Core;

/// <summary>
/// Database statistics
/// </summary>
public record DatabaseStats {
    /// <summary>
    /// Total number of items tracked in database
    /// </summary>
    public int TotalItems { get; init; }

    /// <summary>
    /// Number of successfully synced items
    /// </summary>
    public int SyncedItems { get; init; }

    /// <summary>
    /// Number of items pending synchronization
    /// </summary>
    public int PendingItems { get; init; }

    /// <summary>
    /// Number of items with conflicts
    /// </summary>
    public int ConflictedItems { get; init; }

    /// <summary>
    /// Number of items with errors
    /// </summary>
    public int ErrorItems { get; init; }

    /// <summary>
    /// Database file size in bytes
    /// </summary>
    public long DatabaseSize { get; init; }

    /// <summary>
    /// Timestamp of last successful sync
    /// </summary>
    public DateTime? LastSyncTime { get; init; }
}
