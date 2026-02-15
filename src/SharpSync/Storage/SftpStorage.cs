using System.Linq;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Oire.SharpSync.Core;
using Oire.SharpSync.Logging;
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
    private readonly ILogger _logger;
    private bool _disposed;

    // Path handling for chrooted servers
    private string? _effectiveRoot; // Root as it should be used internally (no leading slash)
    private bool _useRelativePaths; // True for chrooted servers that expect relative paths

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
    /// <param name="logger">Optional logger for diagnostic output</param>
    public SftpStorage(
        string host,
        int port,
        string username,
        string password,
        string rootPath = "",
        int chunkSizeBytes = 10 * 1024 * 1024, // 10MB
        int maxRetries = 3,
        int connectionTimeoutSeconds = 30,
        ILogger? logger = null) {
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

        ArgumentOutOfRangeException.ThrowIfLessThan(connectionTimeoutSeconds, 1);

        _host = host;
        _port = port;
        _username = username;
        _password = password;
        RootPath = NormalizePath(rootPath);

        _chunkSize = chunkSizeBytes;
        _maxRetries = maxRetries;
        _retryDelay = TimeSpan.FromSeconds(1);
        _connectionTimeout = TimeSpan.FromSeconds(connectionTimeoutSeconds);

        _logger = logger ?? NullLogger.Instance;
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
    /// <param name="logger">Optional logger for diagnostic output</param>
    public SftpStorage(
        string host,
        int port,
        string username,
        string privateKeyPath,
        string? privateKeyPassphrase,
        string rootPath = "",
        int chunkSizeBytes = 10 * 1024 * 1024,
        int maxRetries = 3,
        int connectionTimeoutSeconds = 30,
        ILogger? logger = null) {
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

        ArgumentOutOfRangeException.ThrowIfLessThan(connectionTimeoutSeconds, 1);

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

        _logger = logger ?? NullLogger.Instance;
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

        await _connectionSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
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

            await Task.Run(() => _client.Connect(), cancellationToken).ConfigureAwait(false);

            // Detect server path handling based on root path configuration
            // When no root is specified or root doesn't start with "/", assume chrooted environment
            // and use relative paths. This is the safe default.
            var normalizedRoot = string.IsNullOrEmpty(RootPath) ? "" : RootPath.TrimStart('/');
            bool isChrooted = string.IsNullOrEmpty(RootPath) || !RootPath.StartsWith('/');

            if (string.IsNullOrEmpty(normalizedRoot)) {
                // No root path specified
                _effectiveRoot = null;
                _useRelativePaths = isChrooted;
            } else {
                try {
                    // Root path specified - check if it exists or try to create it
                    string? existingRoot = null;
                    var absoluteRoot = "/" + normalizedRoot;

                    // Try different path forms based on server type
                    if (isChrooted) {
                        _logger.SftpChrootDetected();
                        // Chrooted server - use relative paths
                        if (SafeExists(normalizedRoot)) {
                            existingRoot = normalizedRoot;
                        } else {
                            // Path doesn't exist, try to create it
                            var parts = normalizedRoot.Split('/').Where(p => !string.IsNullOrEmpty(p)).ToList();
                            var currentPath = "";

                            foreach (var part in parts) {
                                currentPath = string.IsNullOrEmpty(currentPath) ? part : $"{currentPath}/{part}";

                                if (!SafeExists(currentPath)) {
                                    try {
                                        _client.CreateDirectory(currentPath);
                                    } catch (Exception ex) when (ex is Renci.SshNet.Common.SftpPermissionDeniedException ||
                                                                 ex is Renci.SshNet.Common.SftpPathNotFoundException) {
                                        _logger.SftpPermissionDenied(ex, "root path creation", currentPath);
                                        break;
                                    }
                                }
                            }
                            existingRoot = normalizedRoot;
                        }
                        _useRelativePaths = true;
                    } else {
                        // Normal server - use absolute paths
                        if (SafeExists(absoluteRoot)) {
                            existingRoot = normalizedRoot;
                        } else {
                            // Path doesn't exist, try to create it
                            var parts = normalizedRoot.Split('/').Where(p => !string.IsNullOrEmpty(p)).ToList();
                            var currentPath = "";

                            foreach (var part in parts) {
                                currentPath = string.IsNullOrEmpty(currentPath) ? part : $"{currentPath}/{part}";
                                var absolutePath = "/" + currentPath;

                                if (!SafeExists(absolutePath)) {
                                    try {
                                        _client.CreateDirectory(absolutePath);
                                    } catch (Exception ex) when (ex is Renci.SshNet.Common.SftpPermissionDeniedException ||
                                                                 ex is Renci.SshNet.Common.SftpPathNotFoundException) {
                                        _logger.SftpPermissionDenied(ex, "root path creation", absolutePath);
                                        break;
                                    }
                                }
                            }
                            existingRoot = normalizedRoot;
                        }
                        _useRelativePaths = false;
                    }

                    _effectiveRoot = existingRoot;
                } catch (Renci.SshNet.Common.SftpPermissionDeniedException ex) {
                    _logger.SftpPermissionDenied(ex, "root path setup", normalizedRoot);
                    _effectiveRoot = normalizedRoot;
                    _useRelativePaths = isChrooted;
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
            await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);
            return _client?.IsConnected == true;
        } catch (Exception ex) {
            _logger.ConnectionTestFailed(ex, "SFTP");
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
        await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);

        var fullPath = GetFullPath(path);

        return await ExecuteWithRetry(async () => {
            var items = new List<SyncItem>();

            if (!SafeExists(fullPath)) {
                return items;
            }

            var sftpFiles = await Task.Run(() => _client!.ListDirectory(fullPath), cancellationToken).ConfigureAwait(false);

            foreach (var file in sftpFiles) {
                // Skip current and parent directory entries
                if (file.Name == "." || file.Name == "..") {
                    continue;
                }

                cancellationToken.ThrowIfCancellationRequested();

                items.Add(new SyncItem {
                    Path = GetRelativePath(file.FullName),
                    IsDirectory = file.IsDirectory,
                    IsSymlink = file.Attributes?.IsSymbolicLink ?? false,
                    Size = file.Length,
                    LastModified = file.LastWriteTimeUtc,
                    Permissions = ConvertPermissionsToString(file)
                });
            }

            return (IEnumerable<SyncItem>)items;
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets metadata for a specific item (file or directory)
    /// </summary>
    /// <param name="path">The relative path to the item</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation</param>
    /// <returns>The sync item if it exists, null otherwise</returns>
    public async Task<SyncItem?> GetItemAsync(string path, CancellationToken cancellationToken = default) {
        await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);

        var fullPath = GetFullPath(path);

        return await ExecuteWithRetry(async () => {
            if (!_client!.Exists(fullPath)) {
                return null;
            }

            var file = await Task.Run(() => _client.Get(fullPath), cancellationToken).ConfigureAwait(false);

            return new SyncItem {
                Path = path,
                IsDirectory = file.IsDirectory,
                IsSymlink = file.Attributes?.IsSymbolicLink ?? false,
                Size = file.Length,
                LastModified = file.LastWriteTimeUtc,
                Permissions = ConvertPermissionsToString(file)
            };
        }, cancellationToken).ConfigureAwait(false);
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
        await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);

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
                }, cancellationToken).ConfigureAwait(false);
            } else {
                // Download without progress
                await Task.Run(() => _client.DownloadFile(fullPath, memoryStream), cancellationToken).ConfigureAwait(false);
            }

            memoryStream.Position = 0;
            return (Stream)memoryStream;
        }, cancellationToken).ConfigureAwait(false);
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
        await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);

        var fullPath = GetFullPath(path);

        // Ensure parent directories exist
        var directory = GetParentDirectory(fullPath);
        if (!string.IsNullOrEmpty(directory)) {
            await CreateDirectoryAsync(GetRelativePath(directory), cancellationToken).ConfigureAwait(false);
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
                }, cancellationToken).ConfigureAwait(false);
            } else {
                // Upload without progress
                await Task.Run(() => _client!.UploadFile(content, fullPath, true), cancellationToken).ConfigureAwait(false);
            }

            return true;
        }, cancellationToken).ConfigureAwait(false);
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
        await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);

        var fullPath = GetFullPath(path);

        // Don't attempt to create root or current directory - treat them as already existing
        if (fullPath == "." || fullPath == "/" || string.IsNullOrEmpty(fullPath)) {
            return;
        }

        await ExecuteWithRetry(async () => {
            if (SafeExists(fullPath)) {
                return true; // Directory already exists
            }

            // Create parent directories recursively if needed
            var parts = fullPath.Split('/').Where(p => !string.IsNullOrEmpty(p)).ToList();
            var currentPath = _useRelativePaths ? "" : (fullPath.StartsWith('/') ? "/" : "");

            foreach (var part in parts) {
                cancellationToken.ThrowIfCancellationRequested();

                if (_useRelativePaths) {
                    currentPath = string.IsNullOrEmpty(currentPath) ? part : $"{currentPath}/{part}";
                } else {
                    currentPath = string.IsNullOrEmpty(currentPath) || currentPath == "/"
                        ? $"/{part}"
                        : $"{currentPath}/{part}";
                }

                if (!SafeExists(currentPath)) {
                    try {
                        await Task.Run(() => _client!.CreateDirectory(currentPath), cancellationToken).ConfigureAwait(false);
                    } catch (Exception ex) when (ex is Renci.SshNet.Common.SftpPermissionDeniedException ||
                                                 ex is Renci.SshNet.Common.SftpPathNotFoundException) {
                        _logger.SftpPermissionDenied(ex, "directory creation", currentPath);
                        // Try alternate path form (relative vs absolute)
                        var alternatePath = currentPath.StartsWith('/') ? currentPath.TrimStart('/') : "/" + currentPath;
                        if (!SafeExists(alternatePath)) {
                            try {
                                await Task.Run(() => _client!.CreateDirectory(alternatePath), cancellationToken).ConfigureAwait(false);
                            } catch (Exception ex2) when (ex2 is Renci.SshNet.Common.SftpPermissionDeniedException ||
                                                          ex2 is Renci.SshNet.Common.SftpPathNotFoundException) {
                                _logger.SftpPermissionDenied(ex2, "directory creation (alternate path)", alternatePath);
                                // Both forms failed - check if either now exists
                                if (!SafeExists(currentPath) && !SafeExists(alternatePath)) {
                                    continue;
                                }
                            }
                        }
                    }
                }
            }

            return true;
        }, cancellationToken).ConfigureAwait(false);
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
        await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);

        var fullPath = GetFullPath(path);

        await ExecuteWithRetry(async () => {
            if (!_client!.Exists(fullPath)) {
                return true; // Already deleted
            }

            var file = _client.Get(fullPath);

            if (file.IsDirectory) {
                await Task.Run(() => DeleteDirectoryRecursive(fullPath, cancellationToken), cancellationToken).ConfigureAwait(false);
            } else {
                await Task.Run(() => _client.DeleteFile(fullPath), cancellationToken).ConfigureAwait(false);
            }

            return true;
        }, cancellationToken).ConfigureAwait(false);
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
        await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);

        var sourceFullPath = GetFullPath(sourcePath);
        var targetFullPath = GetFullPath(targetPath);

        // Ensure target parent directory exists
        // Use the original targetPath (normalized relative form) to compute parent directory
        // This avoids mixing full/absolute path forms that can confuse creation on chrooted servers
        var normalizedTargetPath = NormalizePath(targetPath);
        var targetParentRelative = GetParentDirectory(normalizedTargetPath);
        if (!string.IsNullOrEmpty(targetParentRelative)) {
            await CreateDirectoryAsync(targetParentRelative, cancellationToken).ConfigureAwait(false);
        }

        await ExecuteWithRetry(async () => {
            if (!_client!.Exists(sourceFullPath)) {
                throw new FileNotFoundException($"Source not found: {sourcePath}");
            }

            // SSH.NET's RenameFile maps to SSH_FXP_RENAME, which handles both
            // same-directory renames and cross-directory moves for files and directories
            await Task.Run(() => _client.RenameFile(sourceFullPath, targetFullPath), cancellationToken).ConfigureAwait(false);

            return true;
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Checks whether a file or directory exists on the SFTP server
    /// </summary>
    /// <param name="path">The relative path to check</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation</param>
    /// <returns>True if the file or directory exists, false otherwise</returns>
    public async Task<bool> ExistsAsync(string path, CancellationToken cancellationToken = default) {
        await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);

        var fullPath = GetFullPath(path);

        return await ExecuteWithRetry(async () => {
            return await Task.Run(() => _client!.Exists(fullPath), cancellationToken).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
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
        await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);

        return await ExecuteWithRetry(async () => {
            try {
                var statVfs = await Task.Run(() => _client!.GetStatus(RootPath.Length != 0 ? RootPath : "/"), cancellationToken).ConfigureAwait(false);

                var totalSpace = (long)(statVfs.TotalBlocks * statVfs.BlockSize);
                var usedSpace = (long)((statVfs.TotalBlocks - statVfs.FreeBlocks) * statVfs.BlockSize);

                return new StorageInfo {
                    TotalSpace = totalSpace,
                    UsedSpace = usedSpace
                };
            } catch (Exception ex) {
                _logger.SftpStatVfsUnsupported(ex);
                return new StorageInfo {
                    TotalSpace = -1,
                    UsedSpace = -1
                };
            }
        }, cancellationToken).ConfigureAwait(false);
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
        using var stream = await ReadFileAsync(path, cancellationToken).ConfigureAwait(false);
        using var sha256 = SHA256.Create();

        var hashBytes = await sha256.ComputeHashAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToBase64String(hashBytes);
    }

    /// <summary>
    /// Sets the last modified time for a file on the SFTP server
    /// </summary>
    public async Task SetLastModifiedAsync(string path, DateTime lastModified, CancellationToken cancellationToken = default) {
        await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);
        var fullPath = GetFullPath(path);

        await ExecuteWithRetry(async () => {
            if (_client!.Exists(fullPath)) {
                var attrs = _client.GetAttributes(fullPath);
                attrs.LastWriteTime = lastModified;
                await Task.Run(() => _client.SetAttributes(fullPath, attrs), cancellationToken).ConfigureAwait(false);
            }
            return true;
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Sets file permissions on the SFTP server
    /// </summary>
    public async Task SetPermissionsAsync(string path, string permissions, CancellationToken cancellationToken = default) {
        await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);
        var fullPath = GetFullPath(path);

        await ExecuteWithRetry(async () => {
            if (_client!.Exists(fullPath)) {
                // Parse numeric permission (e.g., "755")
                // SSH.NET's SetPermissions expects the decimal integer 755, not the octal value 0x1ED.
                // It extracts digits via decimal division and validates each is 0-7.
                if (permissions.Length >= 3 && permissions.Length <= 4
                    && short.TryParse(permissions, out var mode)
                    && permissions.All(c => c >= '0' && c <= '7')) {
                    var attrs = _client.GetAttributes(fullPath);
                    attrs.SetPermissions(mode);
                    await Task.Run(() => _client.SetAttributes(fullPath, attrs), cancellationToken).ConfigureAwait(false);
                }
            }
            return true;
        }, cancellationToken).ConfigureAwait(false);
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
    /// Safely checks if a path exists, handling permission denied exceptions for chrooted servers
    /// </summary>
    private bool SafeExists(string path) {
        try {
            return _client!.Exists(path);
        } catch (Renci.SshNet.Common.SftpPermissionDeniedException ex) {
            // Try alternate form (relative vs absolute)
            _logger.SftpTryingAlternatePath(ex, path);
            var alternatePath = path.StartsWith('/') ? path.TrimStart('/') : "/" + path;
            try {
                return _client!.Exists(alternatePath);
            } catch (Exception ex2) {
                _logger.SftpPermissionDenied(ex2, "existence check", path);
                return false;
            }
        }
    }

    /// <summary>
    /// Gets the full path on the SFTP server, respecting chroot behavior
    /// </summary>
    private string GetFullPath(string relativePath) {
        if (string.IsNullOrEmpty(relativePath) || relativePath == "/") {
            if (string.IsNullOrEmpty(_effectiveRoot)) {
                return _useRelativePaths ? "." : "/";
            }
            return _useRelativePaths ? _effectiveRoot! : $"/{_effectiveRoot}";
        }

        relativePath = NormalizePath(relativePath);

        if (string.IsNullOrEmpty(_effectiveRoot)) {
            return _useRelativePaths ? relativePath : $"/{relativePath}";
        }

        return _useRelativePaths
            ? $"{_effectiveRoot}/{relativePath}"
            : $"/{_effectiveRoot}/{relativePath}";
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
        if (string.IsNullOrEmpty(path)) {
            return string.Empty;
        }

        var lastSlash = path.LastIndexOf('/');
        // If there is no parent (path has no slash or the only slash is the first character),
        // return empty string so callers skip creating directories for root
        if (lastSlash <= 0) {
            return string.Empty;
        }

        return path.Substring(0, lastSlash);
    }

    /// <summary>
    /// Converts SFTP file permissions to a string representation
    /// </summary>
    private static string ConvertPermissionsToString(ISftpFile file) {
        if (file.Attributes is null) {
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
    /// Executes an operation with retry logic
    /// </summary>
    private async Task<T> ExecuteWithRetry<T>(Func<Task<T>> operation, CancellationToken cancellationToken) {
        Exception? lastException = null;

        for (int attempt = 0; attempt <= _maxRetries; attempt++) {
            try {
                cancellationToken.ThrowIfCancellationRequested();
                return await operation().ConfigureAwait(false);
            } catch (Exception ex) when (attempt < _maxRetries && IsRetriableException(ex)) {
                lastException = ex;
                _logger.StorageOperationRetry("SFTP", attempt + 1, _maxRetries);

                // Reconnect if connection was lost
                if (ex is SshConnectionException || ex is SshOperationTimeoutException) {
                    _logger.StorageReconnecting(attempt + 1, "SFTP");
                    try {
                        _client?.Disconnect();
                        _client?.Dispose();
                        _client = null;
                        await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);
                    } catch (Exception reconnectEx) {
                        _logger.StorageReconnectFailed(reconnectEx, "SFTP");
                    }
                }

                await Task.Delay(_retryDelay * (attempt + 1), cancellationToken).ConfigureAwait(false);
            }
        }

        throw lastException ?? new InvalidOperationException("Operation failed");
    }

    /// <summary>
    /// Determines if an exception is retriable
    /// </summary>
    internal static bool IsRetriableException(Exception ex) {
        return ex is SshConnectionException or SshOperationTimeoutException;
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
            } catch (Exception ex) {
                _logger.StorageDisconnectFailed(ex, "SFTP");
            }

            _client?.Dispose();
            _connectionSemaphore?.Dispose();
            _disposed = true;
        }

        GC.SuppressFinalize(this);
    }

    #endregion
}
