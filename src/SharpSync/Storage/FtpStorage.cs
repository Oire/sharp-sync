using System.Security.Cryptography;
using FluentFTP;
using Oire.SharpSync.Core;

namespace Oire.SharpSync.Storage;

/// <summary>
/// FTP/FTPS storage implementation with support for secure connections
/// Provides file synchronization over File Transfer Protocol
/// </summary>
public class FtpStorage: ISyncStorage, IDisposable {
    private AsyncFtpClient? _client;
    private readonly string _host;
    private readonly int _port;
    private readonly string _username;
    private readonly string _password;
    private readonly FtpEncryptionMode _encryptionMode;
    private readonly FtpConfig _config;

    // Configuration
    private readonly int _chunkSize;
    private readonly int _maxRetries;
    private readonly TimeSpan _retryDelay;
    private readonly TimeSpan _connectionTimeout;

    private readonly SemaphoreSlim _connectionSemaphore;
    private bool _disposed;

    /// <summary>
    /// Gets the storage type (always returns <see cref="Core.StorageType.Ftp"/>)
    /// </summary>
    public StorageType StorageType => StorageType.Ftp;

    /// <summary>
    /// Gets the root path on the FTP server
    /// </summary>
    public string RootPath { get; }

    /// <summary>
    /// Creates FTP storage with password authentication
    /// </summary>
    /// <param name="host">FTP server hostname</param>
    /// <param name="port">FTP server port (default 21)</param>
    /// <param name="username">Username for authentication</param>
    /// <param name="password">Password for authentication</param>
    /// <param name="rootPath">Root path on the FTP server</param>
    /// <param name="useFtps">Use FTPS (explicit SSL/TLS) instead of plain FTP</param>
    /// <param name="useImplicitFtps">Use implicit FTPS (SSL from connection start)</param>
    /// <param name="chunkSizeBytes">Chunk size for large file uploads (default 10MB)</param>
    /// <param name="maxRetries">Maximum retry attempts (default 3)</param>
    /// <param name="connectionTimeoutSeconds">Connection timeout in seconds (default 30)</param>
    public FtpStorage(
        string host,
        int port = 21,
        string username = "anonymous",
        string password = "anonymous@example.com",
        string rootPath = "",
        bool useFtps = false,
        bool useImplicitFtps = false,
        int chunkSizeBytes = 10 * 1024 * 1024, // 10MB
        int maxRetries = 3,
        int connectionTimeoutSeconds = 30) {
        if (string.IsNullOrWhiteSpace(host)) {
            throw new ArgumentException("Host cannot be empty", nameof(host));
        }

        if (port <= 0 || port > 65535) {
            throw new ArgumentException("Port must be between 1 and 65535", nameof(port));
        }

        if (string.IsNullOrWhiteSpace(username)) {
            throw new ArgumentException("Username cannot be empty", nameof(username));
        }

        if (string.IsNullOrWhiteSpace(password)) {
            throw new ArgumentException("Password cannot be empty", nameof(password));
        }

        _host = host;
        _port = port;
        _username = username;
        _password = password;
        RootPath = NormalizePath(rootPath);

        // Determine encryption mode
        if (useImplicitFtps) {
            _encryptionMode = FtpEncryptionMode.Implicit;
        } else if (useFtps) {
            _encryptionMode = FtpEncryptionMode.Explicit;
        } else {
            _encryptionMode = FtpEncryptionMode.None;
        }

        _chunkSize = chunkSizeBytes;
        _maxRetries = maxRetries;
        _retryDelay = TimeSpan.FromSeconds(1);
        _connectionTimeout = TimeSpan.FromSeconds(connectionTimeoutSeconds);

        // Configure FluentFTP client
        _config = new FtpConfig {
            EncryptionMode = _encryptionMode,
            ValidateAnyCertificate = true, // Accept any certificate (can be configured for production)
            ConnectTimeout = (int)_connectionTimeout.TotalMilliseconds,
            DataConnectionType = FtpDataConnectionType.AutoPassive,
            TransferChunkSize = _chunkSize
        };

        _connectionSemaphore = new SemaphoreSlim(1, 1);
    }

    /// <summary>
    /// Event raised when upload/download progress changes
    /// </summary>
    public event EventHandler<StorageProgressEventArgs>? ProgressChanged;

