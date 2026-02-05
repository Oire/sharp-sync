namespace Oire.SharpSync.Core;

/// <summary>
/// Represents a planned synchronization action that will be performed on a file or directory.
/// </summary>
/// <remarks>
/// This class is designed for desktop clients to display detailed sync previews to users before
/// synchronization begins. It provides all the information needed to show what will happen to each file.
/// </remarks>
public sealed record SyncPlanAction {
    /// <summary>
    /// Gets the type of synchronization action (download, upload, delete, etc.)
    /// </summary>
    public SyncActionType ActionType { get; init; }

    /// <summary>
    /// Gets the relative path of the file or directory affected by this action
    /// </summary>
    public string Path { get; init; } = string.Empty;

    /// <summary>
    /// Gets whether this action affects a directory (true) or a file (false)
    /// </summary>
    public bool IsDirectory { get; init; }

    /// <summary>
    /// Gets the size of the file in bytes (0 for directories or if unknown)
    /// </summary>
    public long Size { get; init; }

    /// <summary>
    /// Gets the last modification time of the file or directory (if available)
    /// </summary>
    public DateTime? LastModified { get; init; }

    /// <summary>
    /// Gets the type of conflict if this action represents a conflict, otherwise null
    /// </summary>
    public ConflictType? ConflictType { get; init; }

    /// <summary>
    /// Gets the priority of this action (higher number = higher priority)
    /// </summary>
    /// <remarks>
    /// Used internally for optimization. Desktop clients can use this to sort actions
    /// in a way that matches the actual synchronization order.
    /// </remarks>
    public int Priority { get; init; }

    /// <summary>
    /// Gets whether this download action will create a virtual file placeholder.
    /// </summary>
    /// <remarks>
    /// This is true when <see cref="SyncOptions.CreateVirtualFilePlaceholders"/> is enabled
    /// and this action is a download operation for a file (not directory).
    /// Desktop clients can use this to display placeholder indicators in their UI.
    /// </remarks>
    public bool WillCreateVirtualPlaceholder { get; init; }

    /// <summary>
    /// Gets the current virtual file state of the item (if applicable).
    /// </summary>
    /// <remarks>
    /// For existing local files, this indicates whether the file is currently
    /// a placeholder, hydrated, or a regular file. Useful for displaying
    /// file status in desktop client UIs.
    /// </remarks>
    public VirtualFileState CurrentVirtualState { get; init; }

}
