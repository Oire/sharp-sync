# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Common Development Commands

### Building the Project
```bash
# Build the entire solution
dotnet build

# Build in Release mode
dotnet build --configuration Release

# Clean and rebuild
dotnet clean
dotnet build
```

### Running Tests
```bash
# Run all tests (unit tests only, integration tests skip automatically)
dotnet test

# Run tests with verbose output
dotnet test --verbosity normal

# Run tests for a specific project
dotnet test tests/SharpSync.Tests/SharpSync.Tests.csproj

# Run tests with test results output (TRX format)
dotnet test --logger trx --results-directory TestResults
```

#### Running Integration Tests
Integration tests require external services (SFTP, FTP, S3 via LocalStack). Use the provided scripts:

```bash
# Linux/macOS - automatically starts Docker test servers
./scripts/run-integration-tests.sh

# Windows - automatically starts Docker test servers
.\scripts\run-integration-tests.ps1

# Or manually with Docker Compose
docker-compose -f docker-compose.test.yml up -d
export SFTP_TEST_HOST=localhost SFTP_TEST_PORT=2222 SFTP_TEST_USER=testuser SFTP_TEST_PASS=testpass SFTP_TEST_ROOT=/home/testuser/upload
export FTP_TEST_HOST=localhost FTP_TEST_PORT=21 FTP_TEST_USER=testuser FTP_TEST_PASS=testpass FTP_TEST_ROOT=/
export S3_TEST_BUCKET=test-bucket S3_TEST_ACCESS_KEY=test S3_TEST_SECRET_KEY=test S3_TEST_ENDPOINT=http://localhost:4566
dotnet test --verbosity normal
docker-compose -f docker-compose.test.yml down
```

See [TESTING.md](TESTING.md) for detailed testing documentation.

### Creating NuGet Package
```bash
# Create NuGet package
dotnet pack --configuration Release

# Pack specific project with output directory
dotnet pack src/SharpSync/SharpSync.csproj --configuration Release --output ./artifacts

# Pack with version suffix
dotnet pack --configuration Release --version-suffix preview
```

### CI/CD Pipeline Commands
The project uses GitHub Actions for CI/CD. The pipeline currently:
- Builds and tests on **Ubuntu, Windows, and macOS** (matrix strategy)
- Runs format checking on all platforms
- **Integration tests** (SFTP, FTP, S3, WebDAV) run on **Ubuntu only** (Docker-based servers)
- **Unit tests** run on **all platforms** (integration tests auto-skip when env vars not set)
- Automatically configures test environment variables for integration tests on Ubuntu

#### Cross-Platform Testing Strategy
Since integration tests require Docker (not available on GitHub-hosted macOS runners, limited on Windows):
- **Ubuntu**: Full test suite - unit tests + integration tests with Docker services
- **Windows/macOS**: Unit tests only - verifies library compiles and works correctly

Integration tests use `Skip.If()` to gracefully skip when environment variables aren't set, so no code changes are needed for cross-platform support.

## High-Level Architecture

SharpSync is a **pure .NET file synchronization library** with no native dependencies. It provides a modular, interface-based architecture for synchronizing files between various storage backends.

### Core Components

1. **Core Interfaces** (`src/SharpSync/Core/`)
   - `ISyncEngine` - Main synchronization orchestrator (`SynchronizeAsync`, `GetSyncPlanAsync`, `GetStatsAsync`, `ResetSyncStateAsync`, plus selective/incremental sync and lifecycle methods)
   - `ISyncStorage` - Storage backend abstraction (local, WebDAV, cloud) with `ProgressChanged` event and default methods for `SetLastModifiedAsync`/`SetPermissionsAsync`
   - `ISyncDatabase` - Sync state persistence
   - `IConflictResolver` - Pluggable conflict resolution strategies
   - `ISyncFilter` - File filtering for selective sync
   - Domain models: `SyncItem` (with `IsSymlink` support), `SyncOptions`, `SyncProgress`, `SyncResult`

