namespace Oire.SharpSync.Core;

/// <summary>
/// Represents a detected change to a file or directory, used for both local and remote
/// change notifications and storage-level change detection.
/// </summary>
/// <remarks>
/// <para>
/// This record is used in three contexts:
/// <list type="bullet">
/// <item><description>Batch local change notifications via <see cref="ISyncEngine.NotifyLocalChangeBatchAsync"/></description></item>
/// <item><description>Batch remote change notifications via <see cref="ISyncEngine.NotifyRemoteChangeBatchAsync"/></description></item>
/// <item><description>Storage-level remote change detection via <see cref="ISyncStorage.GetRemoteChangesAsync"/></description></item>
/// </list>
/// </para>
/// </remarks>
/// <param name="Path">The relative path of the changed item</param>
/// <param name="ChangeType">The type of change that occurred</param>
/// <param name="Size">The size of the file in bytes (0 for directories or deletions)</param>
/// <param name="IsDirectory">Whether the changed item is a directory</param>
/// <param name="RenamedFrom">For rename operations, the original path before the rename</param>
/// <param name="RenamedTo">For rename operations, the new path after the rename</param>
public record ChangeInfo(
    string Path,
    ChangeType ChangeType,
    long Size = 0,
    bool IsDirectory = false,
    string? RenamedFrom = null,
    string? RenamedTo = null) {

    /// <summary>
    /// When the change was detected (UTC). Defaults to <see cref="DateTime.UtcNow"/> if not specified at construction.
    /// </summary>
    public DateTime DetectedAt { get; init; } = DateTime.UtcNow;
}
