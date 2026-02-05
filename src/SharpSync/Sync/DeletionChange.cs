using Oire.SharpSync.Core;
using Oire.SharpSync.Database;

namespace Oire.SharpSync.Sync;

/// <summary>
/// Represents a deletion of a file or directory
/// </summary>
internal sealed class DeletionChange: IChange {
    public string Path { get; set; } = string.Empty;
    public bool DeletedLocally { get; set; }
    public bool DeletedRemotely { get; set; }
    public SyncState TrackedState { get; set; } = new();
}