2. **Storage Implementations** (`src/SharpSync/Storage/`)
   - `LocalFileStorage` - Local filesystem operations with symlink detection, timestamp/permission preservation (fully implemented and tested)
   - `WebDavStorage` - WebDAV with OAuth2, chunking, and platform-specific optimizations (fully implemented and tested)
   - `SftpStorage` - SFTP with password and key-based authentication, symlink detection, timestamp/permission preservation (fully implemented and tested)
   - `FtpStorage` - FTP/FTPS with secure connections support and timestamp preservation (fully implemented and tested)
   - `S3Storage` - Amazon S3 and S3-compatible storage (MinIO, LocalStack) with multipart uploads (fully implemented and tested)

   Additional public types in `src/SharpSync/Storage/`:
   - `ServerCapabilities` - Detected server features (Nextcloud, OCIS, chunking, TUS protocol version)
   - `StorageOperation` - Enum: Upload, Download, Delete, Move
   - `StorageProgressEventArgs` - Byte-level progress for storage operations (path, bytes transferred, total, percent)
   - `ThrottledStream` - Token-bucket bandwidth throttling wrapper (internal)
   - `ProgressStream` - Stream wrapper that fires progress events (internal)

3. **Authentication** (`src/SharpSync/Auth/`)
   - `IOAuth2Provider` - OAuth2 authentication abstraction (no UI dependencies)
   - Pre-configured for Nextcloud and OCIS

4. **Database Layer** (`src/SharpSync/Database/`)
   - `SqliteSyncDatabase` - SQLite-based state tracking
   - Optimized indexes for performance
   - Transaction support for consistency

5. **Synchronization Engine** (`src/SharpSync/Sync/`)
   - `SyncEngine` - Production-ready sync implementation with:
     - Incremental sync with change detection (timestamp, checksum-only, or size-only modes)
     - Parallel processing for large file sets
     - Three-phase optimization (directories/small files, large files, deletes/conflicts)
     - All `SyncOptions` properties fully wired: `TimeoutSeconds`, `ChecksumOnly`, `SizeOnly`, `UpdateExisting`, `ConflictResolution` override, `ExcludePatterns`, `Verbose`, `FollowSymlinks`, `PreserveTimestamps`, `PreservePermissions`
   - `SyncFilter` - Pattern-based file filtering

   Internal sync pipeline types (in `Oire.SharpSync.Sync` namespace):
   - `IChange` / `AdditionChange` / `ModificationChange` / `DeletionChange` - Change detection models
   - `ChangeSet` - Aggregates detected additions, modifications, and deletions with path tracking
   - `SyncAction` - Represents a single planned sync operation with type, path, items, and priority
   - `ActionGroups` - Organizes `SyncAction`s into five groups for three-phase execution: Directories, SmallFiles, LargeFiles, Deletes, Conflicts
   - `ThreadSafeSyncResult` - `Interlocked`-based thread-safe counters for parallel sync result tracking
   - `ProgressCounter` - Thread-safe counter for progress tracking across parallel operations

   Internal database types (in `Oire.SharpSync.Database` namespace):
   - `OperationHistory` - SQLite table model for persisted completed operations, maps to/from `CompletedOperation`
   - `SqliteSyncTransaction` - `ISyncTransaction` implementation for SQLite

### Key Features

- **Multi-Protocol Support**: Local, WebDAV, SFTP, FTP/FTPS, and S3-compatible storage
- **OAuth2 Authentication**: Full OAuth2 flow support without UI dependencies (WebDAV)
- **SSH Key & Password Auth**: Secure SFTP authentication with private keys or passwords
- **FTP/FTPS Support**: Plain FTP, explicit FTPS, and implicit FTPS with password authentication
- **S3 Compatibility**: Full AWS S3 support plus S3-compatible services (MinIO, LocalStack, etc.)
- **Smart Conflict Resolution**: Rich conflict analysis for UI integration
- **Selective Sync**: Include/exclude patterns for files and folders
- **Progress Reporting**: Real-time progress events for UI binding
- **Large File Support**: Chunked/multipart uploads with platform-specific optimizations
- **Network Resilience**: Retry logic and error handling with automatic reconnection
- **Parallel Processing**: Configurable parallelism with intelligent prioritization
- **Bandwidth Throttling**: Configurable transfer rate limits via `SyncOptions.MaxBytesPerSecond`
- **Virtual File Support**: Callback hook for Windows Cloud Files API placeholder integration
- **Timestamp Preservation**: Optionally preserves file modification times across sync (`PreserveTimestamps`)
- **Permission Preservation**: Optionally preserves Unix file permissions across sync (`PreservePermissions`)
- **Symlink Awareness**: Detects symlinks and optionally follows or skips them (`FollowSymlinks`)
- **Flexible Change Detection**: Checksum-only or size-only modes for change detection
- **Per-Sync Exclusion**: Runtime exclude patterns via `SyncOptions.ExcludePatterns`
- **Structured Logging**: High-performance logging via `Microsoft.Extensions.Logging` with verbose mode

