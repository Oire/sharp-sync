using System.Security.Cryptography;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Transfer;
using Oire.SharpSync.Core;

namespace Oire.SharpSync.Storage;

/// <summary>
/// Amazon S3 and S3-compatible storage implementation
/// Provides file synchronization over S3 protocol with support for AWS S3, MinIO, LocalStack, and other S3-compatible services
/// </summary>
public class S3Storage: ISyncStorage, IDisposable {
    private readonly AmazonS3Client _client;
    private readonly string _bucketName;
    private readonly string _prefix;

    // Configuration
    private readonly int _chunkSize;
    private readonly int _maxRetries;
    private readonly TimeSpan _retryDelay;

    private readonly SemaphoreSlim _transferSemaphore;
    private bool _disposed;

    /// <summary>
    /// Gets the storage type (always returns <see cref="Core.StorageType.S3"/>)
    /// </summary>
    public StorageType StorageType => StorageType.S3;

    /// <summary>
    /// Gets the root path (prefix) within the S3 bucket
    /// </summary>
    public string RootPath { get; }

    /// <summary>
    /// Creates S3 storage with AWS credentials
    /// </summary>
    /// <param name="bucketName">S3 bucket name</param>
    /// <param name="accessKey">AWS access key ID</param>
    /// <param name="secretKey">AWS secret access key</param>
    /// <param name="region">AWS region (e.g., "us-east-1")</param>
    /// <param name="prefix">Prefix (folder path) within the bucket</param>
    /// <param name="sessionToken">Optional AWS session token for temporary credentials</param>
    /// <param name="chunkSizeBytes">Chunk size for multipart uploads (default 10MB)</param>
    /// <param name="maxRetries">Maximum retry attempts (default 3)</param>
    public S3Storage(
        string bucketName,
        string accessKey,
        string secretKey,
        string region = "us-east-1",
        string prefix = "",
        string? sessionToken = null,
        int chunkSizeBytes = 10 * 1024 * 1024,
        int maxRetries = 3) {
        if (string.IsNullOrWhiteSpace(bucketName)) {
            throw new ArgumentException("Bucket name cannot be empty", nameof(bucketName));
        }

        if (string.IsNullOrWhiteSpace(accessKey)) {
            throw new ArgumentException("Access key cannot be empty", nameof(accessKey));
        }

        if (string.IsNullOrWhiteSpace(secretKey)) {
            throw new ArgumentException("Secret key cannot be empty", nameof(secretKey));
        }

        _bucketName = bucketName;
        _prefix = NormalizePath(prefix);
        RootPath = _prefix;

        _chunkSize = chunkSizeBytes;
        _maxRetries = maxRetries;
        _retryDelay = TimeSpan.FromSeconds(1);

        // Create AWS credentials
        AWSCredentials credentials = string.IsNullOrEmpty(sessionToken)
            ? new BasicAWSCredentials(accessKey, secretKey)
            : new SessionAWSCredentials(accessKey, secretKey, sessionToken);

        // Create S3 client configuration
        var config = new AmazonS3Config {
            RegionEndpoint = Amazon.RegionEndpoint.GetBySystemName(region),
            Timeout = TimeSpan.FromSeconds(300),
            MaxErrorRetry = maxRetries
        };

        _client = new AmazonS3Client(credentials, config);
        _transferSemaphore = new SemaphoreSlim(10, 10); // Allow up to 10 concurrent transfers
    }

