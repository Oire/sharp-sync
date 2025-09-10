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

### Core Components

1. **SharpSync.dll** - Main library providing the .NET wrapper around CSync
   - `SyncEngine` (src/SharpSync/SyncEngine.cs) - High-level async-friendly API for file synchronization
   - `Native/CSyncNative` (src/SharpSync/Native/CSyncNative.cs) - P/Invoke declarations for CSync C library
   - Exception hierarchy in `Exceptions.cs` - Typed exceptions for different error conditions
   - `SyncOptions` - Configuration for sync operations
   - Progress and conflict handling infrastructure

2. **Native Dependency** - Requires CSync C library to be installed on the system
   - The library uses P/Invoke to call into the native CSync library
   - Library name: "csync" (loaded dynamically at runtime)

### Key Design Patterns

1. **Async-First Design**: All synchronization operations have async counterparts using Task-based patterns
2. **Event-Based Progress**: Progress reporting via events (`ProgressChanged`, `ConflictDetected`)
3. **Disposable Pattern**: `SyncEngine` implements IDisposable for proper resource cleanup
4. **Native Interop**: Uses P/Invoke with proper marshaling and GC handle management for callbacks

### Testing Strategy

- Uses xUnit as the test framework
- Test project references main project directly
- Tests cover:
  - Exception handling scenarios
  - Sync options validation
  - Progress reporting
  - Conflict resolution
  - Result data structures

### Important Considerations

1. **Thread Safety**: `SyncEngine` instances are NOT thread-safe. Each thread needs its own instance.
2. **Platform Dependencies**: Requires native CSync library installation (apt/yum/brew/manual)
3. **Memory Management**: Careful handling of native callbacks with GCHandle to prevent collection
4. **Error Handling**: Comprehensive exception hierarchy mapped from CSync error codes

## Project Structure
```
/
├── src/
│   ├── SharpSync.sln       # Solution file
│   ├── SharpSync/          # Main library project
│   │   ├── Native/         # P/Invoke declarations
│   │   └── *.cs           # Core implementation files
│   └── SharpSync.Tests/    # Unit tests
└── .github/
    └── workflows/          # CI/CD configuration
```