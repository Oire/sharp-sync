namespace Oire.SharpSync.Core;

/// <summary>
/// Conflict resolution strategies
/// </summary>
public enum ConflictResolution
{
    /// <summary>
    /// Ask for user input when conflicts occur
    /// </summary>
    Ask,

    /// <summary>
    /// Always use the local version
    /// </summary>
    UseLocal,

    /// <summary>
    /// Always use the remote version
    /// </summary>
    UseRemote,

    /// <summary>
    /// Skip conflicted files (leave unchanged)
    /// </summary>
    Skip,

    /// <summary>
    /// Rename the local file and download the remote file
    /// </summary>
    RenameLocal,

    /// <summary>
    /// Rename the remote file and upload the local file
    /// </summary>
    RenameRemote
}