    /// <summary>
    /// Creates S3 storage with custom endpoint (for S3-compatible services like MinIO, LocalStack)
    /// </summary>
    /// <param name="bucketName">S3 bucket name</param>
    /// <param name="accessKey">Access key ID</param>
    /// <param name="secretKey">Secret access key</param>
    /// <param name="serviceUrl">Service endpoint URL (e.g., "http://localhost:9000" for MinIO)</param>
    /// <param name="prefix">Prefix (folder path) within the bucket</param>
    /// <param name="forcePathStyle">Force path-style URLs (required for MinIO and some S3-compatible services)</param>
    /// <param name="chunkSizeBytes">Chunk size for multipart uploads (default 10MB)</param>
    /// <param name="maxRetries">Maximum retry attempts (default 3)</param>
    public S3Storage(
        string bucketName,
        string accessKey,
        string secretKey,
        Uri serviceUrl,
        string prefix = "",
        bool forcePathStyle = true,
        int chunkSizeBytes = 10 * 1024 * 1024,
        int maxRetries = 3) {
        if (string.IsNullOrWhiteSpace(bucketName)) {
            throw new ArgumentException("Bucket name cannot be empty", nameof(bucketName));
        }

        if (string.IsNullOrWhiteSpace(accessKey)) {
            throw new ArgumentException("Access key cannot be empty", nameof(accessKey));
        }

        if (string.IsNullOrWhiteSpace(secretKey)) {
            throw new ArgumentException("Secret key cannot be empty", nameof(secretKey));
        }

        ArgumentNullException.ThrowIfNull(serviceUrl);

        _bucketName = bucketName;
        _prefix = NormalizePath(prefix);
        RootPath = _prefix;

        _chunkSize = chunkSizeBytes;
        _maxRetries = maxRetries;
        _retryDelay = TimeSpan.FromSeconds(1);

        // Create AWS credentials
        var credentials = new BasicAWSCredentials(accessKey, secretKey);

        // Create S3 client configuration for custom endpoint
        var config = new AmazonS3Config {
            ServiceURL = serviceUrl.ToString(),
            ForcePathStyle = forcePathStyle,
            Timeout = TimeSpan.FromSeconds(300),
            MaxErrorRetry = maxRetries
        };

        _client = new AmazonS3Client(credentials, config);
        _transferSemaphore = new SemaphoreSlim(10, 10);
    }

    /// <summary>
    /// Event raised when upload/download progress changes
    /// </summary>
    public event EventHandler<StorageProgressEventArgs>? ProgressChanged;