    /// <summary>
    /// Establishes connection to FTP server
    /// </summary>
    private async Task EnsureConnectedAsync(CancellationToken cancellationToken = default) {
        if (_client?.IsConnected == true) {
            return;
        }

        await _connectionSemaphore.WaitAsync(cancellationToken);
        try {
            if (_client?.IsConnected == true) {
                return;
            }

            // Dispose old client if exists
            if (_client != null) {
                await _client.Disconnect(cancellationToken);
                _client.Dispose();
            }

            // Create and connect client
            _client = new AsyncFtpClient(_host, _username, _password, _port, _config);

            await _client.Connect(cancellationToken);
        } finally {
            _connectionSemaphore.Release();
        }
    }

    /// <summary>
    /// Tests the connection to the FTP server
    /// </summary>
    /// <param name="cancellationToken">Cancellation token to cancel the operation</param>
    /// <returns>True if connection is successful, false otherwise</returns>
    public async Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default) {
        try {
            await EnsureConnectedAsync(cancellationToken);
            return _client?.IsConnected == true;
        } catch {
            return false;
        }
    }

    /// <summary>
    /// Lists all items (files and directories) in the specified path
    /// </summary>
    /// <param name="path">The relative path to list items from</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation</param>
    /// <returns>A collection of sync items representing files and directories</returns>
    /// <exception cref="UnauthorizedAccessException">Thrown when authentication fails</exception>
    public async Task<IEnumerable<SyncItem>> ListItemsAsync(string path, CancellationToken cancellationToken = default) {
        await EnsureConnectedAsync(cancellationToken);

        var fullPath = GetFullPath(path);

        return await ExecuteWithRetry(async () => {
            var items = new List<SyncItem>();

            if (!await _client!.DirectoryExists(fullPath, cancellationToken)) {
                return items;
            }

            var ftpItems = await _client.GetListing(fullPath, cancellationToken);

            foreach (var item in ftpItems) {
                cancellationToken.ThrowIfCancellationRequested();

                // Skip parent directory entries
                if (item.Name == ".." || item.Name == ".") {
                    continue;
                }

                var relativePath = GetRelativePath(item.FullName);

                items.Add(new SyncItem {
                    Path = relativePath,
                    IsDirectory = item.Type == FtpObjectType.Directory,
                    Size = item.Size,
                    LastModified = item.Modified.ToUniversalTime(),
                    Permissions = ConvertPermissionsToString(item),
                    MimeType = item.Type == FtpObjectType.Directory ? null : GetMimeType(item.Name)
                });
            }

            return (IEnumerable<SyncItem>)items;
        }, cancellationToken);
    }

    /// <summary>
    /// Gets metadata for a specific item (file or directory)
    /// </summary>
    /// <param name="path">The relative path to the item</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation</param>
    /// <returns>The sync item if it exists, null otherwise</returns>
    public async Task<SyncItem?> GetItemAsync(string path, CancellationToken cancellationToken = default) {
        await EnsureConnectedAsync(cancellationToken);

        var fullPath = GetFullPath(path);

        return await ExecuteWithRetry(async () => {
            if (!await _client!.FileExists(fullPath, cancellationToken) &&
                !await _client.DirectoryExists(fullPath, cancellationToken)) {
                return null;
            }

            var item = await _client.GetObjectInfo(fullPath);
            if (item == null) {
                return null;
            }

            return new SyncItem {
                Path = path,
                IsDirectory = item.Type == FtpObjectType.Directory,
                Size = item.Size,
                LastModified = item.Modified.ToUniversalTime(),
                Permissions = ConvertPermissionsToString(item),
                MimeType = item.Type == FtpObjectType.Directory ? null : GetMimeType(item.Name)
            };
        }, cancellationToken);
    }

    /// <summary>
    /// Reads the contents of a file from the FTP server
    /// </summary>
    /// <param name="path">The relative path to the file</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation</param>
    /// <returns>A stream containing the file contents</returns>
    /// <exception cref="FileNotFoundException">Thrown when the file does not exist</exception>
    /// <exception cref="InvalidOperationException">Thrown when attempting to read a directory as a file</exception>
    /// <exception cref="UnauthorizedAccessException">Thrown when authentication fails</exception>
    /// <remarks>
    /// For files larger than the configured chunk size, progress events will be raised via <see cref="ProgressChanged"/>
    /// </remarks>
    public async Task<Stream> ReadFileAsync(string path, CancellationToken cancellationToken = default) {
        await EnsureConnectedAsync(cancellationToken);

        var fullPath = GetFullPath(path);

        return await ExecuteWithRetry(async () => {
            if (!await _client!.FileExists(fullPath, cancellationToken)) {
                throw new FileNotFoundException($"File not found: {path}");
            }

            var memoryStream = new MemoryStream();

            // Get file size for progress reporting
            var fileInfo = await _client.GetObjectInfo(fullPath);
            var needsProgress = fileInfo?.Size > _chunkSize;

            if (needsProgress && fileInfo != null) {
                // Download with progress reporting
                var totalBytes = fileInfo.Size;
                var progress = new Progress<FtpProgress>(p => {
                    RaiseProgressChanged(path, p.TransferredBytes, totalBytes, StorageOperation.Download);
                });

                await _client.DownloadStream(memoryStream, fullPath, progress: progress, token: cancellationToken);
            } else {
                // Download without progress
                await _client.DownloadStream(memoryStream, fullPath, token: cancellationToken);
            }

            memoryStream.Position = 0;
            return (Stream)memoryStream;
        }, cancellationToken);
    }

    /// <summary>
    /// Writes content to a file on the FTP server, creating parent directories as needed
    /// </summary>
    /// <param name="path">The relative path to the file</param>
    /// <param name="content">The stream containing the file content to write</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation</param>
    /// <exception cref="UnauthorizedAccessException">Thrown when authentication fails</exception>
    /// <remarks>
    /// If the file already exists, it will be overwritten. For files larger than the configured chunk size,
    /// progress events will be raised via <see cref="ProgressChanged"/>
    /// </remarks>
    public async Task WriteFileAsync(string path, Stream content, CancellationToken cancellationToken = default) {
        await EnsureConnectedAsync(cancellationToken);

        var fullPath = GetFullPath(path);

        // Ensure parent directories exist
        var directory = GetParentDirectory(fullPath);
        if (!string.IsNullOrEmpty(directory)) {
            await CreateDirectoryAsync(GetRelativePath(directory), cancellationToken);
        }

        await ExecuteWithRetry(async () => {
            var needsProgress = content.CanSeek && content.Length > _chunkSize;

            if (needsProgress) {
                // Upload with progress reporting
                var totalBytes = content.Length;
                var progress = new Progress<FtpProgress>(p => {
                    RaiseProgressChanged(path, p.TransferredBytes, totalBytes, StorageOperation.Upload);
                });

                await _client!.UploadStream(content, fullPath, FtpRemoteExists.Overwrite, true, progress, cancellationToken);
            } else {
                // Upload without progress
                await _client!.UploadStream(content, fullPath, FtpRemoteExists.Overwrite, true, token: cancellationToken);
            }

            return true;
        }, cancellationToken);
    }

    /// <summary>
    /// Creates a directory on the FTP server, including all parent directories if needed
    /// </summary>
    /// <param name="path">The relative path to the directory to create</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation</param>
    /// <exception cref="UnauthorizedAccessException">Thrown when authentication fails</exception>
    /// <remarks>
    /// If the directory already exists, this method completes successfully without error
    /// </remarks>
    public async Task CreateDirectoryAsync(string path, CancellationToken cancellationToken = default) {
        await EnsureConnectedAsync(cancellationToken);

        var fullPath = GetFullPath(path);

        // Don't attempt to create root directory
        if (fullPath == "/" || string.IsNullOrEmpty(fullPath)) {
            return;
        }

        await ExecuteWithRetry(async () => {
            if (await _client!.DirectoryExists(fullPath, cancellationToken)) {
                return true; // Directory already exists
            }

            // Create directory with parent directories
            await _client.CreateDirectory(fullPath, cancellationToken);

            return true;
        }, cancellationToken);
    }

    /// <summary>
    /// Deletes a file or directory from the FTP server
    /// </summary>
    /// <param name="path">The relative path to the file or directory to delete</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation</param>
    /// <exception cref="UnauthorizedAccessException">Thrown when authentication fails</exception>
    /// <remarks>
    /// If the path is a directory, it will be deleted recursively along with all its contents.
    /// If the item does not exist, this method completes successfully without error
    /// </remarks>
    public async Task DeleteAsync(string path, CancellationToken cancellationToken = default) {
        await EnsureConnectedAsync(cancellationToken);

        var fullPath = GetFullPath(path);

        await ExecuteWithRetry(async () => {
            if (await _client!.DirectoryExists(fullPath, cancellationToken)) {
                // Delete directory recursively
                await _client.DeleteDirectory(fullPath, cancellationToken);
            } else if (await _client.FileExists(fullPath, cancellationToken)) {
                // Delete file
                await _client.DeleteFile(fullPath, cancellationToken);
            }

            return true;
        }, cancellationToken);
    }

    /// <summary>
    /// Moves or renames a file or directory on the FTP server
    /// </summary>
    /// <param name="sourcePath">The relative path to the source file or directory</param>
    /// <param name="targetPath">The relative path to the target location</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation</param>
    /// <exception cref="FileNotFoundException">Thrown when the source does not exist</exception>
    /// <exception cref="UnauthorizedAccessException">Thrown when authentication fails</exception>
    /// <remarks>
    /// Parent directories of the target path will be created if they don't exist
    /// </remarks>
    public async Task MoveAsync(string sourcePath, string targetPath, CancellationToken cancellationToken = default) {
        await EnsureConnectedAsync(cancellationToken);

        var sourceFullPath = GetFullPath(sourcePath);
        var targetFullPath = GetFullPath(targetPath);

        // Ensure target parent directory exists
        var targetParentRelative = GetParentDirectory(NormalizePath(targetPath));
        if (!string.IsNullOrEmpty(targetParentRelative)) {
            await CreateDirectoryAsync(targetParentRelative, cancellationToken);
        }

        await ExecuteWithRetry(async () => {
            if (!await _client!.FileExists(sourceFullPath, cancellationToken) &&
                !await _client.DirectoryExists(sourceFullPath, cancellationToken)) {
                throw new FileNotFoundException($"Source not found: {sourcePath}");
            }

            await _client.MoveFile(sourceFullPath, targetFullPath, FtpRemoteExists.Overwrite, cancellationToken);

            return true;
        }, cancellationToken);
    }

    /// <summary>
    /// Checks whether a file or directory exists on the FTP server
    /// </summary>
    /// <param name="path">The relative path to check</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation</param>
    /// <returns>True if the file or directory exists, false otherwise</returns>
    public async Task<bool> ExistsAsync(string path, CancellationToken cancellationToken = default) {
        await EnsureConnectedAsync(cancellationToken);

        var fullPath = GetFullPath(path);

        return await ExecuteWithRetry(async () => {
            return await _client!.FileExists(fullPath, cancellationToken) ||
                   await _client.DirectoryExists(fullPath, cancellationToken);
        }, cancellationToken);
    }

    /// <summary>
    /// Gets storage space information for the FTP server
    /// </summary>
    /// <param name="cancellationToken">Cancellation token to cancel the operation</param>
    /// <returns>Storage information (may return -1 for unknown values as not all FTP servers support this)</returns>
    /// <remarks>
    /// FTP protocol does not have standardized disk space reporting. This method returns
    /// best-effort values which may be -1 if the server doesn't support the SIZE command
    /// </remarks>
    public async Task<StorageInfo> GetStorageInfoAsync(CancellationToken cancellationToken = default) {
        await EnsureConnectedAsync(cancellationToken);

        // FTP doesn't have a standard way to get disk space
        // Return unknown values
        return await Task.FromResult(new StorageInfo {
            TotalSpace = -1,
            UsedSpace = -1
        });
    }

    /// <summary>
    /// Computes the SHA256 hash of a file on the FTP server
    /// </summary>
    /// <param name="path">The relative path to the file</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation</param>
    /// <returns>Base64-encoded SHA256 hash of the file contents</returns>
    /// <exception cref="FileNotFoundException">Thrown when the file does not exist</exception>
    /// <remarks>
    /// Since FTP protocol doesn't provide native hash computation, this method downloads
    /// the file and computes the hash locally. For large files, consider the performance implications
    /// </remarks>
    public async Task<string> ComputeHashAsync(string path, CancellationToken cancellationToken = default) {
        // FTP doesn't have native hash support, so we download and hash
        using var stream = await ReadFileAsync(path, cancellationToken);
        using var sha256 = SHA256.Create();

        var hashBytes = await sha256.ComputeHashAsync(stream, cancellationToken);
        return Convert.ToBase64String(hashBytes);
    }

    #region Helper Methods

    /// <summary>
    /// Normalizes a path for FTP (uses forward slashes)
    /// </summary>
    private static string NormalizePath(string path) {
        if (string.IsNullOrWhiteSpace(path)) {
            return "";
        }

        // Convert backslashes to forward slashes
        path = path.Replace('\\', '/');

        // Remove trailing slashes
        path = path.TrimEnd('/');

        // Ensure path doesn't start with slash (unless it's root)
        if (path == "/") {
            return "/";
        }

        return path.TrimStart('/');
    }

    /// <summary>
    /// Gets the full path on the FTP server
    /// </summary>
    private string GetFullPath(string relativePath) {
        if (string.IsNullOrEmpty(relativePath) || relativePath == "/") {
            return string.IsNullOrEmpty(RootPath) ? "/" : $"/{RootPath}";
        }

        relativePath = NormalizePath(relativePath);

        if (string.IsNullOrEmpty(RootPath)) {
            return $"/{relativePath}";
        }

        return $"/{RootPath}/{relativePath}";
    }

    /// <summary>
    /// Gets the relative path from a full FTP path
    /// </summary>
    private string GetRelativePath(string fullPath) {
        var prefix = string.IsNullOrEmpty(RootPath) || RootPath == "/"
            ? "/"
            : $"/{RootPath}/";

        if (fullPath.StartsWith(prefix)) {
            var relativePath = fullPath.Substring(prefix.Length);
            return string.IsNullOrEmpty(relativePath) ? "/" : relativePath;
        }

        return fullPath;
    }

    /// <summary>
    /// Gets the parent directory of a path
    /// </summary>
    private static string GetParentDirectory(string path) {
        if (string.IsNullOrEmpty(path)) {
            return string.Empty;
        }

        var lastSlash = path.LastIndexOf('/');
        if (lastSlash <= 0) {
            return string.Empty;
        }

        return path.Substring(0, lastSlash);
    }

    /// <summary>
    /// Converts FTP file permissions to a string representation
    /// </summary>
    private static string ConvertPermissionsToString(FtpListItem item) {
        // If Chmod is 0 (unknown/unset), return empty; otherwise return numeric string (e.g., "755")
        return item.Chmod != 0 ? item.Chmod.ToString() : string.Empty;
    }

    /// <summary>
    /// Gets MIME type based on file extension
    /// </summary>
    private static string GetMimeType(string fileName) {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        return extension switch {
            ".txt" => "text/plain",
            ".pdf" => "application/pdf",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".zip" => "application/zip",
            ".json" => "application/json",
            ".xml" => "application/xml",
            ".html" or ".htm" => "text/html",
            ".css" => "text/css",
            ".js" => "application/javascript",
            ".mp3" => "audio/mpeg",
            ".mp4" => "video/mp4",
            ".doc" => "application/msword",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".xls" => "application/vnd.ms-excel",
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            _ => "application/octet-stream"
        };
    }

    /// <summary>
    /// Executes an operation with retry logic
    /// </summary>
    private async Task<T> ExecuteWithRetry<T>(Func<Task<T>> operation, CancellationToken cancellationToken) {
        Exception? lastException = null;

        for (int attempt = 0; attempt <= _maxRetries; attempt++) {
            try {
                cancellationToken.ThrowIfCancellationRequested();
                return await operation();
            } catch (Exception ex) when (attempt < _maxRetries && IsRetriableException(ex)) {
                lastException = ex;

                // Reconnect if connection was lost
                if (ex is IOException || ex is TimeoutException) {
                    try {
                        if (_client != null) {
                            await _client.Disconnect(cancellationToken);
                            _client.Dispose();
                            _client = null;
                        }
                        await EnsureConnectedAsync(cancellationToken);
                    } catch {
                        // Ignore reconnection errors, will retry
                    }
                }

                await Task.Delay(_retryDelay * (attempt + 1), cancellationToken);
            }
        }

        throw lastException ?? new InvalidOperationException("Operation failed");
    }

    /// <summary>
    /// Determines if an exception is retriable
    /// </summary>
    private static bool IsRetriableException(Exception ex) {
        return ex is IOException ||
               ex is TimeoutException ||
               ex is UnauthorizedAccessException == false; // Don't retry auth errors
    }

    /// <summary>
    /// Raises progress changed event
    /// </summary>
    private void RaiseProgressChanged(string path, long completed, long total, StorageOperation operation) {
        ProgressChanged?.Invoke(this, new StorageProgressEventArgs {
            Path = path,
            BytesTransferred = completed,
            TotalBytes = total,
            Operation = operation,
            PercentComplete = total > 0 ? (int)((completed * 100L) / total) : 0
        });
    }

    #endregion

    #region IDisposable

    /// <summary>
    /// Releases all resources used by the FTP storage instance
    /// </summary>
    /// <remarks>
    /// Disconnects from the FTP server and disposes of the underlying FTP client and connection semaphore.
    /// This method can be called multiple times safely
    /// </remarks>
    public void Dispose() {
        if (!_disposed) {
            try {
                _client?.Disconnect();
            } catch {
                // Ignore disconnection errors during disposal
            }

            _client?.Dispose();
            _connectionSemaphore?.Dispose();
            _disposed = true;
        }

        GC.SuppressFinalize(this);
    }

    #endregion
}
