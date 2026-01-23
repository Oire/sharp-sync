namespace Oire.SharpSync.Core;

/// <summary>
/// Represents the type of change detected by a file system watcher.
/// Maps to <see cref="System.IO.WatcherChangeTypes"/> for easy integration.
/// </summary>
public enum ChangeType {
    /// <summary>
    /// A new file or directory was created.
    /// </summary>
    Created = 1,

    /// <summary>
    /// A file or directory was deleted.
    /// </summary>
    Deleted = 2,

    /// <summary>
    /// A file or directory was modified.
    /// </summary>
    Changed = 4,

    /// <summary>
    /// A file or directory was renamed.
    /// </summary>
    Renamed = 8
}
