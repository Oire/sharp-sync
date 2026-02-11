# SharpSync Examples

This directory contains example code demonstrating how to use the SharpSync library.

## BasicSyncExample.cs

A comprehensive example showing:

- **Basic sync setup** - Creating storage, database, filter, and sync engine
- **Progress events** - Wiring up UI updates during sync
- **Sync preview** - Previewing changes before executing
- **Activity history** - Using `GetRecentOperationsAsync()` to display sync history
- **FileSystemWatcher integration** - Real-time change detection
- **Selective sync** - Syncing specific files or folders on demand
- **Pause/Resume** - Controlling long-running sync operations
- **Bandwidth throttling** - Limiting transfer speeds
- **Sync options** - Configuring `ChecksumOnly`, `SizeOnly`, `PreserveTimestamps`, `PreservePermissions`, `FollowSymlinks`, `ExcludePatterns`, `TimeoutSeconds`, `UpdateExisting`, `ConflictResolution` override, and `Verbose` logging
- **Smart conflict resolution** - Handling conflicts with UI prompts

## ConsoleOAuth2Example.cs

A reference implementation of `IOAuth2Provider` for console/headless applications:

- **Browser-based OAuth2 flow** - Opens the system browser and listens on localhost for the callback
- **Authorization code exchange** - Exchanges the code for access and refresh tokens
- **Token refresh** - Refreshing expired tokens using the refresh token
- **Token validation** - Checking token validity before API calls
- **Nextcloud integration** - End-to-end example connecting to Nextcloud via WebDAV with OAuth2
- **Cross-platform browser launch** - Works on Windows, macOS, and Linux

## Usage

This is a standalone example file, not a buildable project. To use it:

1. Create a new .NET 8.0+ project
2. Add the SharpSync NuGet package:
   ```bash
   dotnet add package Oire.SharpSync
   ```
3. Optionally add logging:
   ```bash
   dotnet add package Microsoft.Extensions.Logging.Console
   ```
4. Copy the relevant code from `BasicSyncExample.cs` into your project

## Storage Options

SharpSync supports multiple storage backends:

| Storage | Class | Use Case |
|---------|-------|----------|
| Local filesystem | `LocalFileStorage` | Local folders, testing |
| WebDAV | `WebDavStorage` | Nextcloud, ownCloud, any WebDAV server |
| SFTP | `SftpStorage` | SSH/SFTP servers |
| FTP/FTPS | `FtpStorage` | FTP servers with optional TLS |
| S3 | `S3Storage` | AWS S3, MinIO, LocalStack, S3-compatible |

## Quick Start

```csharp
using Oire.SharpSync.Core;
using Oire.SharpSync.Database;
using Oire.SharpSync.Storage;
using Oire.SharpSync.Sync;

// Create storage instances
var localStorage = new LocalFileStorage("/local/path");
var remoteStorage = new SftpStorage("sftp.example.com", "user", "password", "/remote/path");

// Create database
var database = new SqliteSyncDatabase("/path/to/sync.db");
await database.InitializeAsync();

// Create sync engine
var filter = new SyncFilter();
var resolver = new DefaultConflictResolver(ConflictResolution.UseRemote);
using var engine = new SyncEngine(localStorage, remoteStorage, database, resolver, filter);

// Run sync
var result = await engine.SynchronizeAsync();
Console.WriteLine($"Synced {result.FilesSynchronized} files");

// View activity history
var history = await engine.GetRecentOperationsAsync(limit: 20);
foreach (var op in history) {
    Console.WriteLine($"{op.ActionType}: {op.Path} ({op.Duration.TotalSeconds:F1}s)");
}
```