### Dependencies

- `Microsoft.Extensions.Logging.Abstractions` - Logging abstraction
- `sqlite-net-pcl` / `SQLitePCLRaw.bundle_e_sqlite3` - SQLite database
- `WebDav.Client` - WebDAV protocol
- `SSH.NET` - SFTP protocol implementation
- `FluentFTP` - FTP/FTPS protocol implementation
- `AWSSDK.S3` - Amazon S3 and S3-compatible storage
- Target Framework: .NET 8.0

See `src/SharpSync/SharpSync.csproj` for current versions.

### Platform-Specific Optimizations

- **Nextcloud**: Native chunking v2 API support (fully implemented)
- **OCIS**: TUS 1.0.0 protocol for resumable uploads (fully implemented)
- **Generic WebDAV**: Fallback with progress reporting

### Design Patterns

1. **Interface-Based Design**: All major components use interfaces for testability
2. **Async/Await Throughout**: Modern async patterns for all I/O operations
3. **Event-Driven Progress**: Events for progress and conflict notifications
4. **Dependency Injection Ready**: Constructor-based dependencies
5. **Disposable Pattern**: Proper resource cleanup

### Important Considerations

1. **Threading Model**: Only one sync operation can run at a time per `SyncEngine` instance. However, the following are thread-safe and can be called from any thread (including while sync runs):
   - State properties: `IsSynchronizing`, `IsPaused`, `State`
   - Change notifications: `NotifyLocalChangeAsync`, `NotifyLocalChangeBatchAsync`, `NotifyLocalRenameAsync`
   - Control methods: `PauseAsync`, `ResumeAsync`
   - Query methods: `GetPendingOperationsAsync`, `GetRecentOperationsAsync`
   - `ClearPendingChanges`
2. **No UI Dependencies**: Library is UI-agnostic, suitable for any .NET application
3. **Conflict Resolution**: Provides data for UI decisions without implementing UI
4. **OAuth2 Flow**: Caller must implement browser-based auth flow
5. **Database Location**: Caller controls where SQLite database is stored

## Project Structure
```
/
├── SharpSync.sln             # Solution file at root
├── src/
│   └── SharpSync/            # Main library project
│       ├── Auth/             # OAuth2 authentication
│       ├── Core/             # Interfaces and models
│       ├── Database/         # State persistence
│       ├── Logging/          # High-performance logging
│       ├── Storage/          # Storage backends
│       └── Sync/             # Sync engine
├── tests/
│   └── SharpSync.Tests/      # Unit tests
│       ├── Fixtures/         # Test infrastructure and utilities
│       ├── Core/
│       ├── Database/
│       ├── Storage/
│       └── Sync/
├── examples/                 # Usage examples
│   ├── BasicSyncExample.cs
│   ├── ConsoleOAuth2Example.cs
│   └── README.md
└── .github/
    └── workflows/            # CI/CD configuration
```

## Typical Usage Pattern

```csharp
// 1. Create storage instances
var localStorage = new LocalFileStorage("/path/to/local");
var remoteStorage = new WebDavStorage("https://cloud.example.com/remote.php/dav/files/user/",
    oauth2Provider: myOAuth2Provider);

// 2. Create database for state tracking
var database = new SqliteSyncDatabase("/path/to/sync.db");

// 3. Create sync engine
var syncEngine = new SyncEngine(localStorage, remoteStorage, database);

// 4. Configure and run sync
var options = new SyncOptions { ConflictResolver = new SmartConflictResolver() };
var result = await syncEngine.SynchronizeAsync(options);
```

