using Oire.SharpSync.Core;

namespace Oire.SharpSync.Sync;

/// <summary>
/// Represents a set of changes detected during synchronization
/// </summary>
internal sealed class ChangeSet {
    public List<AdditionChange> Additions { get; } = [];
    public List<ModificationChange> Modifications { get; } = [];
    public List<DeletionChange> Deletions { get; } = [];
    public HashSet<string> ProcessedPaths { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> LocalPaths { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> RemotePaths { get; } = new(StringComparer.OrdinalIgnoreCase);

    public int TotalChanges => Additions.Count + Modifications.Count + Deletions.Count;
}
