using Oire.SharpSync.Core;

namespace Oire.SharpSync.Sync;

/// <summary>
/// Represents a new file or directory addition
/// </summary>
internal sealed class AdditionChange: IChange {
    public string Path { get; set; } = string.Empty;
    public SyncItem Item { get; set; } = new();
    public bool IsLocal { get; set; }
}
