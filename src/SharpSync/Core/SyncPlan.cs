namespace Oire.SharpSync.Core;

/// <summary>
/// Represents a detailed plan of synchronization actions that will be performed.
/// </summary>
/// <remarks>
/// This class provides desktop clients with comprehensive information about what will happen
/// during synchronization, allowing users to review and understand changes before they are applied.
/// The plan groups actions by type for easier presentation in UI.
/// </remarks>
public sealed class SyncPlan {
    /// <summary>
    /// Gets all planned actions, sorted by priority (highest first)
    /// </summary>
    public IReadOnlyList<SyncPlanAction> Actions { get; init; } = Array.Empty<SyncPlanAction>();

    /// <summary>
    /// Gets actions that will download files or directories from remote to local
    /// </summary>
    public IReadOnlyList<SyncPlanAction> Downloads => Actions.Where(a => a.ActionType == SyncActionType.Download).ToList();

    /// <summary>
    /// Gets actions that will upload files or directories from local to remote
    /// </summary>
    public IReadOnlyList<SyncPlanAction> Uploads => Actions.Where(a => a.ActionType == SyncActionType.Upload).ToList();

    /// <summary>
    /// Gets actions that will delete files or directories from local storage
    /// </summary>
    public IReadOnlyList<SyncPlanAction> LocalDeletes => Actions.Where(a => a.ActionType == SyncActionType.DeleteLocal).ToList();

    /// <summary>
    /// Gets actions that will delete files or directories from remote storage
    /// </summary>
    public IReadOnlyList<SyncPlanAction> RemoteDeletes => Actions.Where(a => a.ActionType == SyncActionType.DeleteRemote).ToList();

    /// <summary>
    /// Gets actions representing conflicts that need resolution
    /// </summary>
    public IReadOnlyList<SyncPlanAction> Conflicts => Actions.Where(a => a.ActionType == SyncActionType.Conflict).ToList();

    /// <summary>
    /// Gets the total number of planned actions
    /// </summary>
    public int TotalActions => Actions.Count;

    /// <summary>
    /// Gets the total number of files that will be downloaded
    /// </summary>
    public int DownloadCount => Downloads.Count;

    /// <summary>
    /// Gets the total number of files that will be uploaded
    /// </summary>
    public int UploadCount => Uploads.Count;

    /// <summary>
    /// Gets the total number of deletions (both local and remote)
    /// </summary>
    public int DeleteCount => LocalDeletes.Count + RemoteDeletes.Count;

    /// <summary>
    /// Gets the total number of conflicts
    /// </summary>
    public int ConflictCount => Conflicts.Count;

    /// <summary>
    /// Gets the total size of data that will be downloaded (in bytes)
    /// </summary>
    public long TotalDownloadSize => Downloads.Where(a => !a.IsDirectory).Sum(a => a.Size);

    /// <summary>
    /// Gets the total size of data that will be uploaded (in bytes)
    /// </summary>
    public long TotalUploadSize => Uploads.Where(a => !a.IsDirectory).Sum(a => a.Size);

    /// <summary>
    /// Gets whether this plan has any actions to perform
    /// </summary>
    public bool HasChanges => TotalActions > 0;

    /// <summary>
    /// Gets whether this plan contains any conflicts
    /// </summary>
    public bool HasConflicts => ConflictCount > 0;
}
