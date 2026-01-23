using SQLite;
using Oire.SharpSync.Core;

namespace Oire.SharpSync.Database;

/// <summary>
/// SQLite table model for persisting completed sync operations.
/// </summary>
[Table("OperationHistory")]
internal sealed class OperationHistory {
    /// <summary>
    /// Unique identifier for this operation record
    /// </summary>
    [PrimaryKey, AutoIncrement]
    public long Id { get; set; }

    /// <summary>
    /// The relative path of the file or directory
    /// </summary>
    [Indexed]
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// The type of operation (stored as integer)
    /// </summary>
    public int ActionType { get; set; }

    /// <summary>
    /// Whether the item is a directory
    /// </summary>
    public bool IsDirectory { get; set; }

    /// <summary>
    /// File size in bytes
    /// </summary>
    public long Size { get; set; }

    /// <summary>
    /// Source of the change (Local = 0, Remote = 1)
    /// </summary>
    public int Source { get; set; }

    /// <summary>
    /// When the operation started (stored as ticks)
    /// </summary>
    public long StartedAtTicks { get; set; }

    /// <summary>
    /// When the operation completed (stored as ticks)
    /// </summary>
    [Indexed]
    public long CompletedAtTicks { get; set; }

    /// <summary>
    /// Whether the operation succeeded
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Error message if failed
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Original path for rename operations
    /// </summary>
    public string? RenamedFrom { get; set; }

    /// <summary>
    /// New path for rename operations
    /// </summary>
    public string? RenamedTo { get; set; }

    /// <summary>
    /// Converts this database record to a CompletedOperation domain model
    /// </summary>
    public CompletedOperation ToCompletedOperation() => new() {
        Id = Id,
        Path = Path,
        ActionType = (SyncActionType)ActionType,
        IsDirectory = IsDirectory,
        Size = Size,
        Source = (ChangeSource)Source,
        StartedAt = new DateTime(StartedAtTicks, DateTimeKind.Utc),
        CompletedAt = new DateTime(CompletedAtTicks, DateTimeKind.Utc),
        Success = Success,
        ErrorMessage = ErrorMessage,
        RenamedFrom = RenamedFrom,
        RenamedTo = RenamedTo
    };

    /// <summary>
    /// Creates a database record from operation details
    /// </summary>
    public static OperationHistory FromOperation(
        string path,
        SyncActionType actionType,
        bool isDirectory,
        long size,
        ChangeSource source,
        DateTime startedAt,
        DateTime completedAt,
        bool success,
        string? errorMessage = null,
        string? renamedFrom = null,
        string? renamedTo = null) => new() {
            Path = path,
            ActionType = (int)actionType,
            IsDirectory = isDirectory,
            Size = size,
            Source = (int)source,
            StartedAtTicks = startedAt.Ticks,
            CompletedAtTicks = completedAt.Ticks,
            Success = success,
            ErrorMessage = errorMessage,
            RenamedFrom = renamedFrom,
            RenamedTo = renamedTo
        };
}
