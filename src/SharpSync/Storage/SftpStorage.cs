using System.Security.Cryptography;
using Oire.SharpSync.Core;
using Renci.SshNet;
using Renci.SshNet.Common;
using Renci.SshNet.Sftp;

namespace Oire.SharpSync.Storage;

/// <summary>
/// SFTP storage implementation with support for password and key-based authentication
/// Provides secure file synchronization over SSH File Transfer Protocol
/// </summary>
public class SftpStorage: ISyncStorage, IDisposable {
    private SftpClient? _client;
    private readonly string _host;
    private readonly int _port;
    private readonly string _username;
    private readonly string? _password;
    private readonly string? _privateKeyPath;
    private readonly string? _privateKeyPassphrase;

    // Configuration
    private readonly int _chunkSize;
    private readonly int _maxRetries;
    private readonly TimeSpan _retryDelay;
    private readonly TimeSpan _connectionTimeout;

    private readonly SemaphoreSlim _connectionSemaphore;
    private bool _disposed;

    /// <summary>
    /// Gets the storage type (always returns <see cref="Core.StorageType.Sftp"/>)
    /// </summary>
    public StorageType StorageType => StorageType.Sftp;

    /// <summary>
    /// Gets the root path on the SFTP server
    /// </summary>
    public string RootPath { get; }

    /// <summary>
    /// Creates SFTP storage with password authentication
    /// </summary>
    /// <param name="host">SFTP server hostname</param>
    /// <param name="port">SFTP server port (default 22)</param>
    /// <param name="username">Username for authentication</param>
    /// <param name="password">Password for authentication</param>
    /// <param name="rootPath">Root path on the SFTP server</param>
    /// <param name="chunkSizeBytes">Chunk size for large file uploads (default 10MB)</param>
    /// <param name="maxRetries">Maximum retry attempts (default 3)</param>
    /// <param name="connectionTimeoutSeconds">Connection timeout in seconds (default 30)</param>
    public SftpStorage(
        string host,
        int port,
        string username,
        string password,
        string rootPath = "",
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

        _chunkSize = chunkSizeBytes;
        _maxRetries = maxRetries;
        _retryDelay = TimeSpan.FromSeconds(1);
        _connectionTimeout = TimeSpan.FromSeconds(connectionTimeoutSeconds);

        _connectionSemaphore = new SemaphoreSlim(1, 1);
    }

    /// <summary>
    /// Creates SFTP storage with private key authentication
    /// </summary>
    /// <param name="host">SFTP server hostname</param>
    /// <param name="port">SFTP server port (default 22)</param>
    /// <param name="username">Username for authentication</param>
    /// <param name="privateKeyPath">Path to private key file</param>
    /// <param name="privateKeyPassphrase">Passphrase for private key (if encrypted)</param>
    /// <param name="rootPath">Root path on the SFTP server</param>
    /// <param name="chunkSizeBytes">Chunk size for large file uploads (default 10MB)</param>
    /// <param name="maxRetries">Maximum retry attempts (default 3)</param>
    /// <param name="connectionTimeoutSeconds">Connection timeout in seconds (default 30)</param>
    public SftpStorage(
        string host,
        int port,
        string username,
        string privateKeyPath,
        string? privateKeyPassphrase,
        string rootPath = "",
        int chunkSizeBytes = 10 * 1024 * 1024,
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

        if (string.IsNullOrWhiteSpace(privateKeyPath)) {
            throw new ArgumentException("Private key path cannot be empty", nameof(privateKeyPath));
        }

        if (!File.Exists(privateKeyPath)) {
            throw new FileNotFoundException($"Private key file not found: {privateKeyPath}");
        }

        _host = host;
        _port = port;
        _username = username;
        _privateKeyPath = privateKeyPath;
        _privateKeyPassphrase = privateKeyPassphrase;
        RootPath = NormalizePath(rootPath);

        _chunkSize = chunkSizeBytes;
        _maxRetries = maxRetries;
        _retryDelay = TimeSpan.FromSeconds(1);
        _connectionTimeout = TimeSpan.FromSeconds(connectionTimeoutSeconds);

        _connectionSemaphore = new SemaphoreSlim(1, 1);
    }

    /// <summary>
    /// Event raised when upload/download progress changes
    /// </summary>
    public event EventHandler<StorageProgressEventArgs>? ProgressChanged;

    /// <summary>
    /// Establishes connection to SFTP server
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
            _client?.Dispose();

            // Create connection info
            ConnectionInfo connectionInfo;

