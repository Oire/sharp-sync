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
Integration tests require external services (SFTP server). Use the provided scripts:

```bash
# Linux/macOS - automatically starts Docker SFTP server
./scripts/run-integration-tests.sh

# Windows - automatically starts Docker SFTP server
.\scripts\run-integration-tests.ps1

# Or manually with Docker Compose
docker-compose -f docker-compose.test.yml up -d
export SFTP_TEST_HOST=localhost SFTP_TEST_PORT=2222 SFTP_TEST_USER=testuser SFTP_TEST_PASS=testpass SFTP_TEST_ROOT=/home/testuser/upload
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
- Includes SFTP and FTP integration tests using Docker-based servers
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
   - `StorageType` enum includes: S3 (planned for future versions)

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

- **Multi-Protocol Support**: Local, WebDAV, SFTP, and FTP/FTPS storage (extensible to S3)
- **OAuth2 Authentication**: Full OAuth2 flow support without UI dependencies (WebDAV)
- **SSH Key & Password Auth**: Secure SFTP authentication with private keys or passwords
- **FTP/FTPS Support**: Plain FTP, explicit FTPS, and implicit FTPS with password authentication
- **Smart Conflict Resolution**: Rich conflict analysis for UI integration
- **Selective Sync**: Include/exclude patterns for files and folders
- **Progress Reporting**: Real-time progress events for UI binding
- **Large File Support**: Chunked uploads with platform-specific optimizations
- **Network Resilience**: Retry logic and error handling with automatic reconnection
- **Parallel Processing**: Configurable parallelism with intelligent prioritization

### Dependencies

- `sqlite-net-pcl` (1.9.172) - SQLite database
- `SQLitePCLRaw.bundle_e_sqlite3` (3.0.2) - SQLite native binaries
- `WebDav.Client` (2.9.0) - WebDAV protocol
- `SSH.NET` (2025.1.0) - SFTP protocol implementation
- `FluentFTP` (52.0.2) - FTP/FTPS protocol implementation
- Target Framework: .NET 8.0

### Platform-Specific Optimizations

- **Nextcloud**: Native chunking v2 API support
- **OCIS**: TUS protocol preparation
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

10. **~~SFTP~~/~~FTP~~/S3 Implementations** (SFTP ✅ DONE, FTP ✅ DONE)
    - ✅ SFTP now fully implemented with comprehensive tests
    - ✅ FTP/FTPS now fully implemented with comprehensive tests
    - S3 remains future feature for v1.1+
    - StorageType enum prepared for future S3 implementation

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
- ⚠️ All storage implementations tested (LocalFileStorage ✅, SftpStorage ✅, FtpStorage ✅, WebDavStorage ❌)
- ❌ README matches actual API (completely wrong)
- ✅ No TODOs/FIXMEs in code (achieved)
- ❌ Examples directory exists (missing)
- ✅ Package metadata accurate (SFTP and FTP now implemented!)
- ✅ Integration test infrastructure (Docker-based CI testing for SFTP and FTP)

**Current Score: 5/9 (56%)** - Improved from 33%! (FTP implementation complete)

### 🎯 Post-v1.0 Roadmap (Future Versions)

**v1.0** ✅ SFTP and FTP Implemented!
- ✅ SFTP storage implementation (DONE!)
- ✅ FTP/FTPS storage implementation (DONE!)
- ✅ Integration test infrastructure with Docker for SFTP and FTP (DONE!)

**v1.1**
- Code coverage reporting
- Performance benchmarks
- Multi-platform CI testing (Windows, macOS)
- Additional conflict resolution strategies

**v1.2**
- S3-compatible storage implementation
- Advanced filtering (regex support)

**v2.0**
- Breaking changes if needed
- API improvements based on user feedback
- Performance optimizations
