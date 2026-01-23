namespace Oire.SharpSync.Core;

/// <summary>
/// Represents a completed synchronization operation for activity history.
/// Desktop clients can use this to display recent sync activity in their UI.
/// </summary>
public record CompletedOperation {
    /// <summary>
    /// Unique identifier for this operation
    /// </summary>
    public long Id { get; init; }

    /// <summary>
    /// The relative path of the file or directory that was synchronized
    /// </summary>
    public required string Path { get; init; }

    /// <summary>
    /// The type of operation that was performed
    /// </summary>
    public required SyncActionType ActionType { get; init; }

    /// <summary>
    /// Whether the item is a directory
    /// </summary>
    public bool IsDirectory { get; init; }

    /// <summary>
    /// The size of the file in bytes (0 for directories)
    /// </summary>
    public long Size { get; init; }

    /// <summary>
    /// The source of the change that triggered this operation
    /// </summary>
    public ChangeSource Source { get; init; }

    /// <summary>
    /// When the operation started
    /// </summary>
    public DateTime StartedAt { get; init; }

    /// <summary>
    /// When the operation completed
    /// </summary>
    public DateTime CompletedAt { get; init; }

    /// <summary>
    /// Whether the operation completed successfully
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// Error message if the operation failed (null on success)
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Duration of the operation
    /// </summary>
    public TimeSpan Duration => CompletedAt - StartedAt;

    /// <summary>
    /// For rename operations, the original path before the rename
    /// </summary>
    public string? RenamedFrom { get; init; }

    /// <summary>
    /// For rename operations, the new path after the rename
    /// </summary>
    public string? RenamedTo { get; init; }

    /// <summary>
    /// Indicates whether this operation was part of a rename
    /// </summary>
    public bool IsRename => RenamedFrom is not null || RenamedTo is not null;
}
