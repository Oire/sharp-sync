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
- Builds on Ubuntu only (multi-platform testing planned)
- Runs tests with format checking
- Includes SFTP, FTP, and S3 integration tests using Docker-based servers (LocalStack for S3)
- Automatically configures test environment variables for integration tests

## High-Level Architecture

SharpSync is a **pure .NET file synchronization library** with no native dependencies. It provides a modular, interface-based architecture for synchronizing files between various storage backends.

### Core Components

1. **Core Interfaces** (`src/SharpSync/Core/`)
   - `ISyncEngine` - Main synchronization orchestrator
   - `ISyncStorage` - Storage backend abstraction (local, WebDAV, cloud)
   - `ISyncDatabase` - Sync state persistence
   - `IConflictResolver` - Pluggable conflict resolution strategies
   - `ISyncFilter` - File filtering for selective sync
   - Domain models: `SyncItem`, `SyncOptions`, `SyncProgress`, `SyncResult`

2. **Storage Implementations** (`src/SharpSync/Storage/`)
   - `LocalFileStorage` - Local filesystem operations (fully implemented and tested)
   - `WebDavStorage` - WebDAV with OAuth2, chunking, and platform-specific optimizations (implemented, needs tests)
   - `SftpStorage` - SFTP with password and key-based authentication (fully implemented and tested)
   - `FtpStorage` - FTP/FTPS with secure connections support (fully implemented and tested)
   - `S3Storage` - Amazon S3 and S3-compatible storage (MinIO, LocalStack) with multipart uploads (fully implemented and tested)

3. **Authentication** (`src/SharpSync/Auth/`)
   - `IOAuth2Provider` - OAuth2 authentication abstraction (no UI dependencies)
   - Pre-configured for Nextcloud and OCIS

4. **Database Layer** (`src/SharpSync/Database/`)
   - `SqliteSyncDatabase` - SQLite-based state tracking
   - Optimized indexes for performance
   - Transaction support for consistency

5. **Synchronization Engine** (`src/SharpSync/Sync/`)
   - `SyncEngine` - Production-ready sync implementation with:
     - Incremental sync with change detection
     - Parallel processing for large file sets
     - Three-phase optimization (directories/small files, large files, deletes/conflicts)
   - `SyncFilter` - Pattern-based file filtering

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
- **Structured Logging**: High-performance logging via `Microsoft.Extensions.Logging`

### Dependencies

- `Microsoft.Extensions.Logging.Abstractions` (9.0.1) - Logging abstraction
- `sqlite-net-pcl` (1.9.172) - SQLite database
- `SQLitePCLRaw.bundle_e_sqlite3` (3.0.2) - SQLite native binaries
- `WebDav.Client` (2.9.0) - WebDAV protocol
- `SSH.NET` (2025.1.0) - SFTP protocol implementation
- `FluentFTP` (52.0.2) - FTP/FTPS protocol implementation
- `AWSSDK.S3` (3.7.*) - Amazon S3 and S3-compatible storage
- Target Framework: .NET 8.0

### Platform-Specific Optimizations

- **Nextcloud**: Native chunking v2 API support (fully implemented)
- **OCIS**: TUS protocol preparation (NOT YET IMPLEMENTED - falls back to generic upload)
- **Generic WebDAV**: Fallback with progress reporting

### Design Patterns

1. **Interface-Based Design**: All major components use interfaces for testability
2. **Async/Await Throughout**: Modern async patterns for all I/O operations
3. **Event-Driven Progress**: Events for progress and conflict notifications
4. **Dependency Injection Ready**: Constructor-based dependencies
5. **Disposable Pattern**: Proper resource cleanup

### Important Considerations

1. **Thread Safety**: `SyncEngine` instances are NOT thread-safe. Use one per thread.
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
├── examples/                 # (Planned for v1.0)
│   └── BasicSyncExample.cs   # Usage examples
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
var result = await syncEngine.SyncAsync(options);
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
| Progress events | Excellent | `ProgressChanged` event with percentage, item counts, current file |
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
        ProgressBar.Value = e.Progress.Percentage;
        StatusLabel.Text = $"Syncing: {e.Progress.CurrentItem}";
        ItemCountLabel.Text = $"{e.Progress.ProcessedItems}/{e.Progress.TotalItems}";
    });
};

