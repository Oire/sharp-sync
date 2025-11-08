namespace Oire.SharpSync.Core;

/// <summary>
/// Defines the types of synchronization actions that can be performed on files and directories.
/// </summary>
/// <remarks>
/// These action types are used in sync plans to indicate what operation will be performed on each item.
/// Desktop clients can use this information to show users detailed previews before synchronization.
/// </remarks>
public enum SyncActionType {
    /// <summary>
    /// Download a file or directory from remote storage to local storage
    /// </summary>
    Download,

    /// <summary>
    /// Upload a file or directory from local storage to remote storage
    /// </summary>
    Upload,

    /// <summary>
    /// Delete a file or directory from local storage
    /// </summary>
    DeleteLocal,

    /// <summary>
    /// Delete a file or directory from remote storage
    /// </summary>
    DeleteRemote,

    /// <summary>
    /// A conflict exists and requires resolution
    /// </summary>
    Conflict
}
