namespace Oire.SharpSync.Core;

/// <summary>
/// Supported storage backend types
/// </summary>
public enum StorageType
{
    /// <summary>
    /// Local filesystem storage
    /// </summary>
    Local,
    
    /// <summary>
    /// WebDAV storage (Nextcloud, ownCloud, etc.)
    /// </summary>
    WebDav,
    
    /// <summary>
    /// SFTP/SSH storage
    /// </summary>
    Sftp,
    
    /// <summary>
    /// FTP/FTPS storage
    /// </summary>
    Ftp,
    
    /// <summary>
    /// Amazon S3 or S3-compatible storage
    /// </summary>
    S3
}