    /// <summary>
    /// Tests the connection to the S3 service
    /// </summary>
    /// <param name="cancellationToken">Cancellation token to cancel the operation</param>
    /// <returns>True if connection is successful and bucket is accessible, false otherwise</returns>
    public async Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default) {
        try {
            // Try to list objects with max keys 1 to verify bucket access
            var request = new ListObjectsV2Request {
                BucketName = _bucketName,
                MaxKeys = 1,
                Prefix = _prefix
            };

            await _client.ListObjectsV2Async(request, cancellationToken);
            return true;
        } catch {
            return false;
        }
    }

    /// <summary>
    /// Lists all items (objects and "directories") in the specified path
    /// </summary>
    /// <param name="path">The relative path to list items from</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation</param>
    /// <returns>A collection of sync items representing objects and directories</returns>
    /// <exception cref="UnauthorizedAccessException">Thrown when authentication fails</exception>
    public async Task<IEnumerable<SyncItem>> ListItemsAsync(string path, CancellationToken cancellationToken = default) {
        var items = new List<SyncItem>();
        var directories = new HashSet<string>();

        return await ExecuteWithRetry(async () => {
            // Build the S3 prefix with trailing slash for directory listing
            string listPrefix;
            if (string.IsNullOrEmpty(path)) {
                // Listing root - use prefix with trailing slash if prefix exists
                listPrefix = string.IsNullOrEmpty(_prefix) ? "" : _prefix + "/";
            } else {
                // Listing subdirectory - ensure trailing slash
                var fullPath = GetFullPath(path);
                listPrefix = fullPath.EndsWith('/') ? fullPath : fullPath + "/";
            }

            var request = new ListObjectsV2Request {
                BucketName = _bucketName,
                Prefix = listPrefix,
                Delimiter = "/" // Use delimiter to get directory-like structure
            };

            ListObjectsV2Response? response;
            do {
                response = await _client.ListObjectsV2Async(request, cancellationToken);

                // Add files (objects)
                foreach (var s3Object in response.S3Objects) {
                    cancellationToken.ThrowIfCancellationRequested();

                    // Skip directory marker objects (keys ending with '/')
                    if (s3Object.Key.EndsWith('/')) {
                        continue;
                    }

                    var relativePath = GetRelativePath(s3Object.Key);

                    items.Add(new SyncItem {
                        Path = relativePath,
                        IsDirectory = false,
                        Size = s3Object.Size ?? 0,
                        LastModified = s3Object.LastModified?.ToUniversalTime() ?? DateTime.UtcNow,
                        ETag = s3Object.ETag?.Trim('"'),
                        MimeType = GetMimeType(s3Object.Key),
                        Metadata = new Dictionary<string, object> {
                            ["StorageClass"] = s3Object.StorageClass?.Value ?? "STANDARD"
                        }
                    });
                }

                // Add directories (common prefixes)
                foreach (var commonPrefix in response.CommonPrefixes) {
                    cancellationToken.ThrowIfCancellationRequested();

                    // CommonPrefix includes trailing slash, remove it for relative path
                    var relativePath = GetRelativePath(commonPrefix.TrimEnd('/'));

                    // Avoid duplicates using HashSet.Add's return value (CA1868)
                    if (directories.Add(relativePath)) {
                        items.Add(new SyncItem {
                            Path = relativePath,
                            IsDirectory = true,
                            Size = 0,
                            LastModified = DateTime.UtcNow, // S3 doesn't track directory timestamps
                            MimeType = null
                        });
                    }
                }

                request.ContinuationToken = response.NextContinuationToken;
            } while (response is not null && response.IsTruncated.GetValueOrDefault());

            return (IEnumerable<SyncItem>)items;
        }, cancellationToken);
    }

    /// <summary>
    /// Gets metadata for a specific item (object or directory)
    /// </summary>
    /// <param name="path">The relative path to the item</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation</param>
    /// <returns>The sync item if it exists, null otherwise</returns>
    public async Task<SyncItem?> GetItemAsync(string path, CancellationToken cancellationToken = default) {
        var fullPath = GetFullPath(path);

        return await ExecuteWithRetry(async () => {
            try {
                // Try to get object metadata
                var request = new GetObjectMetadataRequest {
                    BucketName = _bucketName,
                    Key = fullPath
                };

                var response = await _client.GetObjectMetadataAsync(request, cancellationToken);

                return new SyncItem {
                    Path = path,
                    IsDirectory = false,
                    Size = response.ContentLength,
                    LastModified = response.LastModified?.ToUniversalTime() ?? DateTime.UtcNow,
                    ETag = response.ETag?.Trim('"'),
                    MimeType = response.Headers.ContentType,
                    Metadata = new Dictionary<string, object> {
                        ["StorageClass"] = response.StorageClass?.Value ?? "STANDARD"
                    }
                };
            } catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound) {
                // Object doesn't exist, check if it's a directory (prefix)
                var listRequest = new ListObjectsV2Request {
                    BucketName = _bucketName,
                    Prefix = fullPath.EndsWith('/') ? fullPath : fullPath + "/",
                    MaxKeys = 1
                };

                var listResponse = await _client.ListObjectsV2Async(listRequest, cancellationToken);

                if (listResponse.S3Objects.Count > 0 || listResponse.CommonPrefixes.Count > 0) {
                    return new SyncItem {
                        Path = path,
                        IsDirectory = true,
                        Size = 0,
                        LastModified = DateTime.UtcNow,
                        MimeType = null
                    };
                }

                return null;
            }
        }, cancellationToken);
    }

    /// <summary>
    /// Reads the contents of an object from S3
    /// </summary>
    /// <param name="path">The relative path to the object</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation</param>
    /// <returns>A stream containing the object contents</returns>
    /// <exception cref="FileNotFoundException">Thrown when the object does not exist</exception>
    /// <exception cref="InvalidOperationException">Thrown when attempting to read a directory as a file</exception>
    /// <exception cref="UnauthorizedAccessException">Thrown when authentication fails</exception>
    public async Task<Stream> ReadFileAsync(string path, CancellationToken cancellationToken = default) {
        var fullPath = GetFullPath(path);

        return await ExecuteWithRetry(async () => {
            try {
                var request = new GetObjectRequest {
                    BucketName = _bucketName,
                    Key = fullPath
                };

                var response = await _client.GetObjectAsync(request, cancellationToken);

                // Read the entire stream into memory
                var memoryStream = new MemoryStream();
                var totalBytes = response.ContentLength;
                var bytesRead = 0L;

                var buffer = new byte[_chunkSize];
                int read;

                while ((read = await response.ResponseStream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0) {
                    await memoryStream.WriteAsync(new ReadOnlyMemory<byte>(buffer, 0, read), cancellationToken);
                    bytesRead += read;

                    if (totalBytes > _chunkSize) {
                        RaiseProgressChanged(path, bytesRead, totalBytes, StorageOperation.Download);
                    }
                }

                memoryStream.Position = 0;
                return (Stream)memoryStream;
            } catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound) {
                throw new FileNotFoundException($"File not found: {path}");
            }
        }, cancellationToken);
    }

    /// <summary>
    /// Writes content to an object in S3, creating parent "directories" as needed
    /// </summary>
    /// <param name="path">The relative path to the object</param>
    /// <param name="content">The stream containing the object content to write</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation</param>
    /// <exception cref="UnauthorizedAccessException">Thrown when authentication fails</exception>
    /// <remarks>
    /// If the object already exists, it will be overwritten. For large files,
    /// multipart upload is used automatically with progress reporting
    /// </remarks>
    public async Task WriteFileAsync(string path, Stream content, CancellationToken cancellationToken = default) {
        var fullPath = GetFullPath(path);

        await _transferSemaphore.WaitAsync(cancellationToken);
        try {
            await ExecuteWithRetry(async () => {
                var fileSize = content.CanSeek ? content.Length : -1;

                if (fileSize >= 0 && fileSize > _chunkSize) {
                    // Use multipart upload for large files
                    using var transferUtility = new TransferUtility(_client);

                    var uploadRequest = new TransferUtilityUploadRequest {
                        BucketName = _bucketName,
                        Key = fullPath,
                        InputStream = content,
                        PartSize = _chunkSize,
                        AutoCloseStream = false
                    };

                    // Track progress
                    uploadRequest.UploadProgressEvent += (sender, args) => {
                        RaiseProgressChanged(path, args.TransferredBytes, args.TotalBytes, StorageOperation.Upload);
                    };

                    await transferUtility.UploadAsync(uploadRequest, cancellationToken);
                } else {
                    // Use simple put for small files
                    var putRequest = new PutObjectRequest {
                        BucketName = _bucketName,
                        Key = fullPath,
                        InputStream = content,
                        AutoCloseStream = false
                    };

                    await _client.PutObjectAsync(putRequest, cancellationToken);

                    if (fileSize > 0) {
                        RaiseProgressChanged(path, fileSize, fileSize, StorageOperation.Upload);
                    }
                }

                return true;
            }, cancellationToken);
        } finally {
            _transferSemaphore.Release();
        }
    }

    /// <summary>
    /// Creates a "directory" in S3 by creating a marker object
    /// </summary>
    /// <param name="path">The relative path to the directory to create</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation</param>
    /// <remarks>
    /// S3 doesn't have real directories, but we create a zero-byte object with a trailing slash
    /// to simulate directory structure. This is optional in S3 as paths are just key prefixes
    /// </remarks>
    public async Task CreateDirectoryAsync(string path, CancellationToken cancellationToken = default) {
        // In S3, directories are virtual - they don't need to be explicitly created
        // However, we can create a marker object if needed
        var fullPath = GetFullPath(path);

        // Skip if path is empty or root
        if (string.IsNullOrEmpty(fullPath)) {
            return;
        }

        await ExecuteWithRetry(async () => {
            // Ensure path ends with /
            var directoryKey = fullPath.EndsWith('/') ? fullPath : fullPath + "/";

            // Check if marker already exists
            try {
                var headRequest = new GetObjectMetadataRequest {
                    BucketName = _bucketName,
                    Key = directoryKey
                };

                await _client.GetObjectMetadataAsync(headRequest, cancellationToken);
                return true; // Marker already exists
            } catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound) {
                // Marker doesn't exist, create it
                var putRequest = new PutObjectRequest {
                    BucketName = _bucketName,
                    Key = directoryKey,
                    InputStream = new MemoryStream(Array.Empty<byte>()),
                    ContentType = "application/x-directory"
                };

                await _client.PutObjectAsync(putRequest, cancellationToken);
                return true;
            }
        }, cancellationToken);
    }

    /// <summary>
    /// Deletes an object or "directory" from S3
    /// </summary>
    /// <param name="path">The relative path to the object or directory to delete</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation</param>
    /// <remarks>
    /// If the path is a directory, all objects under that prefix will be deleted recursively
    /// </remarks>
    public async Task DeleteAsync(string path, CancellationToken cancellationToken = default) {
        var fullPath = GetFullPath(path);

        await ExecuteWithRetry(async () => {
            // First, try to get the item to determine if it's a file or directory
            var item = await GetItemAsync(path, cancellationToken);

            if (item == null) {
                return true; // Already deleted
            }

            if (item.IsDirectory) {
                // Delete all objects with this prefix
                await DeleteDirectoryRecursive(fullPath, cancellationToken);
            } else {
                // Delete single object
                var deleteRequest = new DeleteObjectRequest {
                    BucketName = _bucketName,
                    Key = fullPath
                };

                await _client.DeleteObjectAsync(deleteRequest, cancellationToken);
            }

            return true;
        }, cancellationToken);
    }

    /// <summary>
    /// Recursively deletes all objects under a prefix
    /// </summary>
    private async Task DeleteDirectoryRecursive(string prefix, CancellationToken cancellationToken) {
        var directoryPrefix = prefix.EndsWith('/') ? prefix : prefix + "/";

        var listRequest = new ListObjectsV2Request {
            BucketName = _bucketName,
            Prefix = directoryPrefix
        };

        ListObjectsV2Response? response;
        do {
            response = await _client.ListObjectsV2Async(listRequest, cancellationToken);

            if (response.S3Objects.Count > 0) {
                // Delete objects in batches (S3 allows up to 1000 objects per request)
                var deleteRequest = new DeleteObjectsRequest {
                    BucketName = _bucketName,
                    Objects = response.S3Objects.Select(obj => new KeyVersion { Key = obj.Key }).ToList()
                };

                await _client.DeleteObjectsAsync(deleteRequest, cancellationToken);
            }

            listRequest.ContinuationToken = response.NextContinuationToken;
        } while (response is not null && response.IsTruncated.GetValueOrDefault());

        // Also delete the directory marker if it exists
        try {
            var deleteMarkerRequest = new DeleteObjectRequest {
                BucketName = _bucketName,
                Key = directoryPrefix
            };

            await _client.DeleteObjectAsync(deleteMarkerRequest, cancellationToken);
        } catch (AmazonS3Exception) {
            // Ignore if marker doesn't exist
        }
    }

    /// <summary>
    /// Moves or renames an object in S3
    /// </summary>
    /// <param name="sourcePath">The relative path to the source object</param>
    /// <param name="targetPath">The relative path to the target location</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation</param>
    /// <exception cref="FileNotFoundException">Thrown when the source does not exist</exception>
    /// <remarks>
    /// S3 doesn't have a native move operation, so this copies the object and deletes the source
    /// </remarks>
    public async Task MoveAsync(string sourcePath, string targetPath, CancellationToken cancellationToken = default) {
        var sourceFullPath = GetFullPath(sourcePath);
        var targetFullPath = GetFullPath(targetPath);

        await ExecuteWithRetry(async () => {
            // Check if source exists
            try {
                var headRequest = new GetObjectMetadataRequest {
                    BucketName = _bucketName,
                    Key = sourceFullPath
                };

                await _client.GetObjectMetadataAsync(headRequest, cancellationToken);
            } catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound) {
                throw new FileNotFoundException($"Source not found: {sourcePath}");
            }

            // Copy object to new location
            var copyRequest = new CopyObjectRequest {
                SourceBucket = _bucketName,
                SourceKey = sourceFullPath,
                DestinationBucket = _bucketName,
                DestinationKey = targetFullPath
            };

            await _client.CopyObjectAsync(copyRequest, cancellationToken);

            // Delete source object
            var deleteRequest = new DeleteObjectRequest {
                BucketName = _bucketName,
                Key = sourceFullPath
            };

            await _client.DeleteObjectAsync(deleteRequest, cancellationToken);

            return true;
        }, cancellationToken);
    }

    /// <summary>
    /// Checks whether an object or directory exists in S3
    /// </summary>
    /// <param name="path">The relative path to check</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation</param>
    /// <returns>True if the object or directory exists, false otherwise</returns>
    public async Task<bool> ExistsAsync(string path, CancellationToken cancellationToken = default) {
        var item = await GetItemAsync(path, cancellationToken);
        return item != null;
    }

    /// <summary>
    /// Gets storage space information for the S3 bucket
    /// </summary>
    /// <param name="cancellationToken">Cancellation token to cancel the operation</param>
    /// <returns>Storage information (returns -1 for unknown values as S3 doesn't provide quota information via API)</returns>
    /// <remarks>
    /// S3 doesn't provide bucket size or quota information through standard APIs.
    /// To get accurate bucket size, you would need to sum all object sizes, which is expensive.
    /// This method returns -1 for both total and used space
    /// </remarks>
    public async Task<StorageInfo> GetStorageInfoAsync(CancellationToken cancellationToken = default) {
        // S3 doesn't provide bucket-level quota/size information
        // We could calculate it by listing all objects, but that's expensive
        // Return unknown values
        return await Task.FromResult(new StorageInfo {
            TotalSpace = -1,
            UsedSpace = -1
        });
    }

    /// <summary>
    /// Computes the SHA256 hash of an object in S3
    /// </summary>
    /// <param name="path">The relative path to the object</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation</param>
    /// <returns>Base64-encoded SHA256 hash of the object contents</returns>
    /// <exception cref="FileNotFoundException">Thrown when the object does not exist</exception>
    /// <remarks>
    /// S3 ETags are MD5 hashes for simple uploads, but for multipart uploads they use a different algorithm.
    /// This method downloads the object and computes SHA256 locally for consistency with other storage implementations
    /// </remarks>
    public async Task<string> ComputeHashAsync(string path, CancellationToken cancellationToken = default) {
        // S3 ETag is MD5-based and complex for multipart uploads
        // Download and compute SHA256 for consistency
        using var stream = await ReadFileAsync(path, cancellationToken);
        using var sha256 = SHA256.Create();

        var hashBytes = await sha256.ComputeHashAsync(stream, cancellationToken);
        return Convert.ToBase64String(hashBytes);
    }

    #region Helper Methods

    /// <summary>
    /// Normalizes a path for S3 (removes leading/trailing slashes)
    /// </summary>
    private static string NormalizePath(string path) {
        if (string.IsNullOrWhiteSpace(path)) {
            return "";
        }

        // Convert backslashes to forward slashes
        path = path.Replace('\\', '/');

        // Remove leading and trailing slashes
        path = path.Trim('/');

        return path;
    }

    /// <summary>
    /// Gets the full S3 key (path with prefix)
    /// </summary>
    private string GetFullPath(string relativePath) {
        if (string.IsNullOrEmpty(relativePath) || relativePath == "/") {
            return _prefix;
        }

        relativePath = NormalizePath(relativePath);

        if (string.IsNullOrEmpty(_prefix)) {
            return relativePath;
        }

        return $"{_prefix}/{relativePath}";
    }

    /// <summary>
    /// Gets the relative path from a full S3 key
    /// </summary>
    private string GetRelativePath(string fullKey) {
        if (string.IsNullOrEmpty(_prefix)) {
            return fullKey;
        }

        var prefix = _prefix + "/";
        if (fullKey.StartsWith(prefix)) {
            return fullKey.Substring(prefix.Length);
        }

        return fullKey;
    }

    /// <summary>
    /// Gets MIME type based on file extension
    /// </summary>
    private static string GetMimeType(string key) {
        var extension = Path.GetExtension(key).ToLowerInvariant();
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
                await Task.Delay(_retryDelay * (attempt + 1), cancellationToken);
            }
        }

        throw lastException ?? new InvalidOperationException("Operation failed");
    }

    /// <summary>
    /// Determines if an exception is retriable
    /// </summary>
    private static bool IsRetriableException(Exception ex) {
        // Retry on network errors, timeouts, and throttling
        return ex is AmazonS3Exception s3Ex &&
               (s3Ex.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable ||
                s3Ex.StatusCode == System.Net.HttpStatusCode.RequestTimeout ||
                s3Ex.ErrorCode == "RequestTimeout" ||
                s3Ex.ErrorCode == "SlowDown" ||
                s3Ex.ErrorCode == "InternalError");
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
    /// Releases all resources used by the S3 storage instance
    /// </summary>
    public void Dispose() {
        if (!_disposed) {
            _client?.Dispose();
            _transferSemaphore?.Dispose();
            _disposed = true;
        }

        GC.SuppressFinalize(this);
    }

    #endregion
}
