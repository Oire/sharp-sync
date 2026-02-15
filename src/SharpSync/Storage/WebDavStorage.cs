using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using WebDav;
using Oire.SharpSync.Auth;
using Oire.SharpSync.Core;
using Oire.SharpSync.Logging;

namespace Oire.SharpSync.Storage;

/// <summary>
/// WebDAV storage implementation with OAuth2 support, chunked uploads, and retry logic
/// Designed for production use with Nextcloud/OCIS with platform-specific optimizations
/// </summary>
public class WebDavStorage: ISyncStorage, IDisposable {
    private WebDavClient _client;
    private readonly string _baseUrl;
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

    private readonly ILogger _logger;
    private bool _disposed;

    /// <summary>
    /// Gets the storage type (always returns <see cref="Core.StorageType.WebDav"/>)
    /// </summary>
    public StorageType StorageType => StorageType.WebDav;

    /// <summary>
    /// Gets the root path within the WebDAV share
    /// </summary>
    public string RootPath { get; }

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
    /// <param name="logger">Optional logger for diagnostic output</param>
    public WebDavStorage(
        string baseUrl,
        string rootPath = "",
        IOAuth2Provider? oauth2Provider = null,
        OAuth2Config? oauth2Config = null,
        int chunkSizeBytes = 10 * 1024 * 1024, // 10MB
        int maxRetries = 3,
        int timeoutSeconds = 300,
        ILogger? logger = null) {
        if (string.IsNullOrWhiteSpace(baseUrl)) {
            throw new ArgumentException("Base URL cannot be empty", nameof(baseUrl));
        }

        _baseUrl = baseUrl.TrimEnd('/');
        RootPath = rootPath.Trim('/');
        _oauth2Provider = oauth2Provider;
        _oauth2Config = oauth2Config;
        _authSemaphore = new SemaphoreSlim(1, 1);
        _capabilitiesSemaphore = new SemaphoreSlim(1, 1);

        _chunkSize = chunkSizeBytes;
        _maxRetries = maxRetries;
        _retryDelay = TimeSpan.FromSeconds(1);
        _timeout = TimeSpan.FromSeconds(timeoutSeconds);

        _logger = logger ?? NullLogger.Instance;

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
    /// <param name="baseUrl">Base WebDAV URL (e.g., https://cloud.example.com/remote.php/dav/files/username/)</param>
    /// <param name="username">Username for basic authentication</param>
    /// <param name="password">Password for basic authentication</param>
    /// <param name="rootPath">Root path within the WebDAV share</param>
    /// <param name="chunkSizeBytes">Chunk size for large file uploads (default 10MB)</param>
    /// <param name="maxRetries">Maximum retry attempts (default 3)</param>
    /// <param name="timeoutSeconds">Request timeout in seconds (default 300)</param>
    /// <param name="logger">Optional logger for diagnostic output</param>
    public WebDavStorage(
        string baseUrl,
        string username,
        string password,
        string rootPath = "",
        int chunkSizeBytes = 10 * 1024 * 1024,
        int maxRetries = 3,
        int timeoutSeconds = 300,
        ILogger? logger = null)
        : this(baseUrl: baseUrl, rootPath: rootPath, oauth2Provider: null, oauth2Config: null, chunkSizeBytes: chunkSizeBytes, maxRetries: maxRetries, timeoutSeconds: timeoutSeconds, logger: logger) {
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
        if (_serverCapabilities is not null) {
            return _serverCapabilities;
        }

        await _capabilitiesSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try {
            if (_serverCapabilities is not null) {
                return _serverCapabilities;
            }

            _serverCapabilities = await DetectServerCapabilitiesAsync(cancellationToken).ConfigureAwait(false);
            return _serverCapabilities;
        } finally {
            _capabilitiesSemaphore.Release();
        }
    }

    /// <summary>
    /// Authenticates using OAuth2 if configured
    /// </summary>
    public async Task<bool> AuthenticateAsync(CancellationToken cancellationToken = default) {
        if (_oauth2Provider is null || _oauth2Config is null) {
            return true; // No OAuth2 configured, assume basic auth or anonymous
        }

        await _authSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try {
            // Check if current token is still valid
            if (_oauth2Result?.IsValid == true && !_oauth2Result.WillExpireWithin(TimeSpan.FromMinutes(5))) {
                return true;
            }

            // Try refresh token first
            if (_oauth2Result?.RefreshToken is not null) {
                try {
                    _oauth2Result = await _oauth2Provider.RefreshTokenAsync(_oauth2Config, _oauth2Result.RefreshToken, cancellationToken).ConfigureAwait(false);
                    UpdateClientAuth();
                    return true;
                } catch (Exception ex) {
                    _logger.OAuthTokenRefreshFailed(ex);
                }
            }

            // Perform full OAuth2 authentication
            _oauth2Result = await _oauth2Provider.AuthenticateAsync(_oauth2Config, cancellationToken).ConfigureAwait(false);
            UpdateClientAuth();
            return _oauth2Result.IsValid;
        } finally {
            _authSemaphore.Release();
        }
    }

    private void UpdateClientAuth() {
        if (_oauth2Result?.AccessToken is not null) {
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

    /// <summary>
    /// Tests whether the WebDAV server is accessible and authentication is working
    /// </summary>
    /// <param name="cancellationToken">Cancellation token to cancel the operation</param>
    /// <returns>True if the connection is successful, false otherwise</returns>
    public async Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default) {
        if (!await EnsureAuthenticated(cancellationToken).ConfigureAwait(false))
            return false;

        return await ExecuteWithRetry(async () => {
            var result = await _client.Propfind(_baseUrl, new PropfindParameters {
                RequestType = PropfindRequestType.AllProperties,
                CancellationToken = cancellationToken
            }).ConfigureAwait(false);
            return result.IsSuccessful;
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Lists all items (files and directories) in the specified path
    /// </summary>
    /// <param name="path">The relative path to list items from</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation</param>
    /// <returns>A collection of sync items representing files and directories</returns>
    public async Task<IEnumerable<SyncItem>> ListItemsAsync(string path, CancellationToken cancellationToken = default) {
        if (!await EnsureAuthenticated(cancellationToken).ConfigureAwait(false))
            throw new UnauthorizedAccessException("Authentication failed");

        var fullPath = GetFullPath(path);

        return await ExecuteWithRetry(async () => {
            var result = await _client.Propfind(fullPath, new PropfindParameters {
                RequestType = PropfindRequestType.AllProperties,
                CancellationToken = cancellationToken
            }).ConfigureAwait(false);

            if (!result.IsSuccessful) {
                if (result.StatusCode == 404) {
                    return Enumerable.Empty<SyncItem>();
                }

                throw new HttpRequestException($"WebDAV request failed: {result.StatusCode}");
            }

            return result.Resources
                .Skip(1) // Skip the directory itself
                .Where(resource => resource.Uri is not null)
                .Select(resource => new SyncItem {
                    Path = GetRelativePath(resource.Uri!),
                    IsDirectory = resource.IsCollection,
                    Size = resource.ContentLength ?? 0,
                    LastModified = resource.LastModifiedDate?.ToUniversalTime() ?? DateTime.MinValue,
                    ETag = NormalizeETag(resource.ETag)
                });
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets metadata for a specific item (file or directory)
    /// </summary>
    /// <param name="path">The relative path to the item</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation</param>
    /// <returns>The sync item if it exists, null otherwise</returns>
    public async Task<SyncItem?> GetItemAsync(string path, CancellationToken cancellationToken = default) {
        if (!await EnsureAuthenticated(cancellationToken).ConfigureAwait(false))
            return null;

        var fullPath = GetFullPath(path);

        return await ExecuteWithRetry(async () => {
            var result = await _client.Propfind(fullPath, new PropfindParameters {
                RequestType = PropfindRequestType.AllProperties,
                CancellationToken = cancellationToken
            }).ConfigureAwait(false);

            if (!result.IsSuccessful) {
                return null;
            }

            var resource = result.Resources.FirstOrDefault();
            if (resource is null) {
                return null;
            }

            return new SyncItem {
                Path = path,
                IsDirectory = resource.IsCollection,
                Size = resource.ContentLength ?? 0,
                LastModified = resource.LastModifiedDate?.ToUniversalTime() ?? DateTime.MinValue,
                ETag = NormalizeETag(resource.ETag)
            };
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Reads the contents of a file from the WebDAV server
    /// </summary>
    /// <param name="path">The relative path to the file</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation</param>
    /// <returns>A stream containing the file contents</returns>
    /// <exception cref="FileNotFoundException">Thrown when the file does not exist</exception>
    /// <exception cref="UnauthorizedAccessException">Thrown when authentication fails</exception>
    public async Task<Stream> ReadFileAsync(string path, CancellationToken cancellationToken = default) {
        if (!await EnsureAuthenticated(cancellationToken).ConfigureAwait(false))
            throw new UnauthorizedAccessException("Authentication failed");

        var fullPath = GetFullPath(path);

        // Get file info first to determine if we need progress reporting
        var item = await GetItemAsync(path, cancellationToken).ConfigureAwait(false);
        var needsProgress = item?.Size > _chunkSize;

        return await ExecuteWithRetry(async () => {
            var response = await _client.GetRawFile(fullPath, new GetFileParameters {
                CancellationToken = cancellationToken
            }).ConfigureAwait(false);

            if (!response.IsSuccessful) {
                if (response.StatusCode == 404) {
                    throw new FileNotFoundException($"File not found: {path}");
                }

                throw new HttpRequestException($"WebDAV request failed: {response.StatusCode}");
            }

            // For large files, wrap stream with progress reporting
            if (needsProgress && item is not null) {
                return new ProgressStream(response.Stream, item.Size,
                    (bytes, total) => RaiseProgressChanged(path, bytes, total, StorageOperation.Download));
            }

            return response.Stream;
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Writes content to a file on the WebDAV server, creating parent directories as needed
    /// </summary>
    /// <param name="path">The relative path to the file</param>
    /// <param name="content">The stream containing the file content to write</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation</param>
    /// <remarks>
    /// For large files (larger than chunk size), uses platform-specific chunked uploads if supported (Nextcloud chunking v2, OCIS TUS).
    /// Progress events are raised during large file uploads.
    /// </remarks>
    public async Task WriteFileAsync(string path, Stream content, CancellationToken cancellationToken = default) {
        if (!await EnsureAuthenticated(cancellationToken).ConfigureAwait(false))
            throw new UnauthorizedAccessException("Authentication failed");

        var fullPath = GetFullPath(path);

        // Ensure root path exists first (if configured)
        if (!string.IsNullOrEmpty(RootPath)) {
            await EnsureRootPathExistsAsync(cancellationToken).ConfigureAwait(false);
        }

        // Ensure parent directories exist
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) {
            await CreateDirectoryAsync(directory, cancellationToken).ConfigureAwait(false);
        }

        // For small files, use regular upload
        if (!content.CanSeek || content.Length <= _chunkSize) {
            // Extract bytes once before retry loop
            content.Position = 0;
            using var tempStream = new MemoryStream();
            await content.CopyToAsync(tempStream, cancellationToken).ConfigureAwait(false);
            var contentBytes = tempStream.ToArray();

            await ExecuteWithRetry(async () => {
                // Create fresh stream for each retry attempt
                using var contentCopy = new MemoryStream(contentBytes);

                var result = await _client.PutFile(fullPath, contentCopy, new PutFileParameters {
                    CancellationToken = cancellationToken
                }).ConfigureAwait(false);

                if (!result.IsSuccessful) {
                    // 409 Conflict on PUT typically means parent directory issue
                    if (result.StatusCode == 409) {
                        _logger.WebDavUploadConflict(path);
                        // Ensure root path and parent directory exist
                        _rootPathCreated = false; // Force re-check
                        if (!string.IsNullOrEmpty(RootPath)) {
                            await EnsureRootPathExistsAsync(cancellationToken).ConfigureAwait(false);
                        }
                        var dir = Path.GetDirectoryName(path);
                        if (!string.IsNullOrEmpty(dir)) {
                            await CreateDirectoryAsync(dir, cancellationToken).ConfigureAwait(false);
                        }
                        // Retry the upload with fresh stream
                        using var retryStream = new MemoryStream(contentBytes);
                        var retryResult = await _client.PutFile(fullPath, retryStream, new PutFileParameters {
                            CancellationToken = cancellationToken
                        }).ConfigureAwait(false);
                        if (retryResult.IsSuccessful) {
                            return true;
                        }
                    }
                    throw new HttpRequestException($"WebDAV upload failed: {result.StatusCode}");
                }

                return true;
            }, cancellationToken).ConfigureAwait(false);

            // Small delay for server propagation, then verify file exists
            await Task.Delay(50, cancellationToken).ConfigureAwait(false);
            return;
        }

        // For large files, use chunked upload (if supported by server)
        await WriteFileChunkedAsync(fullPath, path, content, cancellationToken).ConfigureAwait(false);

        // Small delay for server propagation
        await Task.Delay(50, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Chunked upload implementation with platform-specific optimizations
    /// </summary>
    private async Task WriteFileChunkedAsync(string fullPath, string relativePath, Stream content, CancellationToken cancellationToken) {
        var capabilities = await GetServerCapabilitiesAsync(cancellationToken).ConfigureAwait(false);

        // Use platform-specific chunking if available
        if (capabilities.IsNextcloud && capabilities.ChunkingVersion >= 2) {
            _logger.UploadStrategySelected("Nextcloud chunking v2", relativePath);
            await WriteFileNextcloudChunkedAsync(fullPath, relativePath, content, cancellationToken).ConfigureAwait(false);
        } else if (capabilities.IsOcis && capabilities.SupportsOcisChunking) {
            _logger.UploadStrategySelected("OCIS TUS", relativePath);
            await WriteFileOcisChunkedAsync(fullPath, relativePath, content, cancellationToken).ConfigureAwait(false);
        } else {
            _logger.UploadStrategySelected("generic WebDAV", relativePath);
            await WriteFileGenericAsync(fullPath, relativePath, content, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Generic WebDAV upload with progress reporting
    /// </summary>
    private async Task WriteFileGenericAsync(string fullPath, string relativePath, Stream content, CancellationToken cancellationToken) {
        var totalSize = content.Length;

        await ExecuteWithRetry(async () => {
            // Reset position at start of each retry attempt
            content.Position = 0;

            // Report initial progress
            RaiseProgressChanged(relativePath, 0, totalSize, StorageOperation.Upload);

            var result = await _client.PutFile(fullPath, content, new PutFileParameters {
                CancellationToken = cancellationToken
            }).ConfigureAwait(false);

            if (!result.IsSuccessful) {
                // 409 Conflict on PUT typically means parent directory issue
                if (result.StatusCode == 409) {
                    _logger.WebDavUploadConflict(relativePath);
                    // Ensure root path and parent directory exist
                    _rootPathCreated = false; // Force re-check
                    if (!string.IsNullOrEmpty(RootPath)) {
                        await EnsureRootPathExistsAsync(cancellationToken).ConfigureAwait(false);
                    }
                    var dir = Path.GetDirectoryName(relativePath);
                    if (!string.IsNullOrEmpty(dir)) {
                        await CreateDirectoryAsync(dir, cancellationToken).ConfigureAwait(false);
                    }
                    // Retry the upload
                    content.Position = 0;
                    var retryResult = await _client.PutFile(fullPath, content, new PutFileParameters {
                        CancellationToken = cancellationToken
                    }).ConfigureAwait(false);
                    if (retryResult.IsSuccessful) {
                        RaiseProgressChanged(relativePath, totalSize, totalSize, StorageOperation.Upload);
                        return true;
                    }
                }
                throw new HttpRequestException($"WebDAV upload failed: {result.StatusCode}");
            }

            // Report completion
            RaiseProgressChanged(relativePath, totalSize, totalSize, StorageOperation.Upload);

            return true;
        }, cancellationToken).ConfigureAwait(false);
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
            await CreateDirectoryAsync(chunkFolder, cancellationToken).ConfigureAwait(false);

            // Upload chunks
            var chunkNumber = 0;
            var uploadedBytes = 0L;
            var buffer = new byte[_chunkSize];

            content.Position = 0;

            while (uploadedBytes < totalSize) {
                var bytesRead = await content.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (bytesRead == 0) {
                    break;
                }

                // Upload chunk
                var chunkPath = $"{chunkFolder}/{chunkNumber:D6}";
                using var chunkStream = new MemoryStream(buffer, 0, bytesRead);

                await ExecuteWithRetry(async () => {
                    var result = await _client.PutFile(GetFullPath(chunkPath), chunkStream, new PutFileParameters {
                        CancellationToken = cancellationToken
                    }).ConfigureAwait(false);

                    if (!result.IsSuccessful) {
                        throw new HttpRequestException($"Chunk upload failed: {result.StatusCode}");
                    }

                    return true;
                }, cancellationToken).ConfigureAwait(false);

                uploadedBytes += bytesRead;
                chunkNumber++;

                // Report progress
                RaiseProgressChanged(relativePath, uploadedBytes, totalSize, StorageOperation.Upload);
            }

            // Assemble chunks
            await AssembleNextcloudChunksAsync(chunkFolder, fullPath, totalSize, cancellationToken).ConfigureAwait(false);
        } finally {
            // Clean up chunks folder
            try {
                await DeleteAsync(chunkFolder, cancellationToken).ConfigureAwait(false);
            } catch (Exception ex) {
                _logger.ChunkCleanupFailed(ex, chunkFolder);
            }
        }
    }

    /// <summary>
    /// OCIS-specific chunked upload using TUS protocol
    /// </summary>
    private async Task WriteFileOcisChunkedAsync(string fullPath, string relativePath, Stream content, CancellationToken cancellationToken) {
        try {
            await WriteFileOcisTusAsync(fullPath, relativePath, content, cancellationToken).ConfigureAwait(false);
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            _logger.TusUploadFallback(ex, relativePath);
            // Fallback to generic upload if TUS fails
            if (content.CanSeek) {
                content.Position = 0;
            }
            await WriteFileGenericAsync(fullPath, relativePath, content, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// TUS protocol version used for OCIS uploads
    /// </summary>
    private const string TusProtocolVersion = "1.0.0";

    /// <summary>
    /// Implements TUS 1.0.0 protocol for resumable uploads to OCIS
    /// </summary>
    private async Task WriteFileOcisTusAsync(string fullPath, string relativePath, Stream content, CancellationToken cancellationToken) {
        var totalSize = content.Length;
        content.Position = 0;

        // Report initial progress
        RaiseProgressChanged(relativePath, 0, totalSize, StorageOperation.Upload);

        // Create TUS upload
        var uploadUrl = await TusCreateUploadAsync(fullPath, totalSize, relativePath, cancellationToken).ConfigureAwait(false);

        // Upload chunks
        var offset = 0L;
        var buffer = new byte[_chunkSize];

        while (offset < totalSize) {
            cancellationToken.ThrowIfCancellationRequested();

            var remainingBytes = totalSize - offset;
            var chunkSize = (int)Math.Min(_chunkSize, remainingBytes);

            // Read chunk from content stream
            content.Position = offset;
            var bytesRead = await content.ReadAsync(buffer.AsMemory(0, chunkSize), cancellationToken).ConfigureAwait(false);
            if (bytesRead == 0) {
                break;
            }

            try {
                offset = await TusPatchChunkAsync(uploadUrl, buffer, bytesRead, offset, cancellationToken).ConfigureAwait(false);
            } catch (Exception ex) when (ex is not OperationCanceledException && IsRetriableException(ex)) {
                // Try to resume by checking current offset
                _logger.TusUploadResumeFailed(ex, relativePath, offset);
                var currentOffset = await TusGetOffsetAsync(uploadUrl, cancellationToken).ConfigureAwait(false);
                if (currentOffset >= 0 && currentOffset <= totalSize) {
                    offset = currentOffset;
                    continue;
                }
                throw;
            }

            // Report progress
            RaiseProgressChanged(relativePath, offset, totalSize, StorageOperation.Upload);
        }
    }

    /// <summary>
    /// Creates a TUS upload resource via POST request
    /// </summary>
    /// <returns>The upload URL from the Location header</returns>
    private async Task<string> TusCreateUploadAsync(string fullPath, long totalSize, string relativePath, CancellationToken cancellationToken) {
        using var httpClient = CreateTusHttpClient();

        var request = new HttpRequestMessage(HttpMethod.Post, fullPath);
        request.Headers.Add("Tus-Resumable", TusProtocolVersion);
        request.Headers.Add("Upload-Length", totalSize.ToString());

        // Encode filename in Upload-Metadata header
        var filename = Path.GetFileName(relativePath);
        var encodedMetadata = EncodeTusMetadata(filename);
        request.Headers.Add("Upload-Metadata", encodedMetadata);

        // Empty content for POST
        request.Content = new ByteArrayContent([]);

        var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode) {
            throw new HttpRequestException($"TUS upload creation failed: {(int)response.StatusCode} {response.ReasonPhrase}");
        }

        // Get upload URL from Location header
        var locationHeader = response.Headers.Location;
        if (locationHeader is null) {
            throw new HttpRequestException("TUS server did not return Location header");
        }

        // Location may be absolute or relative
        return locationHeader.IsAbsoluteUri
            ? locationHeader.ToString()
            : new Uri(new Uri(_baseUrl), locationHeader).ToString();
    }

    /// <summary>
    /// Uploads a chunk via TUS PATCH request
    /// </summary>
    /// <returns>The new offset after the chunk was uploaded</returns>
    private async Task<long> TusPatchChunkAsync(string uploadUrl, byte[] buffer, int bytesRead, long currentOffset, CancellationToken cancellationToken) {
        using var httpClient = CreateTusHttpClient();

        var request = new HttpRequestMessage(HttpMethod.Patch, uploadUrl);
        request.Headers.Add("Tus-Resumable", TusProtocolVersion);
        request.Headers.Add("Upload-Offset", currentOffset.ToString());

        // TUS requires application/offset+octet-stream content type
        var content = new ByteArrayContent(buffer, 0, bytesRead);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/offset+octet-stream");
        request.Content = content;

        var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode) {
            throw new HttpRequestException($"TUS chunk upload failed: {(int)response.StatusCode} {response.ReasonPhrase}");
        }

        // Get new offset from Upload-Offset header
        if (response.Headers.TryGetValues("Upload-Offset", out var offsetValues)) {
            var offsetStr = offsetValues.FirstOrDefault();
            if (long.TryParse(offsetStr, out var newOffset)) {
                return newOffset;
            }
        }

        // If server doesn't return offset, calculate it ourselves
        return currentOffset + bytesRead;
    }

    /// <summary>
    /// Gets the current upload offset via TUS HEAD request (for resuming)
    /// </summary>
    /// <returns>The current offset, or -1 if the upload doesn't exist or is invalid</returns>
    private async Task<long> TusGetOffsetAsync(string uploadUrl, CancellationToken cancellationToken) {
        try {
            using var httpClient = CreateTusHttpClient();

            var request = new HttpRequestMessage(HttpMethod.Head, uploadUrl);
            request.Headers.Add("Tus-Resumable", TusProtocolVersion);

            var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode) {
                return -1;
            }

            if (response.Headers.TryGetValues("Upload-Offset", out var offsetValues)) {
                var offsetStr = offsetValues.FirstOrDefault();
                if (long.TryParse(offsetStr, out var offset)) {
                    return offset;
                }
            }

            return -1;
        } catch (Exception ex) {
            _logger.StorageOperationFailed(ex, uploadUrl, "WebDAV");
            return -1;
        }
    }

    /// <summary>
    /// Creates an HttpClient configured for TUS requests with OAuth2 authentication
    /// </summary>
    private HttpClient CreateTusHttpClient() {
        var httpClient = new HttpClient {
            Timeout = _timeout
        };

        // Add OAuth2 bearer token if available
        if (_oauth2Result?.AccessToken is not null) {
            httpClient.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _oauth2Result.AccessToken);
        }

        return httpClient;
    }

    /// <summary>
    /// Encodes filename for TUS Upload-Metadata header (base64 encoded)
    /// </summary>
    internal static string EncodeTusMetadata(string filename) {
        var encodedFilename = Convert.ToBase64String(Encoding.UTF8.GetBytes(filename));
        return $"filename {encodedFilename}";
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
            }).ConfigureAwait(false);

            if (!result.IsSuccessful) {
                throw new HttpRequestException($"Chunk assembly failed: {result.StatusCode}");
            }

            return true;
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Creates a directory on the WebDAV server, including all parent directories if needed
    /// </summary>
    /// <param name="path">The relative path to the directory to create</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation</param>
    public async Task CreateDirectoryAsync(string path, CancellationToken cancellationToken = default) {
        if (!await EnsureAuthenticated(cancellationToken).ConfigureAwait(false))
            throw new UnauthorizedAccessException("Authentication failed");

        // Ensure root path exists first (if configured)
        if (!string.IsNullOrEmpty(RootPath)) {
            await EnsureRootPathExistsAsync(cancellationToken).ConfigureAwait(false);
        }

        // Normalize the path
        path = path.Replace('\\', '/').Trim('/');

        // Handle empty path
        if (string.IsNullOrEmpty(path)) {
            return; // Root directory already exists
        }

        // Create all parent directories first
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var currentPath = "";

        for (int i = 0; i < segments.Length; i++) {
            currentPath = i == 0 ? segments[i] : $"{currentPath}/{segments[i]}";
            var fullPath = GetFullPath(currentPath);
            var pathToCheck = currentPath; // Capture for lambda

            await ExecuteWithRetry(async () => {
                // Check if directory already exists first
                if (await ExistsAsync(pathToCheck, cancellationToken).ConfigureAwait(false)) {
                    return true; // Directory already exists, skip creation
                }

                // Try to create the directory
                var result = await _client.Mkcol(fullPath, new MkColParameters {
                    CancellationToken = cancellationToken
                }).ConfigureAwait(false);

                // Treat 201 (Created), 405 (Already exists), and 409 (Conflict/race condition) as success
                if (result.IsSuccessful || result.StatusCode == 201 || result.StatusCode == 405 || result.StatusCode == 409) {
                    // Verify the directory was actually created (with a short delay for server propagation)
                    await Task.Delay(50, cancellationToken).ConfigureAwait(false);
                    if (await ExistsAsync(pathToCheck, cancellationToken).ConfigureAwait(false)) {
                        return true;
                    }
                    // If it doesn't exist yet, give it more time and try again
                    await Task.Delay(100, cancellationToken).ConfigureAwait(false);
                    return await ExistsAsync(pathToCheck, cancellationToken).ConfigureAwait(false);
                }

                throw new HttpRequestException($"Directory creation failed for {pathToCheck}: {result.StatusCode} {result.Description}");
            }, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Deletes a file or directory from the WebDAV server
    /// </summary>
    /// <param name="path">The relative path to the file or directory to delete</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation</param>
    /// <remarks>
    /// If the path is a directory, it will be deleted recursively along with all its contents
    /// </remarks>
    public async Task DeleteAsync(string path, CancellationToken cancellationToken = default) {
        if (!await EnsureAuthenticated(cancellationToken).ConfigureAwait(false))
            throw new UnauthorizedAccessException("Authentication failed");

        var fullPath = GetFullPath(path);

        await ExecuteWithRetry(async () => {
            var result = await _client.Delete(fullPath, new DeleteParameters {
                CancellationToken = cancellationToken
            }).ConfigureAwait(false);

            if (!result.IsSuccessful && result.StatusCode != 404) // 404 = already deleted
                throw new HttpRequestException($"Delete failed: {result.StatusCode}");

            return true;
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Moves or renames a file or directory on the WebDAV server
    /// </summary>
    /// <param name="sourcePath">The relative path to the source file or directory</param>
    /// <param name="targetPath">The relative path to the target location</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation</param>
    public async Task MoveAsync(string sourcePath, string targetPath, CancellationToken cancellationToken = default) {
        if (!await EnsureAuthenticated(cancellationToken).ConfigureAwait(false))
            throw new UnauthorizedAccessException("Authentication failed");

        var sourceFullPath = GetFullPath(sourcePath);
        var targetFullPath = GetFullPath(targetPath);

        // Ensure target parent directory exists
        var targetDirectory = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrEmpty(targetDirectory)) {
            await CreateDirectoryAsync(targetDirectory, cancellationToken).ConfigureAwait(false);
        }

        await ExecuteWithRetry(async () => {
            var result = await _client.Move(sourceFullPath, targetFullPath, new MoveParameters {
                CancellationToken = cancellationToken
            }).ConfigureAwait(false);

            if (!result.IsSuccessful) {
                throw new HttpRequestException($"Move failed: {result.StatusCode}");
            }

            return true;
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Checks whether a file or directory exists on the WebDAV server
    /// </summary>
    /// <param name="path">The relative path to check</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation</param>
    /// <returns>True if the file or directory exists, false otherwise</returns>
    public async Task<bool> ExistsAsync(string path, CancellationToken cancellationToken = default) {
        if (!await EnsureAuthenticated(cancellationToken).ConfigureAwait(false))
            return false;

        var fullPath = GetFullPath(path);

        try {
            return await ExecuteWithRetry(async () => {
                var result = await _client.Propfind(fullPath, new PropfindParameters {
                    // Use AllProperties for better compatibility with various WebDAV servers
                    RequestType = PropfindRequestType.AllProperties,
                    CancellationToken = cancellationToken
                }).ConfigureAwait(false);

                // Check if the request was successful and we got at least one resource
                if (!result.IsSuccessful || result.StatusCode == 404) {
                    return false;
                }

                // Ensure we actually have resources in the response
                return result.Resources.Count > 0;
            }, cancellationToken).ConfigureAwait(false);
        } catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound) {
            return false;
        } catch (Exception ex) {
            _logger.StorageOperationFailed(ex, path, "WebDAV");
            return false;
        }
    }

    /// <summary>
    /// Gets storage space information from the WebDAV server
    /// </summary>
    /// <param name="cancellationToken">Cancellation token to cancel the operation</param>
    /// <returns>Storage information including total and used space, or -1 if not supported</returns>
    public async Task<StorageInfo> GetStorageInfoAsync(CancellationToken cancellationToken = default) {
        if (!await EnsureAuthenticated(cancellationToken).ConfigureAwait(false))
            return new StorageInfo { TotalSpace = -1, UsedSpace = -1 };

        return await ExecuteWithRetry(async () => {
            // Try to get quota information from the root
            var result = await _client.Propfind(_baseUrl, new PropfindParameters {
                CancellationToken = cancellationToken
            }).ConfigureAwait(false);

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
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Computes a hash for a file on the WebDAV server
    /// </summary>
    /// <param name="path">The relative path to the file</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation</param>
    /// <returns>SHA256 hash of the file contents (content-based, not ETag)</returns>
    /// <remarks>
    /// This method always computes a content-based hash (SHA256) to ensure consistent
    /// hash values for files with identical content. For Nextcloud/OCIS servers,
    /// it first tries to use server-side checksums to avoid downloading the file.
    /// ETags are not used as the WebDAV/HTTP spec does not guarantee them to be content-based;
    /// they are opaque per-resource version identifiers that typically incorporate path, inode,
    /// or internal file ID, so identical content at different paths produces different ETags.
    /// </remarks>
    public async Task<string> ComputeHashAsync(string path, CancellationToken cancellationToken = default) {
        // For Nextcloud/OCIS, try to get content-based checksum from properties
        var capabilities = await GetServerCapabilitiesAsync(cancellationToken).ConfigureAwait(false);
        if (capabilities.IsNextcloud || capabilities.IsOcis) {
            var checksum = await GetServerChecksumAsync(path, cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(checksum))
                return checksum;
        }

        // Compute SHA256 hash from file content (content-based, same for identical files)
        using var stream = await ReadFileAsync(path, cancellationToken).ConfigureAwait(false);
        using var sha256 = SHA256.Create();

        var hashBytes = await sha256.ComputeHashAsync(stream, cancellationToken).ConfigureAwait(false);
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
            }).ConfigureAwait(false);

            if (!result.IsSuccessful || result.Resources.Count == 0) {
                return null;
            }

            var resource = result.Resources.First();

            // Look for checksum properties (Nextcloud uses oc:checksum)
            var checksumProp = resource.Properties?.FirstOrDefault(p =>
                p.Name.LocalName == "checksum" &&
                (p.Name.NamespaceName.Contains("owncloud") || p.Name.NamespaceName.Contains("nextcloud")));

            if (checksumProp is not null && !string.IsNullOrEmpty(checksumProp.Value)) {
                // Format is usually "SHA1:hash" or "MD5:hash"
                var parts = checksumProp.Value.Split(':');
                if (parts.Length == 2) {
                    return parts[1];
                }
            }

            return null;
        } catch (Exception ex) {
            _logger.ServerChecksumUnavailable(ex, path);
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
            var serverBase = GetServerBaseUrl(_baseUrl);
            var statusUrl = $"{serverBase}/status.php";

            using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

            // Add auth header if using OAuth2
            if (_oauth2Result?.AccessToken is not null) {
                httpClient.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _oauth2Result.AccessToken);
            }

            try {
                var response = await httpClient.GetAsync(statusUrl, cancellationToken).ConfigureAwait(false);
                if (response.IsSuccessStatusCode) {
                    var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
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
            } catch (Exception ex) {
                _logger.ServerCapabilityDetectionFailed(ex, statusUrl);
            }

            // Check for capabilities endpoint (Nextcloud/OCIS)
            if (capabilities.IsNextcloud || capabilities.IsOcis) {
                var capabilitiesUrl = $"{serverBase}/ocs/v1.php/cloud/capabilities";

                try {
                    var response = await httpClient.GetAsync(capabilitiesUrl, cancellationToken).ConfigureAwait(false);
                    if (response.IsSuccessStatusCode) {
                        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
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
                } catch (Exception ex) {
                    _logger.ServerCapabilityDetectionFailed(ex, _baseUrl);
                }
            }
        } catch (Exception ex) {
            _logger.ServerCapabilityDetectionFailed(ex, _baseUrl);
        }

        _logger.ServerCapabilitiesDetected(capabilities.IsNextcloud, capabilities.IsOcis, capabilities.SupportsChunking);
        return capabilities;
    }

    /// <summary>
    /// Extracts the server base URL (scheme + authority + any prefix path) by stripping
    /// the WebDAV path component. Handles Nextcloud (<c>/remote.php/dav</c>),
    /// and OCIS native paths (<c>/dav/</c>) as well as subdirectory installations.
    /// </summary>
    /// <remarks>
    /// OCIS is written in Go but provides <c>/remote.php/</c> and <c>.php</c> endpoints
    /// for backward compatibility with existing Nextcloud clients.
    /// </remarks>
    internal static string GetServerBaseUrl(string baseUrl) {
        var uri = new Uri(baseUrl);
        var path = uri.AbsolutePath;

        // Find the WebDAV path component and strip it along with everything after it.
        // Order matters: check more specific patterns first so that
        // "/remote.php/webdav" is not partially matched by "/dav/".
        string[] markers = ["/remote.php/dav", "/remote.php/webdav", "/dav/"];

        foreach (var marker in markers) {
            var idx = path.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0) {
                var basePath = path[..idx];
                return $"{uri.Scheme}://{uri.Authority}{basePath}";
            }
        }

        // Fallback: just use scheme + authority
        return $"{uri.Scheme}://{uri.Authority}";
    }

    private string GetFullPath(string relativePath) {
        if (string.IsNullOrEmpty(relativePath) || relativePath == "/") {
            var basePath = string.IsNullOrEmpty(RootPath) ? _baseUrl : $"{_baseUrl.TrimEnd('/')}/{RootPath.Trim('/')}";
            return basePath.TrimEnd('/') + "/";
        }

        relativePath = relativePath.Trim('/');

        if (string.IsNullOrEmpty(RootPath)) {
            return $"{_baseUrl.TrimEnd('/')}/{relativePath}";
        } else {
            return $"{_baseUrl.TrimEnd('/')}/{RootPath.Trim('/')}/{relativePath}";
        }
    }

    private string GetRelativePath(string fullUrl) {
        // The fullUrl can be either a full URL (http://server/path) or just a path (/path)
        // We need to strip the base URL and RootPath to get the relative path

        // Extract the path portion if it's a full URL
        string path;
        if (Uri.TryCreate(fullUrl, UriKind.Absolute, out var uri)) {
            // It's a full URL - get the path component and decode it
            path = Uri.UnescapeDataString(uri.AbsolutePath);
        } else {
            // It's already a path
            path = fullUrl;
        }

        // Remove leading slash for consistency
        path = path.TrimStart('/');

        // If there's no root path, return the path as-is (trimming trailing slashes)
        if (string.IsNullOrEmpty(RootPath)) {
            return path.TrimEnd('/');
        }

        // Normalize the root path (no leading/trailing slashes)
        var normalizedRoot = RootPath.Trim('/');

        // The path should start with RootPath/
        if (path.StartsWith($"{normalizedRoot}/")) {
            return path.Substring(normalizedRoot.Length + 1).TrimEnd('/');
        }

        // If it's exactly the root path itself (directory listing)
        if (path == normalizedRoot || path == $"{normalizedRoot}/") {
            return "";
        }

        // Otherwise return as-is (trim trailing slashes)
        return path.TrimEnd('/');
    }

    private bool _rootPathCreated;

    private async Task EnsureRootPathExistsAsync(CancellationToken cancellationToken) {
        if (_rootPathCreated || string.IsNullOrEmpty(RootPath)) {
            return;
        }

        var rootUrl = $"{_baseUrl.TrimEnd('/')}/{RootPath.Trim('/')}";

        await ExecuteWithRetry(async () => {
            var result = await _client.Mkcol(rootUrl, new MkColParameters {
                CancellationToken = cancellationToken
            }).ConfigureAwait(false);

            // Treat 201 (Created), 405 (Already exists), and 409 (Conflict) as success
            if (result.IsSuccessful || result.StatusCode == 201 || result.StatusCode == 405 || result.StatusCode == 409) {
                _rootPathCreated = true;
                return true;
            }

            throw new HttpRequestException($"Failed to create root path: {result.StatusCode} {result.Description}");
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> EnsureAuthenticated(CancellationToken cancellationToken) {
        if (_oauth2Provider is not null) {
            return await AuthenticateAsync(cancellationToken).ConfigureAwait(false);
        }
        return true;
    }

    private async Task<T> ExecuteWithRetry<T>(Func<Task<T>> operation, CancellationToken cancellationToken) {
        Exception? lastException = null;

        for (int attempt = 0; attempt <= _maxRetries; attempt++) {
            try {
                cancellationToken.ThrowIfCancellationRequested();
                return await operation().ConfigureAwait(false);
            } catch (Exception ex) when (attempt < _maxRetries && IsRetriableException(ex)) {
                lastException = ex;
                _logger.StorageOperationRetry("WebDAV", attempt + 1, _maxRetries);
                // Exponential backoff: delay * 2^attempt (e.g., 1s, 2s, 4s, 8s...)
                var delay = _retryDelay * (1 << attempt);
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
        }

        throw lastException ?? new InvalidOperationException("Operation failed after retries");
    }

    internal static bool IsRetriableException(Exception ex) {
        if (ex is HttpRequestException httpEx) {
            // No status code means the request never got a response (DNS failure, connection refused, etc.)
            if (httpEx.StatusCode is null) {
                return true;
            }

            var statusCode = (int)httpEx.StatusCode;
            return statusCode >= 500 || statusCode == 408; // Server errors or Request Timeout
        }

        // Transient network and I/O failures are always worth retrying
        if (ex is TaskCanceledException or SocketException or IOException or TimeoutException) {
            return true;
        }

        // If the exception wraps another, check the inner exception
        if (ex.InnerException is not null) {
            return IsRetriableException(ex.InnerException);
        }

        return false;
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

    #region Remote Change Detection

    /// <summary>
    /// Gets remote changes detected since the specified time using the OCS activity API.
    /// </summary>
    /// <remarks>
    /// This method returns results when connected to a Nextcloud or OCIS server.
    /// It queries the OCS activity API v2 to discover file changes without a full PROPFIND scan.
    /// For generic WebDAV servers, returns an empty list (falls back to base default).
    /// </remarks>
    /// <param name="since">Only return changes detected after this time (UTC)</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation</param>
    /// <returns>A collection of remote changes detected since the specified time</returns>
    public async Task<IReadOnlyList<ChangeInfo>> GetRemoteChangesAsync(DateTime since, CancellationToken cancellationToken = default) {
        var capabilities = await GetServerCapabilitiesAsync(cancellationToken).ConfigureAwait(false);
        if (!capabilities.IsNextcloud && !capabilities.IsOcis) {
            return [];
        }

        var changes = new List<ChangeInfo>();

        try {
            var serverBase = GetServerBaseUrl(_baseUrl);
            var sinceTimestamp = new DateTimeOffset(since.ToUniversalTime()).ToUnixTimeSeconds();
            var activityUrl = $"{serverBase}/ocs/v2.php/apps/activity/api/v2/activity/filter?format=json&object_type=files&since={sinceTimestamp}";

            using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

            // Add auth header if using OAuth2
            if (_oauth2Result?.AccessToken is not null) {
                httpClient.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _oauth2Result.AccessToken);
            }

            // OCS API requires this header
            httpClient.DefaultRequestHeaders.Add("OCS-APIRequest", "true");

            var response = await httpClient.GetAsync(activityUrl, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) {
                return [];
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("ocs", out var ocs) ||
                !ocs.TryGetProperty("data", out var data) ||
                data.ValueKind != JsonValueKind.Array) {
                return [];
            }

            foreach (var activity in data.EnumerateArray()) {
                cancellationToken.ThrowIfCancellationRequested();

                if (!activity.TryGetProperty("type", out var typeProp)) {
                    continue;
                }

                var type = typeProp.GetString() ?? "";

                // Map Nextcloud activity types to ChangeType
                ChangeType? changeType = type switch {
                    "file_created" => ChangeType.Created,
                    "file_changed" => ChangeType.Changed,
                    "file_deleted" => ChangeType.Deleted,
                    "file_restored" => ChangeType.Created,
                    _ => null
                };

                if (changeType is null) {
                    continue;
                }

                // Extract the file path from the activity
                string? filePath = null;
                if (activity.TryGetProperty("object_name", out var objectName)) {
                    filePath = objectName.GetString();
                }

                if (string.IsNullOrEmpty(filePath)) {
                    continue;
                }

                // Parse the activity timestamp
                var detectedAt = DateTime.UtcNow;
                if (activity.TryGetProperty("datetime", out var datetimeProp)) {
                    if (DateTime.TryParse(datetimeProp.GetString(), out var parsed)) {
                        detectedAt = parsed.ToUniversalTime();
                    }
                }

                // Only include changes after 'since'
                if (detectedAt <= since) {
                    continue;
                }

                changes.Add(new ChangeInfo(
                    Path: filePath,
                    ChangeType: changeType.Value) {
                    DetectedAt = detectedAt
                });
            }
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            _logger.StorageOperationFailed(ex, "GetRemoteChangesAsync", "WebDAV");
        }

        return changes;
    }

    #endregion

    #region IDisposable

    /// <summary>
    /// Releases all resources used by the WebDAV storage
    /// </summary>
    /// <remarks>
    /// Disposes of the WebDAV client and authentication semaphores.
    /// This method can be called multiple times safely. After disposal, the storage instance cannot be reused.
    /// </remarks>
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
