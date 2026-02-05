namespace Oire.SharpSync.Core;

/// <summary>
/// Progress information for synchronization operations
/// </summary>
public record SyncProgress {
    /// <summary>
    /// Gets the number of items processed
    /// </summary>
    public int ProcessedItems { get; init; }

    /// <summary>
    /// Gets the total number of items to process
    /// </summary>
    public int TotalItems { get; init; }

    /// <summary>
    /// Gets the current item being processed
    /// </summary>
    public string? CurrentItem { get; init; }

    /// <summary>
    /// Gets the progress percentage (0-100)
    /// </summary>
    public double Percentage => TotalItems > 0 ? (double)ProcessedItems / TotalItems * 100.0 : 0.0;

    /// <summary>
    /// Gets whether the operation has been cancelled
    /// </summary>
    public bool IsCancelled { get; init; }

    /// <summary>
    /// Gets whether the operation is currently paused
    /// </summary>
    public bool IsPaused { get; init; }
}
