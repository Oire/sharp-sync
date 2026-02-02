namespace Oire.SharpSync.Core;

/// <summary>
/// Represents an item in the sync storage
/// </summary>
public class SyncItem {
    /// <summary>
    /// Gets or sets the relative path of the item
    /// </summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets whether this is a directory
    /// </summary>
    public bool IsDirectory { get; set; }

    /// <summary>
    /// Gets or sets the size in bytes (0 for directories)
    /// </summary>
    public long Size { get; set; }

    /// <summary>
    /// Gets or sets the last modified time
    /// </summary>
    public DateTime LastModified { get; set; }

    /// <summary>
    /// Gets or sets the content hash (null for directories)
    /// </summary>
    public string? Hash { get; set; }

    /// <summary>
    /// Gets or sets the ETag if available (for WebDAV)
    /// </summary>
    public string? ETag { get; set; }

    /// <summary>
    /// Gets or sets whether this item is a symbolic link
    /// </summary>
    public bool IsSymlink { get; set; }

    /// <summary>
    /// Gets or sets additional metadata
    /// </summary>
    public Dictionary<string, object> Metadata { get; set; } = new();

    /// <summary>
    /// Gets or sets file permissions (if supported by storage)
    /// </summary>
    public string? Permissions { get; set; }

    /// <summary>
    /// Gets or sets the MIME type
    /// </summary>
    public string? MimeType { get; set; }

    /// <summary>
    /// Gets or sets the virtual file state for cloud file systems
    /// </summary>
    /// <remarks>
    /// Used by desktop clients integrating with Windows Cloud Files API or similar
    /// virtual file systems. When <see cref="VirtualFileState.Placeholder"/>, the file
    /// exists only as metadata locally and content must be fetched on demand.
    /// </remarks>
    public VirtualFileState VirtualState { get; set; } = VirtualFileState.None;

    /// <summary>
    /// Gets or sets the cloud-specific item identifier for virtual file tracking
    /// </summary>
    /// <remarks>
    /// Platform-specific identifier used by cloud file systems to track the file.
    /// For Windows Cloud Files API, this could be the CF_PLACEHOLDER_BASIC_INFO identifier.
    /// </remarks>
    public string? CloudFileId { get; set; }
}