            if (!string.IsNullOrEmpty(_privateKeyPath)) {
                // Key-based authentication
                PrivateKeyFile keyFile = string.IsNullOrEmpty(_privateKeyPassphrase)
                    ? new PrivateKeyFile(_privateKeyPath)
                    : new PrivateKeyFile(_privateKeyPath, _privateKeyPassphrase);

                connectionInfo = new ConnectionInfo(
                    _host,
                    _port,
                    _username,
                    new PrivateKeyAuthenticationMethod(_username, keyFile));
            } else {
                // Password authentication
                connectionInfo = new ConnectionInfo(
                    _host,
                    _port,
                    _username,
                    new PasswordAuthenticationMethod(_username, _password));
            }

            connectionInfo.Timeout = _connectionTimeout;

            // Create and connect client
            _client = new SftpClient(connectionInfo);

            await Task.Run(() => _client.Connect(), cancellationToken);

            // Verify root path exists or create it (handle chrooted scenarios)
            if (!string.IsNullOrEmpty(RootPath)) {
                try {
                    // Try common candidate paths (absolute and relative) to account for chroot
                    string? existingRoot = null;
                    var normalizedRoot = RootPath.TrimStart('/');
                    var absoluteRoot = "/" + normalizedRoot;

                    // Check if any common form already exists
                    if (_client.Exists(RootPath)) {
                        existingRoot = RootPath;
                    } else if (_client.Exists(normalizedRoot)) {
                        existingRoot = normalizedRoot;
                    } else if (_client.Exists(absoluteRoot)) {
                        existingRoot = absoluteRoot;
                    }

                    // If not found, try to create it using relative segments first
                    if (existingRoot == null) {
                        var parts = normalizedRoot.Split('/').Where(p => !string.IsNullOrEmpty(p)).ToList();
                        var currentPath = "";

                        foreach (var part in parts) {
                            // Build path incrementally (relative first)
                            currentPath = string.IsNullOrEmpty(currentPath) ? part : $"{currentPath}/{part}";

                            if (!_client.Exists(currentPath)) {
                                try {
                                    _client.CreateDirectory(currentPath);
                                } catch (Renci.SshNet.Common.SftpPermissionDeniedException) {
                                    // Chrooted server may deny relative creation, try absolute
                                    try {
                                        var absoluteCandidate = "/" + currentPath;
                                        if (!_client.Exists(absoluteCandidate)) {
                                            _client.CreateDirectory(absoluteCandidate);
                                        }
                                    } catch (Renci.SshNet.Common.SftpPermissionDeniedException) {
                                        // Server denies directory creation (likely chroot restriction)
                                        // Continue anyway - operations inside user's home may still work
                                        break;
                                    }
                                }
                            }
                        }
                    }
                } catch (Renci.SshNet.Common.SftpPermissionDeniedException) {
                    // Swallow permission errors during root verification
                    // Chrooted SFTP servers may deny creating absolute parent directories
                    // Allow connection to continue for operations inside the accessible area
                }
            }
        } finally {
            _connectionSemaphore.Release();
        }
    }

    /// <summary>
    /// Tests the connection to the SFTP server
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

            if (!_client!.Exists(fullPath)) {
                return items;
            }

            var sftpFiles = await Task.Run(() => _client.ListDirectory(fullPath), cancellationToken);

            foreach (var file in sftpFiles) {
                // Skip current and parent directory entries
                if (file.Name == "." || file.Name == "..") {
                    continue;
                }

                cancellationToken.ThrowIfCancellationRequested();

                items.Add(new SyncItem {
                    Path = GetRelativePath(file.FullName),
                    IsDirectory = file.IsDirectory,
                    Size = file.Length,
                    LastModified = file.LastWriteTimeUtc,
                    Permissions = ConvertPermissionsToString(file),
                    MimeType = file.IsDirectory ? null : GetMimeType(file.Name)
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
            if (!_client!.Exists(fullPath)) {
                return null;
            }

            var file = await Task.Run(() => _client.Get(fullPath), cancellationToken);

            return new SyncItem {
                Path = path,
                IsDirectory = file.IsDirectory,
                Size = file.Length,
                LastModified = file.LastWriteTimeUtc,
                Permissions = ConvertPermissionsToString(file),
                MimeType = file.IsDirectory ? null : GetMimeType(file.Name)
            };
        }, cancellationToken);
    }

    /// <summary>
    /// Reads the contents of a file from the SFTP server
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
            if (!_client!.Exists(fullPath)) {
                throw new FileNotFoundException($"File not found: {path}");
            }

            var file = _client.Get(fullPath);
            if (file.IsDirectory) {
                throw new InvalidOperationException($"Cannot read directory as file: {path}");
            }

            var memoryStream = new MemoryStream();
            var needsProgress = file.Length > _chunkSize;

            if (needsProgress) {
                // Download with progress reporting
                ulong totalBytes = (ulong)file.Length;
                ulong downloadedBytes = 0;

                await Task.Run(() => {
                    _client.DownloadFile(fullPath, memoryStream, (uploaded) => {
                        downloadedBytes = uploaded;
                        RaiseProgressChanged(path, (long)downloadedBytes, (long)totalBytes, StorageOperation.Download);
                    });
                }, cancellationToken);
            } else {
                // Download without progress
                await Task.Run(() => _client.DownloadFile(fullPath, memoryStream), cancellationToken);
            }

            memoryStream.Position = 0;
            return (Stream)memoryStream;
        }, cancellationToken);
    }

    /// <summary>
    /// Writes content to a file on the SFTP server, creating parent directories as needed
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
                ulong totalBytes = (ulong)content.Length;
                ulong uploadedBytes = 0;

                await Task.Run(() => {
                    _client!.UploadFile(content, fullPath, true, (uploaded) => {
                        uploadedBytes = uploaded;
                        RaiseProgressChanged(path, (long)uploadedBytes, (long)totalBytes, StorageOperation.Upload);
                    });
                }, cancellationToken);
            } else {
                // Upload without progress
                await Task.Run(() => _client!.UploadFile(content, fullPath, true), cancellationToken);
            }

            return true;
        }, cancellationToken);
    }

    /// <summary>
    /// Creates a directory on the SFTP server, including all parent directories if needed
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

        await ExecuteWithRetry(async () => {
            if (_client!.Exists(fullPath)) {
                return true; // Directory already exists
            }

            // Create parent directories recursively if needed
            var parts = fullPath.Split('/').Where(p => !string.IsNullOrEmpty(p)).ToList();
            var currentPath = fullPath.StartsWith('/') ? "/" : "";

            foreach (var part in parts) {
                currentPath = string.IsNullOrEmpty(currentPath) || currentPath == "/"
                    ? $"/{part}"
                    : $"{currentPath}/{part}";

                if (!_client.Exists(currentPath)) {
                    await Task.Run(() => _client.CreateDirectory(currentPath), cancellationToken);
                }
            }

            return true;
        }, cancellationToken);
    }

    /// <summary>
    /// Deletes a file or directory from the SFTP server
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
            if (!_client!.Exists(fullPath)) {
                return true; // Already deleted
            }

            var file = _client.Get(fullPath);

            if (file.IsDirectory) {
                await Task.Run(() => DeleteDirectoryRecursive(fullPath, cancellationToken), cancellationToken);
            } else {
                await Task.Run(() => _client.DeleteFile(fullPath), cancellationToken);
            }

            return true;
        }, cancellationToken);
    }

    /// <summary>
    /// Recursively deletes a directory and all its contents
    /// </summary>
    private void DeleteDirectoryRecursive(string path, CancellationToken cancellationToken) {
        foreach (var file in _client!.ListDirectory(path)) {
            if (file.Name == "." || file.Name == "..") {
                continue;
            }

            cancellationToken.ThrowIfCancellationRequested();

            if (file.IsDirectory) {
                DeleteDirectoryRecursive(file.FullName, cancellationToken);
            } else {
                _client.DeleteFile(file.FullName);
            }
        }

        _client.DeleteDirectory(path);
    }

    /// <summary>
    /// Moves or renames a file or directory on the SFTP server
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
        var targetDirectory = GetParentDirectory(targetFullPath);
        if (!string.IsNullOrEmpty(targetDirectory)) {
            await CreateDirectoryAsync(GetRelativePath(targetDirectory), cancellationToken);
        }

        await ExecuteWithRetry(async () => {
            if (!_client!.Exists(sourceFullPath)) {
                throw new FileNotFoundException($"Source not found: {sourcePath}");
            }

            // SFTP doesn't have a native rename across directories, so we use RenameFile
            // which works for both files and directories
            await Task.Run(() => _client.RenameFile(sourceFullPath, targetFullPath), cancellationToken);

            return true;
        }, cancellationToken);
    }

    /// <summary>
    /// Checks whether a file or directory exists on the SFTP server
    /// </summary>
    /// <param name="path">The relative path to check</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation</param>
    /// <returns>True if the file or directory exists, false otherwise</returns>
    public async Task<bool> ExistsAsync(string path, CancellationToken cancellationToken = default) {
        await EnsureConnectedAsync(cancellationToken);

        var fullPath = GetFullPath(path);

        return await ExecuteWithRetry(async () => {
            return await Task.Run(() => _client!.Exists(fullPath), cancellationToken);
        }, cancellationToken);
    }

    /// <summary>
    /// Gets storage space information for the SFTP server
    /// </summary>
    /// <param name="cancellationToken">Cancellation token to cancel the operation</param>
    /// <returns>Storage information (may return -1 for unknown values as SFTP protocol has limited support)</returns>
    /// <remarks>
    /// SFTP protocol does not have standardized disk space reporting. This method returns
    /// best-effort values which may be -1 if the server doesn't support disk space queries
    /// </remarks>
    public async Task<StorageInfo> GetStorageInfoAsync(CancellationToken cancellationToken = default) {
        await EnsureConnectedAsync(cancellationToken);

        return await ExecuteWithRetry(async () => {
            // Try to get disk space using statvfs
            try {
                var statVfs = await Task.Run(() => _client!.GetStatus(RootPath.Length != 0 ? RootPath : "/"), cancellationToken);

                // SFTP doesn't have a standard way to get disk space
                // This is a best-effort approach using SSH commands if available
                // For now, return unknown values
                return new StorageInfo {
                    TotalSpace = -1,
                    UsedSpace = -1
                };
            } catch {
                // If we can't get storage info, return unknown values
                return new StorageInfo {
                    TotalSpace = -1,
                    UsedSpace = -1
                };
            }
        }, cancellationToken);
    }

    /// <summary>
    /// Computes the SHA256 hash of a file on the SFTP server
    /// </summary>
    /// <param name="path">The relative path to the file</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation</param>
    /// <returns>Base64-encoded SHA256 hash of the file contents</returns>
    /// <exception cref="FileNotFoundException">Thrown when the file does not exist</exception>
    /// <remarks>
    /// Since SFTP protocol doesn't provide native hash computation, this method downloads
    /// the file and computes the hash locally. For large files, consider the performance implications
    /// </remarks>
    public async Task<string> ComputeHashAsync(string path, CancellationToken cancellationToken = default) {
        // SFTP doesn't have native hash support, so we download and hash
        using var stream = await ReadFileAsync(path, cancellationToken);
        using var sha256 = SHA256.Create();

        var hashBytes = await sha256.ComputeHashAsync(stream, cancellationToken);
        return Convert.ToBase64String(hashBytes);
    }

    #region Helper Methods

    /// <summary>
    /// Normalizes a path for SFTP (uses forward slashes)
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
    /// Gets the full path on the SFTP server
    /// </summary>
    private string GetFullPath(string relativePath) {
        if (string.IsNullOrEmpty(relativePath) || relativePath == "/") {
            return string.IsNullOrEmpty(RootPath) ? "/" : $"/{RootPath}";
        }

        relativePath = NormalizePath(relativePath);

        if (string.IsNullOrEmpty(RootPath) || RootPath == "/") {
            return $"/{relativePath}";
        }

        return $"/{RootPath}/{relativePath}";
    }

    /// <summary>
    /// Gets the relative path from a full SFTP path
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
        var lastSlash = path.LastIndexOf('/');
        if (lastSlash <= 0) {
            return "/";
        }

        return path.Substring(0, lastSlash);
    }

    /// <summary>
    /// Converts SFTP file permissions to a string representation
    /// </summary>
    private static string ConvertPermissionsToString(ISftpFile file) {
        if (file.Attributes == null) {
            return string.Empty;
        }

        var result = new char[10];

        // File type
        if (file.IsDirectory) {
            result[0] = 'd';
        } else if (file.Attributes.IsSymbolicLink) {
            result[0] = 'l';
        } else {
            result[0] = '-';
        }

        // Owner permissions
        result[1] = file.Attributes.OwnerCanRead ? 'r' : '-';
        result[2] = file.Attributes.OwnerCanWrite ? 'w' : '-';
        result[3] = file.Attributes.OwnerCanExecute ? 'x' : '-';

        // Group permissions
        result[4] = file.Attributes.GroupCanRead ? 'r' : '-';
        result[5] = file.Attributes.GroupCanWrite ? 'w' : '-';
        result[6] = file.Attributes.GroupCanExecute ? 'x' : '-';

        // Others permissions
        result[7] = file.Attributes.OthersCanRead ? 'r' : '-';
        result[8] = file.Attributes.OthersCanWrite ? 'w' : '-';
        result[9] = file.Attributes.OthersCanExecute ? 'x' : '-';

        return new string(result);
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
                if (ex is SshConnectionException || ex is SshOperationTimeoutException) {
                    try {
                        _client?.Disconnect();
                        _client?.Dispose();
                        _client = null;
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
        return ex is SshConnectionException ||
               ex is SshOperationTimeoutException ||
               ex is SftpPermissionDeniedException == false; // Don't retry permission errors
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
    /// Releases all resources used by the SFTP storage instance
    /// </summary>
    /// <remarks>
    /// Disconnects from the SFTP server and disposes of the underlying SSH client and connection semaphore.
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
