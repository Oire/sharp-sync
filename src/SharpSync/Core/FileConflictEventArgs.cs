namespace Oire.SharpSync.Core;

/// <summary>
/// Event arguments for file conflict events
/// </summary>
public class FileConflictEventArgs: EventArgs {
    /// <summary>
    /// Gets the path of the conflicted file
    /// </summary>
    public string Path { get; }

    /// <summary>
    /// Gets the local item (may be null if deleted locally)
    /// </summary>
    public SyncItem? LocalItem { get; }

    /// <summary>
    /// Gets the remote item (may be null if deleted remotely)
    /// </summary>
    public SyncItem? RemoteItem { get; }

    /// <summary>
    /// Gets the type of conflict
    /// </summary>
    public ConflictType ConflictType { get; }

    /// <summary>
    /// Gets or sets the resolution for this conflict
    /// </summary>
    public ConflictResolution Resolution { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="FileConflictEventArgs"/> class
    /// </summary>
    /// <param name="path">The path of the conflicted file</param>
    /// <param name="localItem">The local item (may be null if deleted locally)</param>
    /// <param name="remoteItem">The remote item (may be null if deleted remotely)</param>
    /// <param name="conflictType">The type of conflict</param>
    public FileConflictEventArgs(string path, SyncItem? localItem, SyncItem? remoteItem, ConflictType conflictType) {
        Path = path;
        LocalItem = localItem;
        RemoteItem = remoteItem;
        ConflictType = conflictType;
        Resolution = ConflictResolution.Ask;
    }
}