## Nimbus Desktop Client Integration

SharpSync serves as the core sync library for **Nimbus**, an accessible Nextcloud desktop client alternative for Windows. This section documents the integration architecture and recommendations.

### Architecture: Library vs Application Responsibilities

**SharpSync (Library) Provides:**
- Sync engine with bidirectional sync, conflict detection, progress reporting
- WebDAV storage with Nextcloud-specific optimizations (chunking v2, server detection)
- OAuth2 token management abstraction (`IOAuth2Provider`)
- Conflict resolution with rich analysis (`SmartConflictResolver`)
- Sync state persistence (`ISyncDatabase`)
- Pattern-based filtering (`ISyncFilter`)

**Nimbus (Application) Handles:**
- Desktop UI (WPF/WinUI/Avalonia)
- System tray integration
- Filesystem monitoring (`FileSystemWatcher`)
- Credential storage (Windows Credential Manager)
- Background sync scheduling
- Virtual files (Windows Cloud Files API)
- User preferences and settings

### Current API Strengths for Desktop Clients

| Feature | Status | Notes |
|---------|--------|-------|
| Progress events | Excellent | `ProgressChanged` (item-level) and `FileProgressChanged` (per-file byte-level) |
| Conflict events | Excellent | `ConflictDetected` with rich `ConflictAnalysis` data |
| Sync preview | Excellent | `GetSyncPlanAsync()` returns detailed plan before execution |
| OAuth2 abstraction | Good | `IOAuth2Provider` interface for app-specific implementation |
| Conflict resolution | Excellent | `SmartConflictResolver` with UI callback delegate |
| Nextcloud support | Good | Chunking v2, server detection, ETag handling |

### Nimbus Integration Pattern

