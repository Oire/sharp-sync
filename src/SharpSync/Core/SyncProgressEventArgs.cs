namespace Oire.SharpSync.Core;

/// <summary>
/// Progress event arguments for sync operations
/// </summary>
public class SyncProgressEventArgs: EventArgs {
    /// <summary>
    /// Gets the current progress information
    /// </summary>
    public SyncProgress Progress { get; }

    /// <summary>
    /// Gets the path of the item currently being processed, or null if not applicable
    /// </summary>
    public string? CurrentItem { get; }

    /// <summary>
    /// Gets the type of operation currently being performed
    /// </summary>
    public SyncOperation Operation { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="SyncProgressEventArgs"/> class
    /// </summary>
    /// <param name="progress">The current progress information</param>
    /// <param name="currentItem">The path of the item currently being processed</param>
    /// <param name="operation">The type of operation currently being performed</param>
    public SyncProgressEventArgs(SyncProgress progress, string? currentItem = null, SyncOperation operation = SyncOperation.Unknown) {
        Progress = progress;
        CurrentItem = currentItem;
        Operation = operation;
    }
}
