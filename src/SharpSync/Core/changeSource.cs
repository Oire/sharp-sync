namespace Oire.SharpSync.Core;

/// <summary>
/// Indicates where a change originated from
/// </summary>
public enum ChangeSource {
    /// <summary>
    /// The change was detected locally (e.g., via FileSystemWatcher)
    /// </summary>
    Local,

    /// <summary>
    /// The change was detected on the remote storage
    /// </summary>
    Remote
}