```csharp
// 1. Nimbus implements IOAuth2Provider with Windows-specific auth
public class NimbusOAuth2Provider : IOAuth2Provider
{
    public async Task<OAuth2Result> AuthenticateAsync(OAuth2Config config, ...)
    {
        // Open system browser to authorization URL
        // Listen on localhost for OAuth callback
        // Exchange authorization code for tokens
        // Store tokens in Windows Credential Manager
        return result;
    }
}

// 2. Create storage and engine instances
var localStorage = new LocalFileStorage(localSyncPath);
var remoteStorage = new WebDavStorage(nextcloudUrl, nimbusOAuthProvider);
var database = new SqliteSyncDatabase(Path.Combine(appDataPath, "sync.db"));
var engine = new SyncEngine(localStorage, remoteStorage, database);

// 3. Wire up UI binding via events
engine.ProgressChanged += (s, e) => {
    Dispatcher.Invoke(() => {
        OverallProgressBar.Value = e.Progress.Percentage;
        StatusLabel.Text = $"Syncing: {e.Progress.CurrentItem}";
        ItemCountLabel.Text = $"{e.Progress.ProcessedItems}/{e.Progress.TotalItems}";
    });
};

// 3b. Wire up per-file transfer progress for detailed UI
engine.FileProgressChanged += (s, e) => {
    Dispatcher.Invoke(() => {
        FileProgressBar.Value = e.PercentComplete;
        FileProgressLabel.Text = $"{e.Path}: {e.BytesTransferred / 1024}KB / {e.TotalBytes / 1024}KB";
        TransferTypeLabel.Text = e.Operation == FileTransferOperation.Upload ? "Uploading" : "Downloading";
    });
};

// 4. Implement conflict resolution with UI dialogs
var resolver = new SmartConflictResolver(
    conflictHandler: async (analysis, ct) => {
        // analysis contains: LocalSize, RemoteSize, LocalModified, RemoteModified,
        // NewerVersion, RecommendedResolution, Reasoning
        return await Dispatcher.InvokeAsync(() => ShowConflictDialog(analysis));
    },
    defaultResolution: ConflictResolution.Ask
);

// 5. Preview changes before sync (optional UI feature)
var plan = await engine.GetSyncPlanAsync();
// plan.Downloads, plan.Uploads, plan.Deletes, plan.Conflicts, plan.Summary

// 6. FileSystemWatcher integration with incremental sync
_watcher = new FileSystemWatcher(localSyncPath);
_watcher.Changed += async (s, e) => {
    var relativePath = Path.GetRelativePath(localSyncPath, e.FullPath);
    await engine.NotifyLocalChangeAsync(relativePath, ChangeType.Changed);
};
_watcher.Created += async (s, e) => {
    var relativePath = Path.GetRelativePath(localSyncPath, e.FullPath);
    await engine.NotifyLocalChangeAsync(relativePath, ChangeType.Created);
};
_watcher.Deleted += async (s, e) => {
    var relativePath = Path.GetRelativePath(localSyncPath, e.FullPath);
    await engine.NotifyLocalChangeAsync(relativePath, ChangeType.Deleted);
};
_watcher.EnableRaisingEvents = true;

// 7. Get pending operations for UI display
var pending = await engine.GetPendingOperationsAsync();
StatusLabel.Text = $"{pending.Count} files waiting to sync";

// 8. Selective sync - sync only specific folder on demand
await engine.SyncFolderAsync("Documents/Important");

// 9. Or sync specific files
await engine.SyncFilesAsync(new[] { "notes.txt", "config.json" });

// 10. Display activity history in UI
var recentOps = await engine.GetRecentOperationsAsync(limit: 50);
foreach (var op in recentOps) {
    var icon = op.ActionType switch {
        SyncActionType.Upload => "↑",
        SyncActionType.Download => "↓",
        SyncActionType.DeleteLocal or SyncActionType.DeleteRemote => "×",
        _ => "?"
    };
    var status = op.Success ? "✓" : "✗";
    ActivityList.Items.Add($"{status} {icon} {op.Path} ({op.Duration.TotalSeconds:F1}s)");
}

// 11. Periodic cleanup of old history (e.g., on app startup)
var deleted = await engine.ClearOperationHistoryAsync(DateTime.UtcNow.AddDays(-30));
```

### Design Constraints

| Constraint | Impact | Notes |
|------------|--------|-------|
| Single-threaded engine | One sync at a time per instance | By design - create separate instances if needed |

### ✅ Resolved API Gaps

| Feature | Implementation |
|---------|----------------|
| Bandwidth throttling | `SyncOptions.MaxBytesPerSecond` - limits transfer rate |
| Virtual file awareness | `SyncOptions.VirtualFileCallback` - hook for Windows Cloud Files API integration |
| Pause/Resume sync | `PauseAsync()` / `ResumeAsync()` - gracefully pause and resume long-running syncs |
| Selective folder sync | `SyncFolderAsync(path)` - sync specific folder without full scan |
| Selective file sync | `SyncFilesAsync(paths)` - sync specific files on demand |
| Incremental change notification | `NotifyLocalChangeAsync(path, changeType)` - accept FileSystemWatcher events |
| Batch change notification | `NotifyLocalChangeBatchAsync(changes)` - efficient batch FileSystemWatcher events |
| Rename tracking | `NotifyLocalRenameAsync(oldPath, newPath)` - proper rename operation tracking |
| Pending operations query | `GetPendingOperationsAsync()` - inspect sync queue for UI display |
| Clear pending changes | `ClearPendingChanges()` - discard pending notifications without syncing |
| GetSyncPlanAsync integration | `GetSyncPlanAsync()` now incorporates pending changes from notifications |
| Activity history | `GetRecentOperationsAsync()` - query completed operations for activity feed |
| History cleanup | `ClearOperationHistoryAsync()` - purge old operation records |
| Per-file progress | `FileProgressChanged` event on `ISyncEngine` - byte-level progress for individual file transfers |
| SyncOptions wiring | All `SyncOptions` properties are now functional: `TimeoutSeconds`, `ChecksumOnly`, `SizeOnly`, `UpdateExisting`, `ConflictResolution` override, `ExcludePatterns`, `Verbose`, `FollowSymlinks`, `PreserveTimestamps`, `PreservePermissions` |
| Timestamp preservation | `ISyncStorage.SetLastModifiedAsync` default interface method, implemented in Local/SFTP/FTP storage |
| Permission preservation | `ISyncStorage.SetPermissionsAsync` default interface method, implemented in Local/SFTP storage |
| Symlink awareness | `SyncItem.IsSymlink` property, detected in Local/SFTP storage, `FollowSymlinks` option in SyncEngine |

