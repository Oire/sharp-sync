namespace Oire.SharpSync.Storage;

/// <summary>
/// Server capabilities detected for optimization
/// </summary>
public class ServerCapabilities {
    /// <summary>
    /// Whether the server is Nextcloud
    /// </summary>
    public bool IsNextcloud { get; set; }

    /// <summary>
    /// Whether the server is OCIS (ownCloud Infinite Scale)
    /// </summary>
    public bool IsOcis { get; set; }

    /// <summary>
    /// Server version string
    /// </summary>
    public string ServerVersion { get; set; } = "";

    /// <summary>
    /// Whether the server supports chunked uploads
    /// </summary>
    public bool SupportsChunking { get; set; }

    /// <summary>
    /// Chunking API version (for Nextcloud)
    /// </summary>
    public int ChunkingVersion { get; set; }

    /// <summary>
    /// Whether the server supports OCIS chunking (TUS protocol)
    /// </summary>
    public bool SupportsOcisChunking { get; set; }

    /// <summary>
    /// Whether the server is a generic WebDAV server
    /// </summary>
    public bool IsGenericWebDav => !IsNextcloud && !IsOcis;
}
