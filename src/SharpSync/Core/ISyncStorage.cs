namespace Oire.SharpSync.Core;

/// <summary>
/// Represents a storage backend for synchronization (local filesystem, WebDAV, etc.)
/// </summary>
public interface ISyncStorage
{
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
}