namespace Oire.SharpSync.Core;

/// <summary>
/// Progress event arguments for sync operations
/// </summary>
public class SyncProgressEventArgs : EventArgs
{
    public SyncProgress Progress { get; }
    public string? CurrentItem { get; }
    public SyncOperation Operation { get; }

    public SyncProgressEventArgs(SyncProgress progress, string? currentItem = null, SyncOperation operation = SyncOperation.Unknown)
    {
        Progress = progress;
        CurrentItem = currentItem;
        Operation = operation;
    }
}