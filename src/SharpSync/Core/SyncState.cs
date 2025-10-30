using SQLite;

namespace Oire.SharpSync.Core;

/// <summary>
/// Represents the synchronization state of a file or directory
/// </summary>
[Table("SyncStates")]
public class SyncState {
    /// <summary>
    /// Gets or sets the unique identifier
    /// </summary>
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    /// <summary>
    /// Gets or sets the relative path (indexed for fast lookups)
    /// </summary>
    [Indexed(Unique = true)]
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets whether this is a directory
    /// </summary>
    public bool IsDirectory { get; set; }

    /// <summary>
    /// Gets or sets the local file hash
    /// </summary>
    public string? LocalHash { get; set; }

    /// <summary>
    /// Gets or sets the remote file hash
    /// </summary>
    public string? RemoteHash { get; set; }

    /// <summary>
    /// Gets or sets the local last modified time
    /// </summary>
    public DateTime? LocalModified { get; set; }

    /// <summary>
    /// Gets or sets the remote last modified time
    /// </summary>
    public DateTime? RemoteModified { get; set; }

    /// <summary>
    /// Gets or sets the local file size
    /// </summary>
    public long LocalSize { get; set; }

    /// <summary>
    /// Gets or sets the remote file size
    /// </summary>
    public long RemoteSize { get; set; }

    /// <summary>
    /// Gets or sets the sync status
    /// </summary>
    public SyncStatus Status { get; set; }

    /// <summary>
    /// Gets or sets the last sync time
    /// </summary>
    public DateTime? LastSyncTime { get; set; }

    /// <summary>
    /// Gets or sets the ETag for WebDAV
    /// </summary>
    public string? ETag { get; set; }

    /// <summary>
    /// Gets or sets any error message
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Gets or sets the number of sync attempts
    /// </summary>
    public int SyncAttempts { get; set; }
}
