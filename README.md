# SharpSync

A high-performance .NET wrapper around [CSync](https://csync.org), providing bi-directional file synchronization capabilities with conflict resolution and progress reporting.

## Features

- **High-level C# API** - Clean, async-friendly interface that wraps all CSync functionality
- **Comprehensive error handling** - Typed exceptions with detailed error information
- **Progress reporting** - Real-time progress updates during synchronization
- **Conflict resolution** - Multiple strategies for handling file conflicts
- **Asynchronous operations** - Full async/await support with cancellation tokens
- **NuGet ready** - Ready-to-publish NuGet package with proper metadata
- **Cross-platform** - Works on Windows, Linux, and macOS
- **Extensive documentation** - Complete XML documentation for IntelliSense

## Installation

### From NuGet (when published)
```bash
dotnet add package SharpSync
```

### Building from Source
```bash
git clone https://github.com/Oire/sharp-sync.git
cd sharp-sync
dotnet build
```

## Quick Start

### Basic Synchronization

```csharp
using SharpSync;

// Create a sync engine
using var syncEngine = new SyncEngine();

// Configure sync options
var options = new SyncOptions
{
    PreserveTimestamps = true,
    DeleteExtraneous = false,
    ConflictResolution = ConflictResolution.Ask
};

// Synchronize directories
var result = await syncEngine.SynchronizeAsync(
    sourcePath: "/path/to/source",
    targetPath: "/path/to/target",
    options: options
);

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
using var syncEngine = new SyncEngine();

// Subscribe to progress events
syncEngine.ProgressChanged += (sender, progress) =>
{
    Console.WriteLine($"Progress: {progress.Percentage:F1}% - {progress.CurrentFileName}");
};

var result = await syncEngine.SynchronizeAsync("/source", "/target");
```

### With Conflict Handling

```csharp
using var syncEngine = new SyncEngine();

// Handle conflicts manually
syncEngine.ConflictDetected += (sender, conflict) =>
{
    Console.WriteLine($"Conflict: {conflict.SourcePath} vs {conflict.TargetPath}");
    
    // Resolve conflict (Ask user, use source, use target, skip, or merge)
    conflict.Resolution = ConflictResolution.UseSource;
};

var result = await syncEngine.SynchronizeAsync("/source", "/target");
```

## API Reference

### SyncEngine

The main class for performing file synchronization operations.

#### Methods

- `SynchronizeAsync(string sourcePath, string targetPath, SyncOptions? options = null, CancellationToken cancellationToken = default)` - Asynchronously synchronizes files between directories
- `Synchronize(string sourcePath, string targetPath, SyncOptions? options = null)` - Synchronously synchronizes files between directories
- `Dispose()` - Releases all resources

#### Events

- `ProgressChanged` - Raised to report synchronization progress
- `ConflictDetected` - Raised when a file conflict is detected

#### Properties

- `IsSynchronizing` - Gets whether the engine is currently synchronizing
- `LibraryVersion` - Gets the CSync library version (static)

### SyncOptions

Configuration options for synchronization operations.

```csharp
var options = new SyncOptions
{
    PreservePermissions = true,     // Preserve file permissions
    PreserveTimestamps = true,      // Preserve file timestamps
    FollowSymlinks = false,         // Follow symbolic links
    DryRun = false,                 // Perform a dry run (no changes)
    Verbose = false,                // Enable verbose logging
    ChecksumOnly = false,           // Use checksum-only comparison
    SizeOnly = false,               // Use size-only comparison
    DeleteExtraneous = false,       // Delete files not in source
    UpdateExisting = true,          // Update existing files
    ConflictResolution = ConflictResolution.Ask  // Conflict resolution strategy
};
```

### ConflictResolution

Strategies for resolving file conflicts:

- `Ask` - Ask for user input when conflicts occur (default)
- `UseSource` - Always use the source file
- `UseTarget` - Always use the target file
- `Skip` - Skip conflicted files
- `Merge` - Attempt to merge files when possible

### SyncResult

Contains the results of a synchronization operation:

```csharp
public class SyncResult
{
    public bool Success { get; }                // Whether sync was successful
    public long FilesSynchronized { get; }      // Number of files synchronized
    public long FilesSkipped { get; }           // Number of files skipped
    public long FilesConflicted { get; }        // Number of files with conflicts
    public long FilesDeleted { get; }           // Number of files deleted
    public TimeSpan ElapsedTime { get; }        // Total elapsed time
    public Exception? Error { get; }            // Any error that occurred
    public string Details { get; }              // Additional details
    public long TotalFilesProcessed { get; }    // Total files processed
}
```

### SyncProgress

Progress information during synchronization:

```csharp
public class SyncProgress
{
    public long CurrentFile { get; }        // Current file number
    public long TotalFiles { get; }         // Total number of files
    public string CurrentFileName { get; }  // Current filename being processed
    public double Percentage { get; }       // Progress percentage (0-100)
    public bool IsCancelled { get; }        // Whether operation was cancelled
}
```

## Exception Handling

SharpSync provides typed exceptions for different error conditions:

```csharp
try
{
    var result = await syncEngine.SynchronizeAsync("/source", "/target");
}
catch (InvalidPathException ex)
{
    Console.WriteLine($"Invalid path: {ex.Path} - {ex.Message}");
}
catch (PermissionDeniedException ex)
{
    Console.WriteLine($"Permission denied: {ex.Path} - {ex.Message}");
}
catch (FileConflictException ex)
{
    Console.WriteLine($"File conflict: {ex.SourcePath} vs {ex.TargetPath}");
}
catch (SyncException ex)
{
    Console.WriteLine($"Sync error ({ex.ErrorCode}): {ex.Message}");
}
```

### Exception Types

- `SyncException` - Base exception for all sync operations
- `InvalidPathException` - Invalid file or directory path
- `PermissionDeniedException` - Access denied to file or directory
- `FileConflictException` - File conflict detected during sync
- `FileNotFoundException` - Required file not found

## Advanced Usage

### Cancellation Support

```csharp
using var cts = new CancellationTokenSource();

// Cancel after 30 seconds
cts.CancelAfter(TimeSpan.FromSeconds(30));

try
{
    var result = await syncEngine.SynchronizeAsync(
        "/source", 
        "/target", 
        cancellationToken: cts.Token
    );
}
catch (OperationCanceledException)
{
    Console.WriteLine("Synchronization was cancelled");
}
```

### Custom Progress Tracking

```csharp
var progressReporter = new Progress<SyncProgress>(progress =>
{
    var percentage = progress.Percentage;
    var fileName = Path.GetFileName(progress.CurrentFileName);
    
    Console.WriteLine($"[{percentage:F1}%] {fileName}");
    
    // Update UI, save to file, etc.
});

syncEngine.ProgressChanged += (s, p) => progressReporter.Report(p);
```

### Batch Operations

```csharp
var syncPairs = new[]
{
    ("/source1", "/target1"),
    ("/source2", "/target2"),
    ("/source3", "/target3")
};

var results = new List<SyncResult>();

foreach (var (source, target) in syncPairs)
{
    var result = await syncEngine.SynchronizeAsync(source, target);
    results.Add(result);
    
    if (!result.Success)
    {
        Console.WriteLine($"Failed to sync {source} -> {target}: {result.Error?.Message}");
    }
}

var totalSynced = results.Sum(r => r.FilesSynchronized);
Console.WriteLine($"Total files synchronized: {totalSynced}");
```

## Requirements

- .NET 8.0 or later
- CSync library installed on the system
  - On Ubuntu/Debian: `sudo apt-get install csync`
  - On CentOS/RHEL: `sudo yum install csync`
  - On macOS: `brew install csync`
  - On Windows: Download from [csync.org](https://csync.org)

## Building and Testing

```bash
# Build the solution
dotnet build

# Run tests
dotnet test

# Create NuGet package
dotnet pack --configuration Release
```

## Performance Considerations

- Use `SizeOnly = true` for faster comparisons when file content rarely changes
- Set `ChecksumOnly = true` for more accurate comparisons when timestamps are unreliable
- Use `DryRun = true` to preview changes before actual synchronization
- Enable `Verbose = true` only for debugging as it impacts performance

## Thread Safety

`SyncEngine` instances are **not thread-safe**. Each thread should use its own `SyncEngine` instance. However, you can safely run multiple synchronization operations in parallel using different `SyncEngine` instances.

## Contributing

1. Fork the repository
2. Create a feature branch
3. Make your changes
4. Add tests for new functionality
5. Ensure all tests pass
6. Submit a pull request

## License

This project is licensed under the Apache License 2.0 - see the [LICENSE](LICENSE) file for details.

## Acknowledgments

- [CSync](https://csync.org) - The underlying C library that powers this wrapper
- .NET Community - For the excellent P/Invoke and async patterns