### Required SharpSync API Additions (v1.0)

These APIs are required for v1.0 release to support Nimbus desktop client:

**✅ Completed:**
- `FileProgressChanged` event on `ISyncEngine` - Per-file byte-level progress during uploads/downloads
- `FileProgressEventArgs` - Per-file progress data with path, bytes transferred, total bytes, and operation type
- `FileTransferOperation` enum - Upload/Download operation type for per-file progress
- `ISyncStorage.ProgressChanged` - Standardized progress event on the storage interface
- OCIS TUS 1.0.0 protocol - Resumable uploads for OCIS servers with chunked transfer and fallback
- `GetRecentOperationsAsync()` - Operation history for activity feed with time filtering
- `ClearOperationHistoryAsync()` - Cleanup old operation history entries
- `CompletedOperation` model - Rich operation details with timing, success/failure, rename tracking
- `SyncOptions.MaxBytesPerSecond` - Built-in bandwidth throttling
- `SyncOptions.VirtualFileCallback` - Hook for virtual file systems (Windows Cloud Files API)
- `SyncOptions.CreateVirtualFilePlaceholders` - Enable/disable virtual file placeholder creation
- `VirtualFileState` enum - Track placeholder state (None, Placeholder, Hydrated, Partial)
- `SyncPlanAction.WillCreateVirtualPlaceholder` - Preview which downloads will create placeholders
- `PauseAsync()` / `ResumeAsync()` - Gracefully pause and resume long-running syncs
- `IsPaused` property and `SyncEngineState` enum - Track engine state (Idle, Running, Paused)
- `SyncFolderAsync(path)` - Sync a specific folder without full scan
- `SyncFilesAsync(paths)` - Sync specific files on demand
- `NotifyLocalChangeAsync(path, changeType)` - Accept FileSystemWatcher events for incremental sync
- `NotifyLocalChangeBatchAsync(changes)` - Batch change notification for efficient FileSystemWatcher handling
- `NotifyLocalRenameAsync(oldPath, newPath)` - Proper rename operation tracking with old/new paths
- `GetPendingOperationsAsync()` - Inspect sync queue for UI display
- `ClearPendingChanges()` - Discard pending notifications without syncing
- `GetSyncPlanAsync()` integration - Now incorporates pending changes from notifications
- `ChangeType` enum - Represents FileSystemWatcher change types (Created, Changed, Deleted, Renamed)
- `PendingOperation` model - Represents operations waiting in sync queue with rename tracking

### API Readiness Score for Nimbus

**Current State (v1.0):**

| Component | Score | Notes |
|-----------|-------|-------|
| Core sync engine | 9/10 | Production-ready, well-tested |
| Nextcloud WebDAV | 9/10 | Full support including OCIS TUS protocol |
| OAuth2 abstraction | 9/10 | Clean interface, Nimbus implements |
| UI binding (events) | 10/10 | Per-file byte-level progress, item-level progress, conflict events |
| Conflict resolution | 9/10 | Rich analysis, extensible callbacks |
| Selective sync | 10/10 | Complete: folder/file/incremental sync, batch notifications, rename tracking |
| Pause/Resume | 10/10 | Fully implemented with graceful pause points |
| Desktop integration hooks | 10/10 | Virtual file callback, bandwidth throttling, pause/resume, pending operations |

**Current Overall: 10/10** - Production-ready with comprehensive desktop client APIs including per-file progress

## Version 1.0 Release Readiness

### Current Status: 100% Complete

The core library is production-ready. All critical items are complete and the library is ready for v1.0 release.

