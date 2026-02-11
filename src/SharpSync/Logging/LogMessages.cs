using Microsoft.Extensions.Logging;

namespace Oire.SharpSync.Logging;

/// <summary>
/// High-performance log messages using source generators.
/// </summary>
internal static partial class LogMessages {
    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Warning,
        Message = "Error scanning directory {DirectoryPath}")]
    public static partial void DirectoryScanError(this ILogger logger, Exception ex, string directoryPath);

    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Warning,
        Message = "Error processing {FilePath}")]
    public static partial void ProcessingError(this ILogger logger, Exception ex, string filePath);

    [LoggerMessage(
        EventId = 3,
        Level = LogLevel.Warning,
        Message = "Error processing large file {FilePath}")]
    public static partial void LargeFileProcessingError(this ILogger logger, Exception ex, string filePath);

    [LoggerMessage(
        EventId = 4,
        Level = LogLevel.Warning,
        Message = "Error resolving conflict for {FilePath}")]
    public static partial void ConflictResolutionError(this ILogger logger, Exception ex, string filePath);

    [LoggerMessage(
        EventId = 5,
        Level = LogLevel.Warning,
        Message = "Error deleting {FilePath}")]
    public static partial void DeletionError(this ILogger logger, Exception ex, string filePath);

    [LoggerMessage(
        EventId = 6,
        Level = LogLevel.Warning,
        Message = "Virtual file callback failed for {FilePath}")]
    public static partial void VirtualFileCallbackError(this ILogger logger, Exception ex, string filePath);

    [LoggerMessage(
        EventId = 7,
        Level = LogLevel.Information,
        Message = "Sync pause requested, waiting for current operation to complete")]
    public static partial void SyncPausing(this ILogger logger);

    [LoggerMessage(
        EventId = 8,
        Level = LogLevel.Information,
        Message = "Sync paused")]
    public static partial void SyncPaused(this ILogger logger);

    [LoggerMessage(
        EventId = 9,
        Level = LogLevel.Information,
        Message = "Sync resume requested")]
    public static partial void SyncResuming(this ILogger logger);

    [LoggerMessage(
        EventId = 10,
        Level = LogLevel.Information,
        Message = "Sync resumed")]
    public static partial void SyncResumed(this ILogger logger);

    [LoggerMessage(
        EventId = 11,
        Level = LogLevel.Debug,
        Message = "Local change notified: {Path} ({ChangeType})")]
    public static partial void LocalChangeNotified(this ILogger logger, string path, Core.ChangeType changeType);

    [LoggerMessage(
        EventId = 12,
        Level = LogLevel.Warning,
        Message = "Failed to log operation for {Path}")]
    public static partial void OperationLoggingError(this ILogger logger, Exception ex, string path);

    [LoggerMessage(
        EventId = 13,
        Level = LogLevel.Debug,
        Message = "Detecting changes (options: ChecksumOnly={ChecksumOnly}, SizeOnly={SizeOnly})")]
    public static partial void DetectChangesStart(this ILogger logger, bool checksumOnly, bool sizeOnly);

    [LoggerMessage(
        EventId = 14,
        Level = LogLevel.Debug,
        Message = "Change detection complete: {Additions} additions, {Modifications} modifications, {Deletions} deletions")]
    public static partial void DetectChangesComplete(this ILogger logger, int additions, int modifications, int deletions);

    [LoggerMessage(
        EventId = 15,
        Level = LogLevel.Debug,
        Message = "HasChanged {Path}: isLocal={IsLocal}, result={Changed}")]
    public static partial void HasChangedResult(this ILogger logger, string path, bool isLocal, bool changed);

    [LoggerMessage(
        EventId = 16,
        Level = LogLevel.Debug,
        Message = "Processing action: {ActionType} {Path}")]
    public static partial void ProcessingAction(this ILogger logger, Core.SyncActionType actionType, string path);

    [LoggerMessage(
        EventId = 17,
        Level = LogLevel.Debug,
        Message = "Phase {Phase} complete: processed {Count} actions")]
    public static partial void PhaseComplete(this ILogger logger, int phase, int count);

    [LoggerMessage(
        EventId = 18,
        Level = LogLevel.Warning,
        Message = "Failed to preserve timestamps for {Path}")]
    public static partial void TimestampPreservationError(this ILogger logger, Exception ex, string path);

    [LoggerMessage(
        EventId = 19,
        Level = LogLevel.Warning,
        Message = "Failed to preserve permissions for {Path}")]
    public static partial void PermissionPreservationError(this ILogger logger, Exception ex, string path);

    // Storage - Connection & Auth (20-24)

    [LoggerMessage(
        EventId = 20,
        Level = LogLevel.Debug,
        Message = "Connected to {StorageType} storage at {Endpoint}")]
    public static partial void StorageConnectionEstablished(this ILogger logger, string storageType, string endpoint);

    [LoggerMessage(
        EventId = 21,
        Level = LogLevel.Warning,
        Message = "Failed to connect to {StorageType} storage at {Endpoint}")]
    public static partial void StorageConnectionFailed(this ILogger logger, Exception ex, string storageType, string endpoint);

    [LoggerMessage(
        EventId = 22,
        Level = LogLevel.Warning,
        Message = "Reconnecting to {StorageType} storage (attempt {Attempt})")]
    public static partial void StorageReconnecting(this ILogger logger, int attempt, string storageType);

    [LoggerMessage(
        EventId = 23,
        Level = LogLevel.Warning,
        Message = "Reconnect to {StorageType} storage failed")]
    public static partial void StorageReconnectFailed(this ILogger logger, Exception ex, string storageType);

    [LoggerMessage(
        EventId = 24,
        Level = LogLevel.Warning,
        Message = "OAuth2 token refresh failed, falling back to full authentication")]
    public static partial void OAuthTokenRefreshFailed(this ILogger logger, Exception ex);

    // Storage - Retry & Resilience (25-26)

    [LoggerMessage(
        EventId = 25,
        Level = LogLevel.Debug,
        Message = "Retrying {StorageType} operation (attempt {Attempt} of {MaxRetries})")]
    public static partial void StorageOperationRetry(this ILogger logger, string storageType, int attempt, int maxRetries);

    [LoggerMessage(
        EventId = 26,
        Level = LogLevel.Warning,
        Message = "Storage operation failed for {Path} on {StorageType}")]
    public static partial void StorageOperationFailed(this ILogger logger, Exception ex, string path, string storageType);

    // Storage - WebDAV Specific (27-31)

    [LoggerMessage(
        EventId = 27,
        Level = LogLevel.Warning,
        Message = "Failed to clean up chunk folder {Path}")]
    public static partial void ChunkCleanupFailed(this ILogger logger, Exception ex, string path);

    [LoggerMessage(
        EventId = 28,
        Level = LogLevel.Warning,
        Message = "Failed to detect server capabilities for {Endpoint}")]
    public static partial void ServerCapabilityDetectionFailed(this ILogger logger, Exception ex, string endpoint);

    [LoggerMessage(
        EventId = 29,
        Level = LogLevel.Debug,
        Message = "Server capabilities detected: Nextcloud={IsNextcloud}, OCIS={IsOcis}, Chunking={SupportsChunking}")]
    public static partial void ServerCapabilitiesDetected(this ILogger logger, bool isNextcloud, bool isOcis, bool supportsChunking);

    [LoggerMessage(
        EventId = 30,
        Level = LogLevel.Warning,
        Message = "TUS upload resume failed for {Path}, retrying from offset {Offset}")]
    public static partial void TusUploadResumeFailed(this ILogger logger, Exception ex, string path, long offset);

    [LoggerMessage(
        EventId = 31,
        Level = LogLevel.Warning,
        Message = "Server-side checksum unavailable for {Path}, computing locally")]
    public static partial void ServerChecksumUnavailable(this ILogger logger, Exception ex, string path);

    // Storage - SFTP Specific (32-34)

    [LoggerMessage(
        EventId = 32,
        Level = LogLevel.Warning,
        Message = "SFTP permission denied during {Operation} for {Path}")]
    public static partial void SftpPermissionDenied(this ILogger logger, Exception ex, string operation, string path);

    [LoggerMessage(
        EventId = 33,
        Level = LogLevel.Warning,
        Message = "SFTP server does not support statvfs extension")]
    public static partial void SftpStatVfsUnsupported(this ILogger logger, Exception ex);

    [LoggerMessage(
        EventId = 34,
        Level = LogLevel.Debug,
        Message = "SFTP chroot detected, using relative paths")]
    public static partial void SftpChrootDetected(this ILogger logger);

    // Storage - S3 Specific (35)

    [LoggerMessage(
        EventId = 35,
        Level = LogLevel.Warning,
        Message = "Failed to delete S3 directory marker {Path}")]
    public static partial void S3DirectoryMarkerCleanupFailed(this ILogger logger, Exception ex, string path);

    // Storage - Disposal (36)

    [LoggerMessage(
        EventId = 36,
        Level = LogLevel.Warning,
        Message = "Error disconnecting from {StorageType} storage during disposal")]
    public static partial void StorageDisconnectFailed(this ILogger logger, Exception ex, string storageType);

    // Storage - Test Connection (37)

    [LoggerMessage(
        EventId = 37,
        Level = LogLevel.Warning,
        Message = "Connection test failed for {StorageType} storage")]
    public static partial void ConnectionTestFailed(this ILogger logger, Exception ex, string storageType);

    // Storage - WebDAV upload conflict/retry (38)

    [LoggerMessage(
        EventId = 38,
        Level = LogLevel.Warning,
        Message = "WebDAV upload received 409 Conflict for {Path}, recreating directories and retrying")]
    public static partial void WebDavUploadConflict(this ILogger logger, string path);

    // Storage - TUS fallback (39)

    [LoggerMessage(
        EventId = 39,
        Level = LogLevel.Warning,
        Message = "TUS upload failed for {Path}, falling back to generic WebDAV upload")]
    public static partial void TusUploadFallback(this ILogger logger, Exception ex, string path);

    // Storage - Upload strategy selection (40)

    [LoggerMessage(
        EventId = 40,
        Level = LogLevel.Debug,
        Message = "Using {Strategy} upload strategy for {Path}")]
    public static partial void UploadStrategySelected(this ILogger logger, string strategy, string path);

    // Storage - SFTP alternate path (41)

    [LoggerMessage(
        EventId = 41,
        Level = LogLevel.Debug,
        Message = "SFTP permission denied for {Path}, trying alternate path form")]
    public static partial void SftpTryingAlternatePath(this ILogger logger, Exception ex, string path);

    // Remote change detection (42-44)

    [LoggerMessage(
        EventId = 42,
        Level = LogLevel.Debug,
        Message = "Remote change notified: {Path} ({ChangeType})")]
    public static partial void RemoteChangeNotified(this ILogger logger, string path, Core.ChangeType changeType);

    [LoggerMessage(
        EventId = 43,
        Level = LogLevel.Debug,
        Message = "Remote change poll completed: {ChangeCount} changes detected since {Since}")]
    public static partial void RemoteChangePollCompleted(this ILogger logger, int changeCount, DateTime since);

    [LoggerMessage(
        EventId = 44,
        Level = LogLevel.Warning,
        Message = "Failed to poll remote storage for changes")]
    public static partial void RemoteChangePollFailed(this ILogger logger, Exception ex);

    [LoggerMessage(
        EventId = 45,
        Level = LogLevel.Debug,
        Message = "Could not retrieve item metadata for pending change at {Path}; file may have been deleted since notification")]
    public static partial void PendingChangeItemNotFound(this ILogger logger, Exception ex, string path);
}
