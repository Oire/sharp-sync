namespace Oire.SharpSync.Core;

/// <summary>
/// Sync status for items
/// </summary>
public enum SyncStatus
{
    /// <summary>
    /// Item is synchronized
    /// </summary>
    Synced,

    /// <summary>
    /// Item needs to be uploaded
    /// </summary>
    LocalNew,

    /// <summary>
    /// Item needs to be downloaded
    /// </summary>
    RemoteNew,

    /// <summary>
    /// Local file has been modified
    /// </summary>
    LocalModified,

    /// <summary>
    /// Remote file has been modified
    /// </summary>
    RemoteModified,

    /// <summary>
    /// Item has been deleted locally
    /// </summary>
    LocalDeleted,

    /// <summary>
    /// Item has been deleted remotely
    /// </summary>
    RemoteDeleted,

    /// <summary>
    /// Conflict detected
    /// </summary>
    Conflict,

    /// <summary>
    /// Error during sync
    /// </summary>
    Error,

    /// <summary>
    /// Item is ignored by sync rules
    /// </summary>
    Ignored
}