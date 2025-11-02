using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text;
using WebDav;
using Oire.SharpSync.Auth;
using Oire.SharpSync.Core;

namespace Oire.SharpSync.Storage;

/// <summary>
/// WebDAV storage implementation with OAuth2 support, chunked uploads, and retry logic
/// Designed for production use with Nextcloud/OCIS with platform-specific optimizations
/// </summary>
public class WebDavStorage: ISyncStorage, IDisposable {
    private WebDavClient _client;
    private readonly string _baseUrl;
    private readonly string _rootPath;
    private readonly IOAuth2Provider? _oauth2Provider;
    private readonly OAuth2Config? _oauth2Config;
    private OAuth2Result? _oauth2Result;
    private readonly SemaphoreSlim _authSemaphore;

    // Configuration
    private readonly int _chunkSize;
    private readonly int _maxRetries;
    private readonly TimeSpan _retryDelay;
    private readonly TimeSpan _timeout;

    // Platform capabilities
    private ServerCapabilities? _serverCapabilities;
    private readonly SemaphoreSlim _capabilitiesSemaphore;

    private bool _disposed;

    public StorageType StorageType => StorageType.WebDav;
    public string RootPath => _rootPath;

    /// <summary>
    /// Creates WebDAV storage with OAuth2 support
    /// </summary>
    /// <param name="baseUrl">Base WebDAV URL (e.g., https://cloud.example.com/remote.php/dav/files/username/)</param>
    /// <param name="rootPath">Root path within the WebDAV share</param>
    /// <param name="oauth2Provider">OAuth2 provider for authentication</param>
    /// <param name="oauth2Config">OAuth2 configuration</param>
    /// <param name="chunkSizeBytes">Chunk size for large file uploads (default 10MB)</param>
    /// <param name="maxRetries">Maximum retry attempts (default 3)</param>
    /// <param name="timeoutSeconds">Request timeout in seconds (default 300)</param>
    public WebDavStorage(
        string baseUrl,
        string rootPath = "",
        IOAuth2Provider? oauth2Provider = null,
        OAuth2Config? oauth2Config = null,
        int chunkSizeBytes = 10 * 1024 * 1024, // 10MB
        int maxRetries = 3,
        int timeoutSeconds = 300) {
        if (string.IsNullOrWhiteSpace(baseUrl)) {
            throw new ArgumentException("Base URL cannot be empty", nameof(baseUrl));
        }

        _baseUrl = baseUrl.TrimEnd('/');
        _rootPath = rootPath.Trim('/');
        _oauth2Provider = oauth2Provider;
        _oauth2Config = oauth2Config;
        _authSemaphore = new SemaphoreSlim(1, 1);
        _capabilitiesSemaphore = new SemaphoreSlim(1, 1);

        _chunkSize = chunkSizeBytes;
        _maxRetries = maxRetries;
        _retryDelay = TimeSpan.FromSeconds(1);
        _timeout = TimeSpan.FromSeconds(timeoutSeconds);

        // Configure WebDAV client
        var clientParams = new WebDavClientParams {
            BaseAddress = new Uri(_baseUrl),
            Timeout = _timeout
        };

        _client = new WebDavClient(clientParams);
    }

    /// <summary>
    /// Creates WebDAV storage with basic authentication
    /// </summary>
    public WebDavStorage(
        string baseUrl,
        string username,
        string password,
        string rootPath = "",
        int chunkSizeBytes = 10 * 1024 * 1024,
        int maxRetries = 3,
        int timeoutSeconds = 300)
        : this(baseUrl: baseUrl, rootPath: rootPath, oauth2Provider: null, oauth2Config: null, chunkSizeBytes: chunkSizeBytes, maxRetries: maxRetries, timeoutSeconds: timeoutSeconds) {
        // Configure basic authentication
        var credentials = new NetworkCredential(username, password);
        var clientParams = new WebDavClientParams {
            BaseAddress = new Uri(_baseUrl),
            Credentials = credentials,
            Timeout = _timeout
        };

        _client?.Dispose();
        _client = new WebDavClient(clientParams);
    }

