namespace Oire.SharpSync.Sync;

/// <summary>
/// Organizes sync actions into optimized processing groups
/// </summary>
internal sealed class ActionGroups {
    public List<SyncAction> Directories { get; } = [];
    public List<SyncAction> SmallFiles { get; } = [];
    public List<SyncAction> LargeFiles { get; } = [];
    public List<SyncAction> Deletes { get; } = [];
    public List<SyncAction> Conflicts { get; } = [];

    public void SortByPriority() {
        Directories.Sort((a, b) => b.Priority.CompareTo(a.Priority));
        SmallFiles.Sort((a, b) => b.Priority.CompareTo(a.Priority));
        LargeFiles.Sort((a, b) => b.Priority.CompareTo(a.Priority));
        Deletes.Sort((a, b) => b.Priority.CompareTo(a.Priority));
        Conflicts.Sort((a, b) => b.Priority.CompareTo(a.Priority));
    }

    public int TotalActions => Directories.Count + SmallFiles.Count + LargeFiles.Count + Deletes.Count + Conflicts.Count;
}
