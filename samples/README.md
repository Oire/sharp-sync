# SharpSync Samples

This directory contains sample applications demonstrating how to use the SharpSync library.

## Available Samples

### 1. SharpSync.Samples.Console

A comprehensive console application that demonstrates most SharpSync features in an interactive menu-driven interface.

**Features demonstrated:**

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
- **OAuth2 authentication** - Browser-based OAuth2 flow for Nextcloud/OCIS

**To run:**
```bash
cd samples/SharpSync.Samples.Console
dotnet run
```

The application will present an interactive menu with options to:
1. Run a basic local-to-local sync demo using temporary directories
2. View an overview of all available `SyncOptions`
3. Run the OAuth2 Nextcloud sync example (requires a live Nextcloud server)

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

## Adding Your Own Samples

Feel free to contribute additional samples demonstrating specific use cases:

- Desktop application integration (WPF/WinUI/Avalonia)
- Background service for scheduled sync
- S3 or SFTP sync workflows
- Custom conflict resolution strategies
