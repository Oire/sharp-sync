namespace Oire.SharpSync.Core;

/// <summary>
/// Represents a planned synchronization action that will be performed on a file or directory.
/// </summary>
/// <remarks>
/// This class is designed for desktop clients to display detailed sync previews to users before
/// synchronization begins. It provides all the information needed to show what will happen to each file.
/// </remarks>
public sealed class SyncPlanAction {
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
    /// Gets a human-readable description of this action
    /// </summary>
    /// <remarks>
    /// Examples: "Download document.pdf (1.2 MB)", "Upload Photos/ folder", "Delete old-file.txt from remote"
    /// </remarks>
    public string Description {
        get {
            var sizeStr = IsDirectory ? "folder" : FormatSize(Size);
            var pathDisplay = IsDirectory ? $"{Path}/" : Path;

            return ActionType switch {
                SyncActionType.Download => $"Download {pathDisplay}" + (IsDirectory ? "" : $" ({sizeStr})"),
                SyncActionType.Upload => $"Upload {pathDisplay}" + (IsDirectory ? "" : $" ({sizeStr})"),
                SyncActionType.DeleteLocal => $"Delete {pathDisplay} from local storage",
                SyncActionType.DeleteRemote => $"Delete {pathDisplay} from remote storage",
                SyncActionType.Conflict => $"Resolve conflict for {pathDisplay}" + (ConflictType.HasValue ? $" ({ConflictType.Value})" : ""),
                _ => $"Process {pathDisplay}"
            };
        }
    }

    /// <summary>
    /// Gets the priority of this action (higher number = higher priority)
    /// </summary>
    /// <remarks>
    /// Used internally for optimization. Desktop clients can use this to sort actions
    /// in a way that matches the actual synchronization order.
    /// </remarks>
    public int Priority { get; init; }

    private static string FormatSize(long bytes) {
        if (bytes < 1024) {
            return $"{bytes} B";
        }

        if (bytes < 1024 * 1024) {
            return $"{bytes / 1024.0:F1} KB";
        }

        if (bytes < 1024 * 1024 * 1024) {
            return $"{bytes / (1024.0 * 1024.0):F1} MB";
        }

        return $"{bytes / (1024.0 * 1024.0 * 1024.0):F1} GB";
    }
}
