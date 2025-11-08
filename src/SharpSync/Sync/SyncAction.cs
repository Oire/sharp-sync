using Oire.SharpSync.Core;

namespace Oire.SharpSync.Sync;

/// <summary>
/// Represents a synchronization action to be performed
/// </summary>
internal sealed class SyncAction {
    public SyncActionType Type { get; set; }
    public string Path { get; set; } = string.Empty;
    public SyncItem? LocalItem { get; set; }
    public SyncItem? RemoteItem { get; set; }
    public ConflictType ConflictType { get; set; }
    public int Priority { get; set; }
}
