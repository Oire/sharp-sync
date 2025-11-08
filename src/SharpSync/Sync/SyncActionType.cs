namespace Oire.SharpSync.Sync;

/// <summary>
/// Types of synchronization actions
/// </summary>
internal enum SyncActionType {
    Download,
    Upload,
    DeleteLocal,
    DeleteRemote,
    Conflict
}
