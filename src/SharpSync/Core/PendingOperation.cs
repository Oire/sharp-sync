namespace Oire.SharpSync.Core;

/// <summary>
/// Represents a pending synchronization operation.
/// Used by desktop clients to display what operations are queued for the next sync.
/// </summary>
public record PendingOperation {
    /// <summary>
    /// The relative path of the file or directory
    /// </summary>
    public required string Path { get; init; }

    /// <summary>
    /// The type of operation that will be performed
    /// </summary>
    public required SyncActionType ActionType { get; init; }

    /// <summary>
    /// Whether the item is a directory
    /// </summary>
    public bool IsDirectory { get; init; }

    /// <summary>
    /// The size of the file in bytes (0 for directories or deletions)
    /// </summary>
    public long Size { get; init; }

    /// <summary>
    /// When the change was detected
    /// </summary>
    public DateTime DetectedAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// The source of the change (Local or Remote)
    /// </summary>
    public ChangeSource Source { get; init; }

    /// <summary>
    /// Optional reason or additional context for the operation
    /// </summary>
    public string? Reason { get; init; }

    /// <summary>
    /// For rename operations, the original path before the rename.
    /// Null for non-rename operations.
    /// </summary>
    /// <remarks>
    /// When a file is renamed, two operations are created:
    /// <list type="bullet">
    /// <item>A delete operation for the old path (with <see cref="RenamedTo"/> set)</item>
    /// <item>An upload operation for the new path (with <see cref="RenamedFrom"/> set)</item>
    /// </list>
    /// Desktop clients can use these properties to display renames as a single operation.
    /// </remarks>
    public string? RenamedFrom { get; init; }

    /// <summary>
    /// For rename operations, the new path after the rename.
    /// Null for non-rename operations.
    /// </summary>
    public string? RenamedTo { get; init; }

    /// <summary>
    /// Indicates whether this operation is part of a rename
    /// </summary>
    public bool IsRename => RenamedFrom is not null || RenamedTo is not null;
}