    /// <summary>
    /// Event raised when upload/download progress changes
    /// </summary>
    public event EventHandler<StorageProgressEventArgs>? ProgressChanged;

    /// <summary>
    /// Gets server capabilities for optimization
    /// </summary>
    public async Task<ServerCapabilities> GetServerCapabilitiesAsync(CancellationToken cancellationToken = default) {
        if (_serverCapabilities != null)
            return _serverCapabilities;

        await _capabilitiesSemaphore.WaitAsync(cancellationToken);
        try {
            if (_serverCapabilities != null)
                return _serverCapabilities;

            _serverCapabilities = await DetectServerCapabilitiesAsync(cancellationToken);
            return _serverCapabilities;
        } finally {
            _capabilitiesSemaphore.Release();
        }
    }

    /// <summary>
    /// Authenticates using OAuth2 if configured
    /// </summary>
    public async Task<bool> AuthenticateAsync(CancellationToken cancellationToken = default) {
        if (_oauth2Provider == null || _oauth2Config == null)
            return true; // No OAuth2 configured, assume basic auth or anonymous

        await _authSemaphore.WaitAsync(cancellationToken);
        try {
            // Check if current token is still valid
            if (_oauth2Result?.IsValid == true && !_oauth2Result.WillExpireWithin(TimeSpan.FromMinutes(5))) {
                return true;
            }

            // Try refresh token first
            if (_oauth2Result?.RefreshToken != null) {
                try {
                    _oauth2Result = await _oauth2Provider.RefreshTokenAsync(_oauth2Config, _oauth2Result.RefreshToken, cancellationToken);
                    UpdateClientAuth();
                    return true;
                } catch {
                    // Refresh failed, fall through to full authentication
                }
            }

            // Perform full OAuth2 authentication
            _oauth2Result = await _oauth2Provider.AuthenticateAsync(_oauth2Config, cancellationToken);
            UpdateClientAuth();
            return _oauth2Result.IsValid;
        } finally {
            _authSemaphore.Release();
        }
    }

    private void UpdateClientAuth() {
        if (_oauth2Result?.AccessToken != null) {
            // Recreate client with OAuth2 token
            var clientParams = new WebDavClientParams {
                BaseAddress = new Uri(_baseUrl),
                Timeout = _timeout,
                DefaultRequestHeaders = new Dictionary<string, string>
                {
                    { "Authorization", $"Bearer {_oauth2Result.AccessToken}" }
                }
            };

            _client?.Dispose();
            _client = new WebDavClient(clientParams);
        }
    }

