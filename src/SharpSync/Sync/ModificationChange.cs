using Oire.SharpSync.Core;
using Oire.SharpSync.Database;

namespace Oire.SharpSync.Sync;

/// <summary>
/// Represents a modification to an existing file or directory
/// </summary>
internal sealed class ModificationChange: IChange {
    public string Path { get; set; } = string.Empty;
    public SyncItem Item { get; set; } = new();
    public bool IsLocal { get; set; }
    public SyncState TrackedState { get; set; } = new();
}
