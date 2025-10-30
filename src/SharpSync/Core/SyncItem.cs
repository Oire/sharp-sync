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
}