    public async Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default) {
        if (!await EnsureAuthenticated(cancellationToken))
            return false;

        return await ExecuteWithRetry(async () => {
            var result = await _client.Propfind(_baseUrl, new PropfindParameters {
                RequestType = PropfindRequestType.AllProperties,
                CancellationToken = cancellationToken
            });
            return result.IsSuccessful;
        }, cancellationToken);
    }

    public async Task<IEnumerable<SyncItem>> ListItemsAsync(string path, CancellationToken cancellationToken = default) {
        if (!await EnsureAuthenticated(cancellationToken))
            throw new UnauthorizedAccessException("Authentication failed");

        var fullPath = GetFullPath(path);

        return await ExecuteWithRetry(async () => {
            var result = await _client.Propfind(fullPath, new PropfindParameters {
                RequestType = PropfindRequestType.AllProperties,
                CancellationToken = cancellationToken
            });

            if (!result.IsSuccessful) {
                if (result.StatusCode == 404)
                    return Enumerable.Empty<SyncItem>();

                throw new HttpRequestException($"WebDAV request failed: {result.StatusCode}");
            }

            return result.Resources
                .Skip(1) // Skip the directory itself
                .Select(resource => new SyncItem {
                    Path = GetRelativePath(resource.Uri),
                    IsDirectory = resource.IsCollection,
                    Size = resource.ContentLength ?? 0,
                    LastModified = resource.LastModifiedDate?.ToUniversalTime() ?? DateTime.MinValue,
                    ETag = NormalizeETag(resource.ETag),
                    MimeType = resource.ContentType
                });
        }, cancellationToken);
    }

    public async Task<SyncItem?> GetItemAsync(string path, CancellationToken cancellationToken = default) {
        if (!await EnsureAuthenticated(cancellationToken))
            return null;

        var fullPath = GetFullPath(path);

        return await ExecuteWithRetry(async () => {
            var result = await _client.Propfind(fullPath, new PropfindParameters {
                RequestType = PropfindRequestType.AllProperties,
                CancellationToken = cancellationToken
            });

            if (!result.IsSuccessful)
                return null;

            var resource = result.Resources.FirstOrDefault();
            if (resource == null)
                return null;

            return new SyncItem {
                Path = path,
                IsDirectory = resource.IsCollection,
                Size = resource.ContentLength ?? 0,
                LastModified = resource.LastModifiedDate?.ToUniversalTime() ?? DateTime.MinValue,
                ETag = NormalizeETag(resource.ETag),
                MimeType = resource.ContentType
            };
        }, cancellationToken);
    }

    public async Task<Stream> ReadFileAsync(string path, CancellationToken cancellationToken = default) {
        if (!await EnsureAuthenticated(cancellationToken))
            throw new UnauthorizedAccessException("Authentication failed");

        var fullPath = GetFullPath(path);

        // Get file info first to determine if we need progress reporting
        var item = await GetItemAsync(path, cancellationToken);
        var needsProgress = item?.Size > _chunkSize;

        return await ExecuteWithRetry(async () => {
            var response = await _client.GetRawFile(fullPath, new GetFileParameters {
                CancellationToken = cancellationToken
            });

            if (!response.IsSuccessful) {
                if (response.StatusCode == 404)
                    throw new FileNotFoundException($"File not found: {path}");

                throw new HttpRequestException($"WebDAV request failed: {response.StatusCode}");
            }

            // For large files, wrap stream with progress reporting
            if (needsProgress && item != null) {
                return new ProgressStream(response.Stream, item.Size,
                    (bytes, total) => RaiseProgressChanged(path, bytes, total, StorageOperation.Download));
            }

            return response.Stream;
        }, cancellationToken);
    }

    public async Task WriteFileAsync(string path, Stream content, CancellationToken cancellationToken = default) {
        if (!await EnsureAuthenticated(cancellationToken))
            throw new UnauthorizedAccessException("Authentication failed");

        var fullPath = GetFullPath(path);

        // Ensure parent directories exist
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) {
            await CreateDirectoryAsync(directory, cancellationToken);
        }

        // For small files, use regular upload
        if (!content.CanSeek || content.Length <= _chunkSize) {
            await ExecuteWithRetry(async () => {
                var result = await _client.PutFile(fullPath, content, new PutFileParameters {
                    CancellationToken = cancellationToken
                });

                if (!result.IsSuccessful)
                    throw new HttpRequestException($"WebDAV upload failed: {result.StatusCode}");

                return true;
            }, cancellationToken);

            return;
        }

        // For large files, use chunked upload (if supported by server)
        await WriteFileChunkedAsync(fullPath, path, content, cancellationToken);
    }

    /// <summary>
    /// Chunked upload implementation with platform-specific optimizations
    /// </summary>
    private async Task WriteFileChunkedAsync(string fullPath, string relativePath, Stream content, CancellationToken cancellationToken) {
        var capabilities = await GetServerCapabilitiesAsync(cancellationToken);

        // Use platform-specific chunking if available
        if (capabilities.IsNextcloud && capabilities.ChunkingVersion >= 2) {
            await WriteFileNextcloudChunkedAsync(fullPath, relativePath, content, cancellationToken);
        } else if (capabilities.IsOcis && capabilities.SupportsOcisChunking) {
            await WriteFileOcisChunkedAsync(fullPath, relativePath, content, cancellationToken);
        } else {
            // Fallback to generic WebDAV upload with progress
            await WriteFileGenericAsync(fullPath, relativePath, content, cancellationToken);
        }
    }

    /// <summary>
    /// Generic WebDAV upload with progress reporting
    /// </summary>
    private async Task WriteFileGenericAsync(string fullPath, string relativePath, Stream content, CancellationToken cancellationToken) {
        var totalSize = content.Length;
        content.Position = 0;

        await ExecuteWithRetry(async () => {
            // Report initial progress
            RaiseProgressChanged(relativePath, 0, totalSize, StorageOperation.Upload);

            var result = await _client.PutFile(fullPath, content, new PutFileParameters {
                CancellationToken = cancellationToken
            });

            if (!result.IsSuccessful)
                throw new HttpRequestException($"WebDAV upload failed: {result.StatusCode}");

            // Report completion
            RaiseProgressChanged(relativePath, totalSize, totalSize, StorageOperation.Upload);

            return true;
        }, cancellationToken);
    }

    /// <summary>
    /// Nextcloud-specific chunked upload using their chunking v2 API
    /// </summary>
    private async Task WriteFileNextcloudChunkedAsync(string fullPath, string relativePath, Stream content, CancellationToken cancellationToken) {
        var totalSize = content.Length;
        var uploadId = Guid.NewGuid().ToString("N");
        var chunkFolder = $".file-chunking/{uploadId}";

        try {
            // Create chunking folder
            await CreateDirectoryAsync(chunkFolder, cancellationToken);

            // Upload chunks
            var chunkNumber = 0;
            var uploadedBytes = 0L;
            var buffer = new byte[_chunkSize];

            content.Position = 0;

            while (uploadedBytes < totalSize) {
                var bytesRead = await content.ReadAsync(buffer, cancellationToken);
                if (bytesRead == 0)
                    break;

                // Upload chunk
                var chunkPath = $"{chunkFolder}/{chunkNumber:D6}";
                using var chunkStream = new MemoryStream(buffer, 0, bytesRead);

                await ExecuteWithRetry(async () => {
                    var result = await _client.PutFile(GetFullPath(chunkPath), chunkStream, new PutFileParameters {
                        CancellationToken = cancellationToken
                    });

                    if (!result.IsSuccessful)
                        throw new HttpRequestException($"Chunk upload failed: {result.StatusCode}");

                    return true;
                }, cancellationToken);

                uploadedBytes += bytesRead;
                chunkNumber++;

                // Report progress
                RaiseProgressChanged(relativePath, uploadedBytes, totalSize, StorageOperation.Upload);
            }

            // Assemble chunks
            await AssembleNextcloudChunksAsync(chunkFolder, fullPath, totalSize, cancellationToken);
        } finally {
            // Clean up chunks folder
            try {
                await DeleteAsync(chunkFolder, cancellationToken);
            } catch { /* Ignore cleanup errors */ }
        }
    }

    /// <summary>
    /// OCIS-specific chunked upload
    /// </summary>
    private async Task WriteFileOcisChunkedAsync(string fullPath, string relativePath, Stream content, CancellationToken cancellationToken) {
        // OCIS uses TUS protocol for resumable uploads
        // For now, fall back to generic upload
        // A full implementation would use TUS client
        await WriteFileGenericAsync(fullPath, relativePath, content, cancellationToken);
    }

    /// <summary>
    /// Assembles Nextcloud chunks into final file
    /// </summary>
    private async Task AssembleNextcloudChunksAsync(string chunkFolder, string targetPath, long fileSize, CancellationToken cancellationToken) {
        // Nextcloud assembly endpoint
        var assemblyPath = $"{GetFullPath(chunkFolder)}/.assembling";

        await ExecuteWithRetry(async () => {
            // Create assembly marker with target info
            var assemblyInfo = JsonSerializer.Serialize(new {
                dest = targetPath,
                size = fileSize
            });

            using var assemblyStream = new MemoryStream(Encoding.UTF8.GetBytes(assemblyInfo));
            var result = await _client.PutFile(assemblyPath, assemblyStream, new PutFileParameters {
                CancellationToken = cancellationToken
            });

            if (!result.IsSuccessful)
                throw new HttpRequestException($"Chunk assembly failed: {result.StatusCode}");

            return true;
        }, cancellationToken);
    }

    public async Task CreateDirectoryAsync(string path, CancellationToken cancellationToken = default) {
        if (!await EnsureAuthenticated(cancellationToken))
            throw new UnauthorizedAccessException("Authentication failed");

        var fullPath = GetFullPath(path);

        await ExecuteWithRetry(async () => {
            // Check if directory already exists
            var existsResult = await _client.Propfind(fullPath, new PropfindParameters {
                CancellationToken = cancellationToken
            });

            if (existsResult.IsSuccessful)
                return true; // Directory already exists

            var result = await _client.Mkcol(fullPath, new MkColParameters {
                CancellationToken = cancellationToken
            });

            if (!result.IsSuccessful && result.StatusCode != 405) // 405 = already exists
                throw new HttpRequestException($"Directory creation failed: {result.StatusCode}");

            return true;
        }, cancellationToken);
    }

    public async Task DeleteAsync(string path, CancellationToken cancellationToken = default) {
        if (!await EnsureAuthenticated(cancellationToken))
            throw new UnauthorizedAccessException("Authentication failed");

        var fullPath = GetFullPath(path);

        await ExecuteWithRetry(async () => {
            var result = await _client.Delete(fullPath, new DeleteParameters {
                CancellationToken = cancellationToken
            });

            if (!result.IsSuccessful && result.StatusCode != 404) // 404 = already deleted
                throw new HttpRequestException($"Delete failed: {result.StatusCode}");

            return true;
        }, cancellationToken);
    }

    public async Task MoveAsync(string sourcePath, string targetPath, CancellationToken cancellationToken = default) {
        if (!await EnsureAuthenticated(cancellationToken))
            throw new UnauthorizedAccessException("Authentication failed");

        var sourceFullPath = GetFullPath(sourcePath);
        var targetFullPath = GetFullPath(targetPath);

        // Ensure target parent directory exists
        var targetDirectory = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrEmpty(targetDirectory)) {
            await CreateDirectoryAsync(targetDirectory, cancellationToken);
        }

        await ExecuteWithRetry(async () => {
            var result = await _client.Move(sourceFullPath, targetFullPath, new MoveParameters {
                CancellationToken = cancellationToken
            });

            if (!result.IsSuccessful)
                throw new HttpRequestException($"Move failed: {result.StatusCode}");

            return true;
        }, cancellationToken);
    }

    public async Task<bool> ExistsAsync(string path, CancellationToken cancellationToken = default) {
        if (!await EnsureAuthenticated(cancellationToken))
            return false;

        var fullPath = GetFullPath(path);

        return await ExecuteWithRetry(async () => {
            var result = await _client.Propfind(fullPath, new PropfindParameters {
                RequestType = PropfindRequestType.AllProperties,
                CancellationToken = cancellationToken
            });

            return result.IsSuccessful;
        }, cancellationToken);
    }

    public async Task<StorageInfo> GetStorageInfoAsync(CancellationToken cancellationToken = default) {
        if (!await EnsureAuthenticated(cancellationToken))
            return new StorageInfo { TotalSpace = -1, UsedSpace = -1 };

        return await ExecuteWithRetry(async () => {
            // Try to get quota information from the root
            var result = await _client.Propfind(_baseUrl, new PropfindParameters {
                CancellationToken = cancellationToken
            });

            if (result.IsSuccessful && result.Resources.Count != 0) {
                var resource = result.Resources.First();

                // WebDAV quota properties (if supported by server)
                var quotaUsed = resource.Properties?.FirstOrDefault(p => p.Name.LocalName == "quota-used-bytes")?.Value;
                var quotaAvailable = resource.Properties?.FirstOrDefault(p => p.Name.LocalName == "quota-available-bytes")?.Value;

                if (long.TryParse(quotaUsed, out var used) && long.TryParse(quotaAvailable, out var available)) {
                    return new StorageInfo {
                        UsedSpace = used,
                        TotalSpace = used + available,
                        Quota = used + available
                    };
                }
            }

            // Fallback - return unknown values
            return new StorageInfo {
                TotalSpace = -1,
                UsedSpace = -1
            };
        }, cancellationToken);
    }

    public async Task<string> ComputeHashAsync(string path, CancellationToken cancellationToken = default) {
        // Use ETag if available for performance (avoids downloading the file)
        var item = await GetItemAsync(path, cancellationToken);
        if (!string.IsNullOrEmpty(item?.ETag)) {
            return item.ETag;
        }

        // For Nextcloud/OCIS, try to get checksum from properties
        var capabilities = await GetServerCapabilitiesAsync(cancellationToken);
        if (capabilities.IsNextcloud || capabilities.IsOcis) {
            var checksum = await GetServerChecksumAsync(path, cancellationToken);
            if (!string.IsNullOrEmpty(checksum))
                return checksum;
        }

        // Fallback to downloading and hashing (expensive for large files)
        using var stream = await ReadFileAsync(path, cancellationToken);
        using var sha256 = SHA256.Create();

        var hashBytes = await sha256.ComputeHashAsync(stream, cancellationToken);
        return Convert.ToBase64String(hashBytes);
    }

    #region Helper Methods

    /// <summary>
    /// Normalizes ETag values for consistent comparison
    /// </summary>
    private static string? NormalizeETag(string? etag) {
        if (string.IsNullOrEmpty(etag))
            return null;

        // Remove quotes and W/ prefix (weak ETags)
        etag = etag.Trim();
        if (etag.StartsWith("W/"))
            etag = etag.Substring(2);

        return etag.Trim('"');
    }

    /// <summary>
    /// Gets server-side checksum for a file (Nextcloud/OCIS specific)
    /// </summary>
    private async Task<string?> GetServerChecksumAsync(string path, CancellationToken cancellationToken) {
        try {
            var fullPath = GetFullPath(path);
            var result = await _client.Propfind(fullPath, new PropfindParameters {
                RequestType = PropfindRequestType.AllProperties,
                CancellationToken = cancellationToken
            });

            if (!result.IsSuccessful || result.Resources.Count == 0)
                return null;

            var resource = result.Resources.First();

            // Look for checksum properties (Nextcloud uses oc:checksum)
            var checksumProp = resource.Properties?.FirstOrDefault(p =>
                p.Name.LocalName == "checksum" &&
                (p.Name.NamespaceName.Contains("owncloud") || p.Name.NamespaceName.Contains("nextcloud")));

            if (checksumProp != null && !string.IsNullOrEmpty(checksumProp.Value)) {
                // Format is usually "SHA1:hash" or "MD5:hash"
                var parts = checksumProp.Value.Split(':');
                if (parts.Length == 2)
                    return parts[1];
            }

            return null;
        } catch {
            return null;
        }
    }

    /// <summary>
    /// Detects server capabilities for optimization
    /// </summary>
    private async Task<ServerCapabilities> DetectServerCapabilitiesAsync(CancellationToken cancellationToken) {
        var capabilities = new ServerCapabilities();

        try {
            // Check for Nextcloud/OCIS status endpoint
            var statusUrl = _baseUrl.Replace("/remote.php/dav", "").Replace("/remote.php/webdav", "") + "/status.php";

            using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

            // Add auth header if using OAuth2
            if (_oauth2Result?.AccessToken != null) {
                httpClient.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _oauth2Result.AccessToken);
            }

            try {
                var response = await httpClient.GetAsync(statusUrl, cancellationToken);
                if (response.IsSuccessStatusCode) {
                    var json = await response.Content.ReadAsStringAsync(cancellationToken);
                    using var doc = JsonDocument.Parse(json);

                    if (doc.RootElement.TryGetProperty("productname", out var productName)) {
                        var product = productName.GetString()?.ToLowerInvariant() ?? "";
                        capabilities.IsNextcloud = product.Contains("nextcloud");
                        capabilities.IsOcis = product.Contains("ocis") || product.Contains("owncloud infinite scale");
                    }

                    if (doc.RootElement.TryGetProperty("version", out var version)) {
                        capabilities.ServerVersion = version.GetString() ?? "";
                    }
                }
            } catch { /* Ignore status check failures */ }

            // Check for capabilities endpoint (Nextcloud/OCIS)
            if (capabilities.IsNextcloud || capabilities.IsOcis) {
                var capabilitiesUrl = _baseUrl.Replace("/remote.php/dav", "").Replace("/remote.php/webdav", "") + "/ocs/v1.php/cloud/capabilities";

                try {
                    var response = await httpClient.GetAsync(capabilitiesUrl, cancellationToken);
                    if (response.IsSuccessStatusCode) {
                        var json = await response.Content.ReadAsStringAsync(cancellationToken);
                        using var doc = JsonDocument.Parse(json);

                        // Check for chunking support
                        if (doc.RootElement.TryGetProperty("ocs", out var ocs) &&
                            ocs.TryGetProperty("data", out var data) &&
                            data.TryGetProperty("capabilities", out var caps)) {
                            // Nextcloud chunking
                            if (caps.TryGetProperty("files", out var files) &&
                                files.TryGetProperty("bigfilechunking", out var chunking)) {
                                capabilities.SupportsChunking = chunking.GetBoolean();
                                capabilities.ChunkingVersion = 2; // v2 is standard for modern Nextcloud
                            }

                            // OCIS uses TUS
                            if (capabilities.IsOcis) {
                                capabilities.SupportsOcisChunking = true; // OCIS always supports TUS
                            }
                        }
                    }
                } catch { /* Ignore capabilities check failures */ }
            }
        } catch { /* Ignore all detection failures - use defaults */ }

        return capabilities;
    }

    private string GetFullPath(string relativePath) {
        if (string.IsNullOrEmpty(relativePath) || relativePath == "/")
            return string.IsNullOrEmpty(_rootPath) ? _baseUrl : $"{_baseUrl}/{_rootPath}";

        relativePath = relativePath.Trim('/');

        if (string.IsNullOrEmpty(_rootPath))
            return $"{_baseUrl}/{relativePath}";
        else
            return $"{_baseUrl}/{_rootPath}/{relativePath}";
    }

    private string GetRelativePath(string fullUrl) {
        var prefix = string.IsNullOrEmpty(_rootPath) ? _baseUrl : $"{_baseUrl}/{_rootPath}";

        if (fullUrl.StartsWith(prefix)) {
            var relativePath = fullUrl.Substring(prefix.Length).Trim('/');
            return string.IsNullOrEmpty(relativePath) ? "/" : relativePath;
        }

        return fullUrl;
    }

    private async Task<bool> EnsureAuthenticated(CancellationToken cancellationToken) {
        if (_oauth2Provider != null)
            return await AuthenticateAsync(cancellationToken);
        return true;
    }

    private async Task<T> ExecuteWithRetry<T>(Func<Task<T>> operation, CancellationToken cancellationToken) {
        Exception? lastException = null;

        for (int attempt = 0; attempt <= _maxRetries; attempt++) {
            try {
                cancellationToken.ThrowIfCancellationRequested();
                return await operation();
            } catch (Exception ex) when (attempt < _maxRetries && IsRetriableException(ex)) {
                lastException = ex;
                await Task.Delay(_retryDelay * (attempt + 1), cancellationToken);
            }
        }

        throw lastException ?? new InvalidOperationException("Operation failed");
    }

    private static bool IsRetriableException(Exception ex) {
        return ex is HttpRequestException ||
               ex is TaskCanceledException ||
               ex is SocketException ||
               (ex is HttpRequestException httpEx && httpEx.Message.Contains('5'));
    }

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

    public void Dispose() {
        if (!_disposed) {
            _client?.Dispose();
            _authSemaphore?.Dispose();
            _capabilitiesSemaphore?.Dispose();
            _disposed = true;
        }
    }

    #endregion
}
