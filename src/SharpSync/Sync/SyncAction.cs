using Oire.SharpSync.Core;

namespace Oire.SharpSync.Sync;

/// <summary>
/// Represents a synchronization action to be performed
/// </summary>
internal sealed record SyncAction {
    public SyncActionType Type { get; init; }
    public string Path { get; init; } = string.Empty;
    public SyncItem? LocalItem { get; init; }
    public SyncItem? RemoteItem { get; init; }
    public ConflictType ConflictType { get; init; }
    public int Priority { get; init; }
}
