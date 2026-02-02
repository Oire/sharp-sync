[![Build](https://github.com/Oire/sharp-sync/actions/workflows/dotnet.yml/badge.svg)](https://github.com/Oire/sharp-sync/actions/workflows/dotnet.yml)
[![codecov](https://codecov.io/gh/Oire/sharp-sync/graph/badge.svg)](https://codecov.io/gh/Oire/sharp-sync)

# SharpSync

A pure .NET file synchronization library supporting multiple storage backends with bidirectional sync, conflict resolution, and progress reporting. No native dependencies required.

## Features

- **Multi-Protocol Support**: Local filesystem, WebDAV, SFTP, FTP/FTPS, and Amazon S3 (including S3-compatible services)
- **Bidirectional Sync**: Full two-way synchronization with intelligent change detection
- **Conflict Resolution**: Pluggable strategies with rich conflict analysis for UI integration
- **Selective Sync**: Include/exclude patterns, folder-level sync, and on-demand file sync
- **Progress Reporting**: Real-time progress events for UI binding
- **Pause/Resume**: Gracefully pause and resume long-running sync operations
- **Bandwidth Throttling**: Configurable transfer rate limits
- **FileSystemWatcher Integration**: Built-in support for incremental sync via change notifications
- **Virtual File Support**: Callback hooks for Windows Cloud Files API placeholder integration
- **Activity History**: Query completed operations for activity feeds
- **Cross-Platform**: Works on Windows, Linux, and macOS (.NET 8.0+)

## Installation

### From NuGet

```bash
dotnet add package Oire.SharpSync
```

### Building from Source

```bash
git clone https://github.com/Oire/sharp-sync.git
cd sharp-sync
dotnet build
```

## Quick Start

### Basic Local-to-WebDAV Sync

```csharp
using Oire.SharpSync.Core;
using Oire.SharpSync.Database;
using Oire.SharpSync.Storage;
using Oire.SharpSync.Sync;

// 1. Create storage backends
var localStorage = new LocalFileStorage("/path/to/local/folder");
var remoteStorage = new WebDavStorage(
    "https://cloud.example.com/remote.php/dav/files/user/",
    username: "user",
    password: "password"
);

// 2. Create sync state database
var database = new SqliteSyncDatabase("/path/to/sync.db");

// 3. Create filter and conflict resolver
var filter = SyncFilter.CreateDefault(); // Excludes .git, node_modules, etc.
var conflictResolver = new DefaultConflictResolver(ConflictResolution.UseRemote);

// 4. Create sync engine
using var engine = new SyncEngine(
    localStorage,
    remoteStorage,
    database,
    filter,
    conflictResolver
);

// 5. Run synchronization
var result = await engine.SynchronizeAsync();

if (result.Success)
{
    Console.WriteLine($"Synchronized {result.FilesSynchronized} files");
}
else
{
    Console.WriteLine($"Sync failed: {result.Error?.Message}");
}
```

### With Progress Reporting

```csharp
// Item-level progress (overall sync progress)
engine.ProgressChanged += (sender, e) =>
{
    Console.WriteLine($"[{e.Progress.Percentage:F1}%] {e.Progress.CurrentItem}");
    Console.WriteLine($"  {e.Progress.ProcessedItems}/{e.Progress.TotalItems} items");
};

// Per-file byte-level progress (individual file transfer progress)
engine.FileProgressChanged += (sender, e) =>
{
    Console.WriteLine($"  {e.Operation}: {e.Path} - {e.PercentComplete}% ({e.BytesTransferred}/{e.TotalBytes} bytes)");
};

var result = await engine.SynchronizeAsync();
```

### With Conflict Handling

```csharp
// Option 1: Use SmartConflictResolver with a callback for UI integration
var resolver = new SmartConflictResolver(
    conflictHandler: async (analysis, ct) =>
    {
        // analysis contains: LocalSize, RemoteSize, LocalModified, RemoteModified,
        // DetectedNewer, Recommendation, ReasonForRecommendation
        Console.WriteLine($"Conflict: {analysis.Path}");
        Console.WriteLine($"  Local: {analysis.LocalModified}, Remote: {analysis.RemoteModified}");
        Console.WriteLine($"  Recommendation: {analysis.Recommendation}");

        // Return user's choice
        return analysis.Recommendation;
    },
    defaultResolution: ConflictResolution.Ask
);

// Option 2: Handle via event
engine.ConflictDetected += (sender, e) =>
{
    Console.WriteLine($"Conflict detected: {e.Path}");
    // The resolver will be called to determine resolution
};
```

## Storage Backends

### Local File System

```csharp
var storage = new LocalFileStorage("/path/to/folder");
```

### WebDAV (Nextcloud, ownCloud, etc.)

```csharp
// Basic authentication
var storage = new WebDavStorage(
    "https://cloud.example.com/remote.php/dav/files/user/",
    username: "user",
    password: "password",
    rootPath: "Documents"  // Optional subfolder
);

// OAuth2 authentication (for desktop apps)
var storage = new WebDavStorage(
    "https://cloud.example.com/remote.php/dav/files/user/",
    oauth2Provider: myOAuth2Provider,
    oauth2Config: myOAuth2Config
);
```

### SFTP

```csharp
// Password authentication
var storage = new SftpStorage(
    host: "sftp.example.com",
    port: 22,
    username: "user",
    password: "password",
    rootPath: "/home/user/sync"
);

// SSH key authentication
var storage = new SftpStorage(
    host: "sftp.example.com",
    port: 22,
    username: "user",
    privateKeyPath: "/path/to/id_rsa",
    privateKeyPassphrase: "optional-passphrase",
    rootPath: "/home/user/sync"
);
```

### FTP/FTPS

```csharp
// Plain FTP
var storage = new FtpStorage(
    host: "ftp.example.com",
    username: "user",
    password: "password"
);

// Explicit FTPS (TLS)
var storage = new FtpStorage(
    host: "ftp.example.com",
    username: "user",
    password: "password",
    useFtps: true
);

// Implicit FTPS
var storage = new FtpStorage(
    host: "ftp.example.com",
    port: 990,
    username: "user",
    password: "password",
    useFtps: true,
    useImplicitFtps: true
);
```

### Amazon S3 (and S3-Compatible Services)

```csharp
// AWS S3
var storage = new S3Storage(
    bucketName: "my-bucket",
    accessKey: "AKIAIOSFODNN7EXAMPLE",
    secretKey: "wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY",
    region: "us-east-1",
    prefix: "sync-folder/"  // Optional key prefix
);

// S3-compatible (MinIO, LocalStack, Backblaze B2, etc.)
var storage = new S3Storage(
    bucketName: "my-bucket",
    accessKey: "minioadmin",
    secretKey: "minioadmin",
    serviceUrl: "http://localhost:9000",  // Custom endpoint
    prefix: "backups/"
);
```

## Advanced Usage

### Preview Changes Before Sync

```csharp
var plan = await engine.GetSyncPlanAsync();

Console.WriteLine($"Downloads: {plan.Downloads.Count}");
Console.WriteLine($"Uploads: {plan.Uploads.Count}");
Console.WriteLine($"Deletes: {plan.Deletes.Count}");
Console.WriteLine($"Conflicts: {plan.Conflicts.Count}");

foreach (var action in plan.Downloads)
{
    Console.WriteLine($"  ↓ {action.Path} ({action.Size} bytes)");
}
```

### Selective Sync

```csharp
// Sync a specific folder
var result = await engine.SyncFolderAsync("Documents/Projects");

// Sync specific files
var result = await engine.SyncFilesAsync(new[]
{
    "report.docx",
    "data.xlsx"
});
```

### FileSystemWatcher Integration

```csharp
var watcher = new FileSystemWatcher(localPath);

watcher.Changed += async (s, e) =>
{
    var relativePath = Path.GetRelativePath(localPath, e.FullPath);
    await engine.NotifyLocalChangeAsync(relativePath, ChangeType.Changed);
};

watcher.Created += async (s, e) =>
{
    var relativePath = Path.GetRelativePath(localPath, e.FullPath);
    await engine.NotifyLocalChangeAsync(relativePath, ChangeType.Created);
};

watcher.Deleted += async (s, e) =>
{
    var relativePath = Path.GetRelativePath(localPath, e.FullPath);
    await engine.NotifyLocalChangeAsync(relativePath, ChangeType.Deleted);
};

watcher.Renamed += async (s, e) =>
{
    var oldPath = Path.GetRelativePath(localPath, e.OldFullPath);
    var newPath = Path.GetRelativePath(localPath, e.FullPath);
    await engine.NotifyLocalRenameAsync(oldPath, newPath);
};

watcher.EnableRaisingEvents = true;

// Check pending operations
var pending = await engine.GetPendingOperationsAsync();
Console.WriteLine($"{pending.Count} files waiting to sync");
```

### Pause and Resume

```csharp
// Start sync in background
var syncTask = engine.SynchronizeAsync();

// Pause when needed
await engine.PauseAsync();
Console.WriteLine($"Paused. State: {engine.State}");

// Resume later
await engine.ResumeAsync();

// Wait for completion
var result = await syncTask;
```

### Bandwidth Throttling

```csharp
var options = new SyncOptions
{
    MaxBytesPerSecond = 1_048_576  // 1 MB/s limit
};

var result = await engine.SynchronizeAsync(options);
```

### Activity History

```csharp
// Get recent operations
var recentOps = await engine.GetRecentOperationsAsync(limit: 50);

foreach (var op in recentOps)
{
    var icon = op.ActionType switch
    {
        SyncActionType.Upload => "↑",
        SyncActionType.Download => "↓",
        SyncActionType.DeleteLocal or SyncActionType.DeleteRemote => "×",
        _ => "?"
    };
    var status = op.Success ? "✓" : "✗";
    Console.WriteLine($"{status} {icon} {op.Path} ({op.Duration.TotalSeconds:F1}s)");
}

// Cleanup old history
var deleted = await engine.ClearOperationHistoryAsync(DateTime.UtcNow.AddDays(-30));
```

### Custom Filtering

```csharp
var filter = new SyncFilter();

// Exclude patterns
filter.AddExclusionPattern("*.tmp");
filter.AddExclusionPattern("*.log");
filter.AddExclusionPattern("node_modules");
filter.AddExclusionPattern(".git");
filter.AddExclusionPattern("**/*.bak");

// Include patterns (if set, only matching files are synced)
filter.AddInclusionPattern("Documents/**");
filter.AddInclusionPattern("*.docx");
```

### Sync Options

```csharp
var options = new SyncOptions
{
    PreservePermissions = true,      // Preserve file permissions
    PreserveTimestamps = true,       // Preserve modification times
    FollowSymlinks = false,          // Follow symbolic links
    DryRun = false,                  // Preview changes without applying
    DeleteExtraneous = false,        // Delete files not in source
    UpdateExisting = true,           // Update existing files
    ChecksumOnly = false,            // Use checksums instead of timestamps
    SizeOnly = false,                // Compare by size only
    ConflictResolution = ConflictResolution.Ask,
    TimeoutSeconds = 300,            // 5 minute timeout
    MaxBytesPerSecond = null,        // No bandwidth limit
    ExcludePatterns = new List<string> { "*.tmp", "~*" }
};
```

## Conflict Resolution Strategies

| Strategy | Description |
|----------|-------------|
| `Ask` | Invoke conflict handler callback (default) |
| `UseLocal` | Always keep the local version |
| `UseRemote` | Always use the remote version |
| `Skip` | Leave conflicted files unchanged |
| `RenameLocal` | Rename local file, download remote |
| `RenameRemote` | Rename remote file, upload local |

## Architecture

SharpSync uses a modular, interface-based architecture:

- **`ISyncEngine`** - Orchestrates synchronization between storages
- **`ISyncStorage`** - Storage backend abstraction (local, WebDAV, SFTP, FTP, S3)
- **`ISyncDatabase`** - Persists sync state for change detection
- **`IConflictResolver`** - Pluggable conflict resolution strategies
- **`ISyncFilter`** - File filtering for selective sync

### Thread Safety

Only one sync operation can run at a time per `SyncEngine` instance. However, the following members are **thread-safe** and can be called from any thread (including while a sync runs):

- **State properties**: `IsSynchronizing`, `IsPaused`, `State`
- **Change notifications**: `NotifyLocalChangeAsync()`, `NotifyLocalChangesAsync()`, `NotifyLocalRenameAsync()` - safe to call from FileSystemWatcher threads
- **Control methods**: `PauseAsync()`, `ResumeAsync()` - safe to call from UI thread
- **Query methods**: `GetPendingOperationsAsync()`, `GetRecentOperationsAsync()`, `ClearPendingChanges()`

This design supports typical desktop client integration where FileSystemWatcher events arrive on thread pool threads, sync runs on a background thread, and UI controls pause/resume from the main thread.

You can safely run multiple sync operations in parallel using **separate** `SyncEngine` instances.

## Requirements

- .NET 8.0 or later
- No native dependencies

## Dependencies

- `Microsoft.Extensions.Logging.Abstractions` - Logging abstraction
- `sqlite-net-pcl` - SQLite database for sync state
- `WebDav.Client` - WebDAV protocol
- `SSH.NET` - SFTP protocol
- `FluentFTP` - FTP/FTPS protocol
- `AWSSDK.S3` - Amazon S3 and S3-compatible storage

## Building and Testing

```bash
# Build the solution
dotnet build

# Run unit tests
dotnet test

# Run integration tests (requires Docker)
./scripts/run-integration-tests.sh   # Linux/macOS
.\scripts\run-integration-tests.ps1  # Windows

# Create NuGet package
dotnet pack --configuration Release
```

## Contributing

1. Fork the repository
2. Create a feature branch
3. Make your changes
4. Add tests for new functionality
5. Ensure all tests pass (`dotnet test`)
6. Ensure code formatting (`dotnet format --verify-no-changes`)
7. Submit a pull request

## License

This project is licensed under the Apache License 2.0 - see the [LICENSE](LICENSE) file for details.

## Acknowledgments

- [WebDav.Client](https://github.com/skazantsev/WebDavClient) - WebDAV protocol implementation
- [SSH.NET](https://github.com/sshnet/SSH.NET) - SFTP protocol implementation
- [FluentFTP](https://github.com/robinrodricks/FluentFTP) - FTP/FTPS protocol implementation
- [AWS SDK for .NET](https://github.com/aws/aws-sdk-net) - S3 protocol implementation
