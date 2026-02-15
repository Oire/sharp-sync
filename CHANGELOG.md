# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.0.0] - 2026-02-15

### Initial Release

A pure .NET 8.0 file synchronization library. SharpSync provides a modular, interface-based architecture for synchronizing files between various storage backends.

#### Storage Backends

- **Local filesystem** (`LocalFileStorage`) with symlink detection, timestamp and permission preservation
- **WebDAV** (`WebDavStorage`) with OAuth2 authentication, Nextcloud chunking v2, and OCIS TUS 1.0.0 resumable uploads
- **SFTP** (`SftpStorage`) with password and private key authentication, symlink detection, and timestamp/permission preservation
- **FTP/FTPS** (`FtpStorage`) with explicit and implicit TLS, and timestamp preservation
- **Amazon S3** (`S3Storage`) and S3-compatible storage (MinIO, LocalStack) with multipart uploads

#### Sync Engine

- Bidirectional synchronization with three-phase optimization (directories/small files, large files, deletes/conflicts)
- Incremental sync with change detection (timestamp, checksum-only, or size-only modes)
- Configurable parallel processing for large file sets
- Sync plan preview via `GetSyncPlanAsync()` before executing changes
- Smart conflict resolution with rich `ConflictAnalysis` data for UI integration
- Pattern-based file filtering with ReDoS-safe regex (`SyncFilter`)

#### Selective and Incremental Sync

- `SyncFolderAsync()` to sync a specific folder without full scan
- `SyncFilesAsync()` to sync specific files on demand
- `NotifyLocalChangeAsync()` / `NotifyLocalChangeBatchAsync()` for FileSystemWatcher integration
- `NotifyLocalRenameAsync()` for proper rename tracking
- `NotifyRemoteChangeAsync()` / `NotifyRemoteChangeBatchAsync()` / `NotifyRemoteRenameAsync()` for remote change detection
- `GetPendingOperationsAsync()` to inspect the sync queue
- `ClearPendingLocalChanges()` / `ClearPendingRemoteChanges()` to discard pending notifications
- Per-sync exclusion patterns via `SyncOptions.ExcludePatterns`

#### Lifecycle and Control

- `PauseAsync()` / `ResumeAsync()` for gracefully pausing and resuming long-running syncs
- `SyncEngineState` enum (Idle, Running, Paused) and `IsPaused` property
- Thread-safe state properties and change notification methods (safe to call while sync runs)

#### Progress and History

- `ProgressChanged` event for item-level sync progress
- `FileProgressChanged` event for per-file byte-level transfer progress
- `ConflictDetected` event for interactive conflict resolution
- `GetRecentOperationsAsync()` for activity history with time filtering
- `ClearOperationHistoryAsync()` for cleaning up old operation records

#### Desktop Client Integration

- `SyncOptions.MaxBytesPerSecond` for bandwidth throttling
- `SyncOptions.VirtualFileCallback` hook for Windows Cloud Files API placeholder integration
- `SyncOptions.PreserveTimestamps` / `PreservePermissions` for metadata preservation
- `SyncOptions.FollowSymlinks` for symlink handling policy
- `ISyncStorage.SetLastModifiedAsync()` / `SetPermissionsAsync()` default interface methods
- `ISyncStorage.GetRemoteChangesAsync()` for storage-level remote change detection

#### Database

- `SqliteSyncDatabase` with optimized indexes and transaction support
- Implements both `IDisposable` and `IAsyncDisposable`
- Operation history persistence for activity feeds

#### Infrastructure

- Structured logging via `Microsoft.Extensions.Logging` with source-generated `[LoggerMessage]` attributes
- Deterministic builds with embedded debug symbols and SourceLink
- Multi-platform CI/CD (Ubuntu, Windows, macOS) with integration tests on Ubuntu via Docker
- Console sample application with OAuth2 example

[1.0.0]: https://github.com/Oire/sharp-sync/releases/tag/v1.0.0
