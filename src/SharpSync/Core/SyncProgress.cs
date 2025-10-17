namespace Oire.SharpSync.Core;

/// <summary>
/// Progress information for synchronization operations
/// </summary>
public record SyncProgress
{
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
    /// Gets the current file number being processed (for backward compatibility)
    /// </summary>
    public long CurrentFile => ProcessedItems;

    /// <summary>
    /// Gets the total number of files to process (for backward compatibility)
    /// </summary>
    public long TotalFiles => TotalItems;

    /// <summary>
    /// Gets the current filename being processed (for backward compatibility)
    /// </summary>
    public string CurrentFileName => CurrentItem ?? string.Empty;

    /// <summary>
    /// Gets the progress percentage (0-100)
    /// </summary>
    public double Percentage => TotalItems > 0 ? (double)ProcessedItems / TotalItems * 100.0 : 0.0;

    /// <summary>
    /// Gets whether the operation has been cancelled
    /// </summary>
    public bool IsCancelled { get; init; }
}