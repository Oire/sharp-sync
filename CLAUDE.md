# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Common Development Commands

### Building the Project
```bash
# Build the entire solution
dotnet build src/SharpSync.sln

# Build in Release mode
dotnet build src/SharpSync.sln --configuration Release

# Clean and rebuild
dotnet clean src/SharpSync.sln
dotnet build src/SharpSync.sln
```

### Running Tests
```bash
# Run all tests
dotnet test src/SharpSync.sln

# Run tests with verbose output
dotnet test src/SharpSync.sln --verbosity normal

# Run tests for a specific project
dotnet test src/SharpSync.Tests/SharpSync.Tests.csproj

# Run tests with test results output (TRX format)
dotnet test src/SharpSync.sln --logger trx --results-directory TestResults
```

### Creating NuGet Package
```bash
# Create NuGet package
dotnet pack --configuration Release

# Pack specific project with output directory
dotnet pack src/SharpSync/SharpSync.csproj --configuration Release --output ./artifacts
```

### CI/CD Pipeline Commands
The project uses GitHub Actions for CI/CD. The pipeline automatically:
- Builds on Ubuntu, Windows, and macOS
- Runs tests on all platforms
- Creates NuGet packages on successful builds to main/master branch

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
   - `LocalFileStorage` - Local filesystem operations
   - `WebDavStorage` - WebDAV with OAuth2, chunking, and platform-specific optimizations
   - Ready for: SFTP, FTP, S3 implementations

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

- **Multi-Protocol Support**: Local, WebDAV, SFTP (extensible to FTP, S3)
- **OAuth2 Authentication**: Full OAuth2 flow support without UI dependencies
- **Smart Conflict Resolution**: Rich conflict analysis for UI integration
- **Selective Sync**: Include/exclude patterns for files and folders
- **Progress Reporting**: Real-time progress events for UI binding
- **Large File Support**: Chunked uploads with platform-specific optimizations
- **Network Resilience**: Retry logic and error handling
- **Parallel Processing**: Configurable parallelism with intelligent prioritization

### Dependencies

- `sqlite-net-pcl` (1.9.172) - SQLite database
- `SQLitePCLRaw.bundle_e_sqlite3` (2.1.8) - SQLite native binaries
- `WebDav.Client` (2.8.0) - WebDAV protocol
- `SSH.NET` (2023.0.1) - SSH/SFTP support
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
├── src/
│   ├── SharpSync.sln         # Solution file
│   ├── SharpSync/            # Main library project
│   │   ├── Auth/             # OAuth2 authentication
│   │   ├── Core/             # Interfaces and models
│   │   ├── Database/         # State persistence
│   │   ├── Storage/          # Storage backends
│   │   └── Sync/             # Sync engine
│   └── SharpSync.Tests/      # Unit tests
│       ├── Core/
│       ├── Database/
│       ├── Storage/
│       └── Sync/
├── examples/
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