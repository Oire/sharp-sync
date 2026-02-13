using Oire.SharpSync.Storage;

namespace Oire.SharpSync.Core;

/// <summary>
/// Represents a storage backend for synchronization (local filesystem, WebDAV, etc.)
/// </summary>
public interface ISyncStorage {
    /// <summary>
    /// Event raised to report transfer progress for large files.
    /// </summary>
    /// <remarks>
    /// Storage implementations should raise this event during file uploads and downloads
    /// to report byte-level progress. This enables UIs to display per-file progress bars.
    /// </remarks>
    event EventHandler<StorageProgressEventArgs>? ProgressChanged;

    /// <summary>
    /// Gets the storage type
    /// </summary>
    StorageType StorageType { get; }

    /// <summary>
    /// Gets the root URL or path for this storage
    /// </summary>
    string RootPath { get; }

    /// <summary>
    /// Lists all items in the specified path
    /// </summary>
    Task<IEnumerable<SyncItem>> ListItemsAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets metadata for a specific item
    /// </summary>
    Task<SyncItem?> GetItemAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads file content
    /// </summary>
    Task<Stream> ReadFileAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes file content
    /// </summary>
    Task WriteFileAsync(string path, Stream content, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a directory
    /// </summary>
    Task CreateDirectoryAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a file or directory
    /// </summary>
    Task DeleteAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>
    /// Moves or renames an item
    /// </summary>
    Task MoveAsync(string sourcePath, string targetPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if an item exists
    /// </summary>
    Task<bool> ExistsAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets available space information
    /// </summary>
    Task<StorageInfo> GetStorageInfoAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Computes hash of a file for comparison
    /// </summary>
    Task<string> ComputeHashAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tests connection to the storage
    /// </summary>
    Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets the last modified time for a file or directory.
    /// </summary>
    /// <remarks>
    /// Not all storage backends support setting modification times. The default implementation
    /// is a no-op. Implementations that support this (e.g., local filesystem, SFTP, FTP)
    /// should override this method.
    /// </remarks>
    /// <param name="path">The relative path to the item</param>
    /// <param name="lastModified">The last modified time to set (UTC)</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation</param>
    Task SetLastModifiedAsync(string path, DateTime lastModified, CancellationToken cancellationToken = default) => Task.CompletedTask;

    /// <summary>
    /// Sets file permissions for a file or directory.
    /// </summary>
    /// <remarks>
    /// Not all storage backends or platforms support setting file permissions.
    /// The default implementation is a no-op. Implementations that support this
    /// (e.g., local filesystem on Unix, SFTP) should override this method.
    /// </remarks>
    /// <param name="path">The relative path to the item</param>
    /// <param name="permissions">The permissions string (e.g., "rwxr-xr-x" or "755")</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation</param>
    Task SetPermissionsAsync(string path, string permissions, CancellationToken cancellationToken = default) => Task.CompletedTask;

    /// <summary>
    /// Gets remote changes detected since the specified time.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Not all storage backends support efficient remote change detection. The default
    /// implementation returns an empty list. Implementations that support this
    /// (e.g., WebDAV with Nextcloud activity API, S3 with date-filtered listing)
    /// should override this method.
    /// </para>
    /// <para>
    /// This method enables the sync engine to discover remote changes without
    /// performing a full storage scan, which is significantly more efficient for
    /// large datasets.
    /// </para>
    /// </remarks>
    /// <param name="since">Only return changes detected after this time (UTC)</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation</param>
    /// <returns>A collection of remote changes detected since the specified time</returns>
    Task<IReadOnlyList<ChangeInfo>> GetRemoteChangesAsync(DateTime since, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<ChangeInfo>>(Array.Empty<ChangeInfo>());
}