// 4. Implement conflict resolution with UI dialogs
var resolver = new SmartConflictResolver(
    conflictHandler: async (analysis, ct) => {
        // analysis contains: LocalSize, RemoteSize, LocalModified, RemoteModified,
        // DetectedNewer, Recommendation, ReasonForRecommendation
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

### Current API Gaps (To Be Resolved in v1.0)

| Gap | Impact | Status |
|-----|--------|--------|
| Single-threaded engine | One sync at a time per instance | By design - create separate instances if needed |
| OCIS TUS not implemented | Falls back to generic upload | Planned for v1.0 |

### ✅ Resolved API Gaps

| Feature | Implementation |
|---------|----------------|
| Bandwidth throttling | `SyncOptions.MaxBytesPerSecond` - limits transfer rate |
| Virtual file awareness | `SyncOptions.VirtualFileCallback` - hook for Windows Cloud Files API integration |
| Pause/Resume sync | `PauseAsync()` / `ResumeAsync()` - gracefully pause and resume long-running syncs |
| Selective folder sync | `SyncFolderAsync(path)` - sync specific folder without full scan |
| Selective file sync | `SyncFilesAsync(paths)` - sync specific files on demand |
| Incremental change notification | `NotifyLocalChangeAsync(path, changeType)` - accept FileSystemWatcher events |
| Batch change notification | `NotifyLocalChangesAsync(changes)` - efficient batch FileSystemWatcher events |
| Rename tracking | `NotifyLocalRenameAsync(oldPath, newPath)` - proper rename operation tracking |
| Pending operations query | `GetPendingOperationsAsync()` - inspect sync queue for UI display |
| Clear pending changes | `ClearPendingChanges()` - discard pending notifications without syncing |
| GetSyncPlanAsync integration | `GetSyncPlanAsync()` now incorporates pending changes from notifications |
| Activity history | `GetRecentOperationsAsync()` - query completed operations for activity feed |
| History cleanup | `ClearOperationHistoryAsync()` - purge old operation records |

### Required SharpSync API Additions (v1.0)

These APIs are required for v1.0 release to support Nimbus desktop client:

**Protocol Support:**
1. OCIS TUS protocol implementation (`WebDavStorage.cs:547` currently falls back)

**Progress & History:**
2. Per-file progress events (currently only per-sync-operation)

**✅ Completed:**
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
- `NotifyLocalChangesAsync(changes)` - Batch change notification for efficient FileSystemWatcher handling
- `NotifyLocalRenameAsync(oldPath, newPath)` - Proper rename operation tracking with old/new paths
- `GetPendingOperationsAsync()` - Inspect sync queue for UI display
- `ClearPendingChanges()` - Discard pending notifications without syncing
- `GetSyncPlanAsync()` integration - Now incorporates pending changes from notifications
- `ChangeType` enum - Represents FileSystemWatcher change types (Created, Changed, Deleted, Renamed)
- `PendingOperation` model - Represents operations waiting in sync queue with rename tracking

### API Readiness Score for Nimbus

**Current State (Pre-v1.0):**

| Component | Score | Notes |
|-----------|-------|-------|
| Core sync engine | 9/10 | Production-ready, well-tested |
| Nextcloud WebDAV | 8/10 | Missing OCIS TUS protocol |
| OAuth2 abstraction | 9/10 | Clean interface, Nimbus implements |
| UI binding (events) | 9/10 | Excellent progress/conflict events |
| Conflict resolution | 9/10 | Rich analysis, extensible callbacks |
| Selective sync | 10/10 | Complete: folder/file/incremental sync, batch notifications, rename tracking |
| Pause/Resume | 10/10 | Fully implemented with graceful pause points |
| Desktop integration hooks | 10/10 | Virtual file callback, bandwidth throttling, pause/resume, pending operations |

**Current Overall: 9.3/10** - Strong foundation with comprehensive desktop client APIs

**Target for v1.0: 9.5/10** - OCIS TUS and per-file progress remaining

## Version 1.0 Release Readiness

### Current Status: ~75% Complete

The core library is production-ready, but several critical items must be addressed before v1.0 release.

### ✅ What's Complete and Production-Ready

**Core Architecture**
- Interface-based design (`ISyncEngine`, `ISyncStorage`, `ISyncDatabase`, `IConflictResolver`, `ISyncFilter`)
- Domain models with comprehensive XML documentation
- Well-tested core components (21 test files, ~4,493 lines)

**Implementations**
- `SyncEngine` - 1,104 lines of production-ready sync logic with three-phase optimization
- `LocalFileStorage` - Fully implemented and tested (557 lines of tests)
- `SftpStorage` - Fully implemented with password/key auth and tested (650+ lines of tests)
- `SqliteSyncDatabase` - Complete with transaction support and tests
- `SmartConflictResolver` - Intelligent conflict analysis with tests
- `DefaultConflictResolver` - Strategy-based resolution with tests
- `SyncFilter` - Pattern-based filtering with tests
- `WebDavStorage` - 812 lines implemented with OAuth2, chunking, platform optimizations

**Infrastructure**
- Clean solution structure
- `.editorconfig` with comprehensive C# style rules
- Basic CI/CD pipeline (build, format check, test on Ubuntu)

### 🚨 CRITICAL (Must Fix Before v1.0)

1. **README.md Completely Wrong** ❌
   - **Issue**: README describes a native CSync wrapper with incorrect API examples
   - **Current**: Shows `new SyncEngine()` with simple two-path sync
   - **Reality**: Requires `ISyncStorage` implementations, database, and complex setup
   - **Impact**: Users will be completely confused about what this library does
   - **Fix**: Complete rewrite matching actual architecture
   - **File**: `/home/user/sharp-sync/README.md:1-409`

2. **~~False SFTP Advertising~~** ✅ **FIXED**
   - **Status**: SFTP is now fully implemented with comprehensive tests
   - **Implementation**: `SftpStorage` class with password and key-based authentication
   - **Tests**: 650+ lines of unit and integration tests
   - **SSH.NET dependency**: Now properly utilized (version 2025.1.0)
   - **Result**: Package metadata is now accurate

3. **WebDavStorage Completely Untested** ❌
   - **Issue**: 812 lines of critical WebDAV code has zero test coverage
   - **Components**: OAuth2 auth, chunked uploads, Nextcloud optimizations, retry logic
   - **Impact**: Cannot release enterprise-grade library with untested core component
   - **Fix**: Create comprehensive `WebDavStorageTests.cs`
   - **File**: `/home/user/sharp-sync/src/SharpSync/Storage/WebDavStorage.cs:1-812`

### ⚠️ HIGH PRIORITY (Should Fix for v1.0)

4. **Missing Samples Directory**
   - **Issue**: Referenced in project structure but doesn't exist
   - **Expected**: `samples/Console.Sync.Sample` with working code samples
   - **Impact**: No practical guidance for new users
   - **Fix**: Create samples directory with at least one complete example
   - **Effort**: 1-2 hours

5. **CI Only Runs on Ubuntu**
   - **Issue**: `.github/workflows/dotnet.yml:15` uses `runs-on: ubuntu-latest` only
   - **Claim**: CLAUDE.md previously claimed multi-platform testing (now fixed)
   - **Impact**: No verification that library works on Windows/macOS
   - **Fix**: Add matrix strategy for ubuntu-latest, windows-latest, macos-latest
   - **Effort**: 30 minutes

6. **No Integration Tests**
   - **Issue**: Only unit tests with mocks exist
   - **Missing**: Real WebDAV server tests, end-to-end sync scenarios
   - **Impact**: No verification of real-world behavior
   - **Fix**: Add integration test suite (can use Docker for WebDAV server)
   - **Effort**: 4-8 hours

### 📋 MEDIUM PRIORITY (Nice to Have for v1.0)

7. **No Code Coverage Reporting**
    - Add coverlet/codecov integration to CI pipeline
    - Track and display test coverage badge

8. **~~SSH.NET Dependency Unused~~** ✅ **FIXED**
    - SSH.NET is now fully utilized by SftpStorage implementation
    - Dependency is justified and necessary for SFTP support

9. **No Concrete OAuth2Provider Example**
    - While intentionally UI-free, a console example would help users
    - Show how to implement `IOAuth2Provider` for different platforms

### 🔄 CAN DEFER TO v1.1+

10. **~~SFTP~~/~~FTP~~/~~S3~~ Implementations** ✅ **ALL DONE!**
    - ✅ SFTP now fully implemented with comprehensive tests
    - ✅ FTP/FTPS now fully implemented with comprehensive tests
    - ✅ S3 now fully implemented with comprehensive tests and LocalStack integration
    - All major storage backends are now complete!

11. **Performance Benchmarks**
    - BenchmarkDotNet suite for sync operations
    - Helps track performance regressions

12. **Additional Conflict Resolvers**
    - Timestamp-based, size-based, hash-based strategies
    - Current resolvers are sufficient for v1.0

### 📅 Recommended Release Timeline

**Week 1: Critical Fixes**
- [ ] Rewrite README.md with correct API documentation and examples
**Week 2: Testing & CI**
- [ ] Write comprehensive WebDavStorage tests (minimum 70% coverage)
- [ ] Add multi-platform CI matrix (Ubuntu, Windows, macOS)
- [ ] Add basic integration tests for WebDAV sync scenarios

**Week 3: Examples & Polish**
- [ ] Create examples directory with at least 2 working samples:
  - Basic local-to-WebDAV sync
  - Advanced usage with OAuth2, conflict resolution, and filtering
- [ ] Code review and documentation polish
- [ ] Final end-to-end testing on all platforms

**Week 4: Release v1.0** 🚀
- [ ] Tag v1.0.0
- [ ] Publish to NuGet
- [ ] Update project documentation
- [ ] Announce release

### 📊 Quality Metrics for v1.0

**Minimum Acceptance Criteria:**
- ✅ Core sync engine tested (achieved)
- ⚠️ All storage implementations tested (LocalFileStorage ✅, SftpStorage ✅, FtpStorage ✅, S3Storage ✅, WebDavStorage ❌)
- ❌ README matches actual API (completely wrong)
- ✅ No TODOs/FIXMEs in code (achieved)
- ✅ Examples directory exists (created)
- ✅ Package metadata accurate (SFTP, FTP, and S3 now implemented!)
- ✅ Integration test infrastructure (Docker-based CI testing for SFTP, FTP, and S3)

**Current Score: 5/9 (56%)** - S3 implementation complete!

### 🎯 v1.0 Roadmap (Pre-Release)

**✅ Completed**
- ✅ SFTP storage implementation
- ✅ FTP/FTPS storage implementation
- ✅ S3 storage implementation with AWS S3 and S3-compatible services
- ✅ Integration test infrastructure with Docker for SFTP, FTP, and S3/LocalStack
- ✅ Bandwidth throttling (`SyncOptions.MaxBytesPerSecond`)
- ✅ Virtual file placeholder support (`SyncOptions.VirtualFileCallback`) for Windows Cloud Files API
- ✅ High-performance logging with `Microsoft.Extensions.Logging.Abstractions`
- ✅ Pause/Resume sync (`PauseAsync()` / `ResumeAsync()`) with graceful pause points
- ✅ Selective folder sync (`SyncFolderAsync(path)`) - Sync specific folder without full scan
- ✅ Selective file sync (`SyncFilesAsync(paths)`) - Sync specific files on demand
- ✅ Incremental change notification (`NotifyLocalChangeAsync(path, changeType)`) - FileSystemWatcher integration
- ✅ Batch change notification (`NotifyLocalChangesAsync(changes)`) - Efficient batch FileSystemWatcher events
- ✅ Rename tracking (`NotifyLocalRenameAsync(oldPath, newPath)`) - Proper rename operation tracking
- ✅ Pending operations query (`GetPendingOperationsAsync()`) - Inspect sync queue for UI display
- ✅ Clear pending changes (`ClearPendingChanges()`) - Discard pending without syncing
- ✅ `GetSyncPlanAsync()` integration with pending changes from notifications
- ✅ `ChangeType` enum for FileSystemWatcher change types
- ✅ `PendingOperation` model for sync queue inspection with rename tracking support

**🚧 Required for v1.0 Release**

Documentation & Testing:
- [ ] Rewrite README.md with correct API documentation
- [ ] WebDavStorage integration tests
- [ ] Multi-platform CI testing (Windows, macOS)
- [ ] Code coverage reporting
- [x] Examples directory with working samples ✅

Desktop Client APIs (for Nimbus):
- [ ] OCIS TUS protocol implementation (currently falls back to generic upload at `WebDavStorage.cs:547`)
- [ ] Per-file progress events (currently only per-sync-operation)
- [x] `GetRecentOperationsAsync()` - Operation history for activity feed ✅

Performance & Polish:
- [ ] Performance benchmarks with BenchmarkDotNet
- [ ] Advanced filtering (regex support)