### ✅ What's Complete and Production-Ready

**Core Architecture**
- Interface-based design (`ISyncEngine`, `ISyncStorage`, `ISyncDatabase`, `IConflictResolver`, `ISyncFilter`)
- Domain models with comprehensive XML documentation
- Well-tested core components

**Implementations**
- `SyncEngine` - Production-ready sync logic with three-phase optimization
- `LocalFileStorage` - Fully implemented and tested
- `SftpStorage` - Fully implemented with password/key auth and tested
- `FtpStorage` - Fully implemented with FTP/FTPS support and tested
- `S3Storage` - Fully implemented with multipart uploads and tested (LocalStack integration)
- `WebDavStorage` - OAuth2, chunking, platform optimizations, and tested
- `SqliteSyncDatabase` - Complete with transaction support and tests
- `SmartConflictResolver` - Intelligent conflict analysis with tests
- `DefaultConflictResolver` - Strategy-based resolution with tests
- `SyncFilter` - Pattern-based filtering with tests

**Infrastructure**
- Clean solution structure
- `.editorconfig` with comprehensive C# style rules
- Multi-platform CI/CD pipeline (Ubuntu, Windows, macOS with matrix strategy)
- Integration tests for all storage backends (SFTP, FTP, S3, WebDAV) via Docker on Ubuntu
- Examples directory with working samples

### 🚨 CRITICAL (Must Fix Before v1.0)

All critical items have been resolved.

### 📊 Quality Metrics for v1.0

**Minimum Acceptance Criteria:**
- ✅ Core sync engine tested
- ✅ All storage implementations tested (LocalFileStorage, SftpStorage, FtpStorage, S3Storage, WebDavStorage)
- ✅ README matches actual API
- ✅ No TODOs/FIXMEs in code
- ✅ Examples directory exists
- ✅ Package metadata accurate
- ✅ Integration test infrastructure (Docker-based CI for all backends)
- ✅ Multi-platform CI (Ubuntu, Windows, macOS)

**Current Score: 9/9 (100%)** - All critical items complete!

### 🎯 v1.0 Roadmap

**All critical work complete - ready for v1.0 release!**

**Completed:**
- ✅ README.md rewritten with correct API documentation and examples
- ✅ All storage backends (Local, SFTP, FTP, S3, WebDAV)
- ✅ Integration tests for all backends
- ✅ Multi-platform CI (Ubuntu + Windows + macOS)
- ✅ Bandwidth throttling (`SyncOptions.MaxBytesPerSecond`)
- ✅ Virtual file placeholder support (`SyncOptions.VirtualFileCallback`)
- ✅ High-performance logging with `Microsoft.Extensions.Logging.Abstractions`
- ✅ Pause/Resume sync (`PauseAsync()` / `ResumeAsync()`)
- ✅ Selective sync (`SyncFolderAsync()`, `SyncFilesAsync()`)
- ✅ FileSystemWatcher integration (`NotifyLocalChangeAsync()`, `NotifyLocalChangeBatchAsync()`, `NotifyLocalRenameAsync()`)
- ✅ Pending operations query (`GetPendingOperationsAsync()`)
- ✅ Activity history (`GetRecentOperationsAsync()`, `ClearOperationHistoryAsync()`)
- ✅ Per-file progress events (`FileProgressChanged` on `ISyncEngine`, `FileProgressEventArgs`, `FileTransferOperation`)
- ✅ Examples directory with working samples
- ✅ Code coverage reporting (Coverlet + Codecov with badge in README)
- ✅ Console OAuth2 provider example (`examples/ConsoleOAuth2Example.cs`)
- ✅ All `SyncOptions` properties wired and functional (TimeoutSeconds, ChecksumOnly, SizeOnly, UpdateExisting, ConflictResolution override, ExcludePatterns, Verbose, FollowSymlinks, PreserveTimestamps, PreservePermissions)
- ✅ `ISyncStorage.SetLastModifiedAsync` / `SetPermissionsAsync` default interface methods
- ✅ Symlink detection (`SyncItem.IsSymlink`) in Local and SFTP storage
