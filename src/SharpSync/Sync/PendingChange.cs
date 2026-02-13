using Oire.SharpSync.Core;

namespace Oire.SharpSync.Sync;

/// <summary>
/// Tracks a pending change from FileSystemWatcher or remote change notifications.
/// </summary>
/// <param name="Path">The normalized relative path of the changed item</param>
/// <param name="ChangeType">The type of change that occurred</param>
internal sealed record PendingChange(
    string Path,
    ChangeType ChangeType) {

    /// <summary>
    /// When the change was detected (UTC).
    /// </summary>
    public DateTime DetectedAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// For rename operations, the original path (set on the new path entry).
    /// </summary>
    public string? RenamedFrom { get; init; }

    /// <summary>
    /// For rename operations, the new path (set on the old path entry).
    /// </summary>
    public string? RenamedTo { get; init; }
}
