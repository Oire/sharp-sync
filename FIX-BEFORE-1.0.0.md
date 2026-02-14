# SharpSync v1.0.0 Pre-Release Audit

**Audited**: 2026-02-14
**Build**: Clean (0 warnings, 0 errors)
**Tests**: 1,636 passed, 0 failed, 260 skipped (integration tests require Docker)

---

## CRITICAL -- Must fix before release

### C1. Missing `ConfigureAwait(false)` throughout the entire library

**Where**: Every `await` call in every `.cs` file under `src/SharpSync/`. This affects `SyncEngine.cs`, all five storage implementations (`WebDavStorage.cs`, `SftpStorage.cs`, `FtpStorage.cs`, `S3Storage.cs`, `LocalFileStorage.cs`), `SqliteSyncDatabase.cs`, `SmartConflictResolver.cs`, and `DefaultConflictResolver.cs`.

**What**: There is not a single call to `.ConfigureAwait(false)` anywhere in the library. When a consumer calls this library from a WPF, WinForms, or ASP.NET application, every `await` without `ConfigureAwait(false)` captures the synchronization context and marshals the continuation back to the calling thread (e.g., the UI thread). This causes:

- Deadlocks (the classic `.Result`/`.Wait()` scenario)
- Performance degradation from unnecessary UI thread marshaling

This is the #1 best practice for .NET library code per Microsoft's guidelines. For a library explicitly targeting desktop client integration, this is an outright correctness hazard.

**Fix**: Add `.ConfigureAwait(false)` to every `await` call in the library. Consider adding `ConfigureAwait.Fody` or `Meziantou.Analyzer` (rule MA0004) to enforce this going forward.

### C2. `SyncOptions.Clone()` produces a broken shallow copy

**Where**: `src/SharpSync/Core/SyncOptions.cs`, line 109.

**What**: `Clone()` uses `MemberwiseClone()`, which creates a shallow copy. The `ExcludePatterns` property is a `List<string>` (a reference type), so the cloned instance shares the same list object. Mutating `ExcludePatterns` on the clone corrupts the original, and vice versa.

**Fix**:
```csharp
public SyncOptions Clone() {
    var clone = (SyncOptions)MemberwiseClone();
    clone.ExcludePatterns = new List<string>(ExcludePatterns);
    return clone;
}
```

---

## SERIOUS -- Should fix before release

### S1. No SourceLink, no deterministic builds, no debug symbols

**Where**: `src/SharpSync/SharpSync.csproj`.

**What**: Missing `PublishRepositoryUrl`, `EmbedUntrackedSources`, `DebugType` set to `embedded`/`portable`, `IncludeSymbols`/`SymbolPackageFormat`, and SourceLink package reference. `Deterministic` is not explicitly set. `IncludeSourceRevisionInInformationalVersion` is actually set to `false`, which suppresses the commit hash.

Without SourceLink, consumers cannot step into SharpSync source code when debugging. Without deterministic builds, reproducibility is not guaranteed. These are table-stakes for any serious NuGet package.

**Fix**: Add to the `<PropertyGroup>`:
```xml
<PublishRepositoryUrl>true</PublishRepositoryUrl>
<EmbedUntrackedSources>true</EmbedUntrackedSources>
<DebugType>embedded</DebugType>
<Deterministic>true</Deterministic>
<ContinuousIntegrationBuild Condition="'$(CI)' == 'true'">true</ContinuousIntegrationBuild>
```
And add:
```xml
<PackageReference Include="Microsoft.SourceLink.GitHub" Version="8.0.0" PrivateAssets="all" />
```

### S2. `SyncPlan` properties enumerate `Actions` on every access

**Where**: `src/SharpSync/Core/SyncPlan.cs`, lines 20-75.

**What**: Every property (`Downloads`, `Uploads`, `LocalDeletes`, `RemoteDeletes`, `Conflicts`) calls `.Where(...).ToList()` on the full `Actions` list each time it is accessed. The computed properties (`DownloadCount`, `UploadCount`, etc.) each trigger their respective property getter, which re-enumerates and re-allocates. Properties like `HasConflicts` call `ConflictCount`, which calls `Conflicts`, which allocates a full list just to check `.Count`.

A consumer inspecting a plan in a UI will enumerate `Actions` 10+ times, creating a new `List<T>` allocation each time.

**Fix**: Cache the categorized lists lazily:
```csharp
private IReadOnlyList<SyncPlanAction>? _downloads;
public IReadOnlyList<SyncPlanAction> Downloads =>
    _downloads ??= Actions.Where(a => a.ActionType == SyncActionType.Download).ToList();
```
Or compute all groups once in the constructor/init.

### S4. Regex Denial of Service (ReDoS) risk in `SyncFilter`

**Where**: `src/SharpSync/Sync/SyncFilter.cs`:
- Line 88: `new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled)` (exclusion)
- Line 117: `new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled)` (inclusion)
- Line 224: `Regex.IsMatch(path, regexPattern, RegexOptions.IgnoreCase)` (wildcard matching)

**What**: User-provided patterns are compiled into regex without `NonBacktracking` or a timeout. A malicious or accidental pattern could cause catastrophic backtracking, hanging the sync engine.

**Fix**: Use `RegexOptions.NonBacktracking` (available in .NET 8+):
```csharp
var regex = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.NonBacktracking);
// ...
return Regex.IsMatch(path, regexPattern, RegexOptions.IgnoreCase | RegexOptions.NonBacktracking);
```
Note: `NonBacktracking` and `Compiled` are mutually exclusive in .NET. `NonBacktracking` is preferred here since security > minor perf gain from compilation.

### S4. `SqliteSyncDatabase.Dispose()` calls `.Wait()` on an async method

**Where**: `src/SharpSync/Database/SqliteSyncDatabase.cs`, line 292.

**What**: `Dispose()` calls `_connection?.CloseAsync().Wait()`, which blocks the calling thread waiting for an async operation. Combined with C1 (no `ConfigureAwait(false)`), this can deadlock on the UI thread.

**Fix**: Implement `IAsyncDisposable` alongside `IDisposable`:
```csharp
public class SqliteSyncDatabase : ISyncDatabase, IAsyncDisposable {
    public async ValueTask DisposeAsync() {
        if (!_disposed) {
            if (_connection is not null)
                await _connection.CloseAsync().ConfigureAwait(false);
            _connection = null;
            _disposed = true;
        }
        GC.SuppressFinalize(this);
    }
}
```

### S5. Four bare `catch { }` blocks swallow all exceptions silently

**Where**:
- `src/SharpSync/Sync/SyncEngine.cs:1430` -- `GetDomainFromUrl` catches URI parsing failures
- `src/SharpSync/Sync/SyncEngine.cs:2158` -- `TryGetItemAsync` catches storage errors
- `src/SharpSync/Sync/SyncFilter.cs:90` -- catches regex compilation failures (exclusion)
- `src/SharpSync/Sync/SyncFilter.cs:119` -- catches regex compilation failures (inclusion)

**What**: Silent exception swallowing makes debugging impossible. The `TryGetItemAsync` case is particularly severe since it directly affects sync behavior -- when a storage call fails, the file is silently skipped.

**Fix**: At minimum, log at debug/trace level in each catch block. For `TryGetItemAsync`, change to:
```csharp
catch (Exception ex) {
    _logger.LogDebug(ex, "Failed to get item at {Path}", path);
    return null;
}
```
For `SyncFilter`, log when a user-provided pattern fails to compile.

### S6. Non-sealed public classes implementing `IDisposable` without proper pattern

**Where**:
- `src/SharpSync/Sync/SyncEngine.cs:38` -- `public class SyncEngine`
- `src/SharpSync/Database/SqliteSyncDatabase.cs:13` -- `public class SqliteSyncDatabase`

**What**: These are unsealed public classes implementing `IDisposable` without the `Dispose(bool disposing)` pattern and without `GC.SuppressFinalize(this)`. If subclassed, derived classes cannot properly participate in disposal. Violates CA1063.

**Fix**: Either mark them `sealed`, or implement the full dispose pattern:
```csharp
protected virtual void Dispose(bool disposing) {
    if (!_disposed) {
        if (disposing) { /* dispose managed resources */ }
        _disposed = true;
    }
}

public void Dispose() {
    Dispose(disposing: true);
    GC.SuppressFinalize(this);
}
```
Sealing is simpler and likely the right choice if subclassing is not a supported scenario.

### S7. `SynchronizeAsync` wraps `OperationCanceledException` in dead code

**Where**: `src/SharpSync/Sync/SyncEngine.cs`, lines 232, 1750, 1833.

**What**: When a sync is cancelled, the code does:
```csharp
catch (OperationCanceledException) {
    result.Error = new InvalidOperationException("Synchronization was cancelled");
    throw;
}
```
The `result.Error` assignment is dead code -- since the exception is re-thrown, `result` is never returned to the caller. It's also misleading to wrap `OperationCanceledException` in an `InvalidOperationException`.

**Fix**: Simply re-throw without setting `result.Error`:
```csharp
catch (OperationCanceledException) {
    throw;
}
```
Or remove the catch block entirely (it only re-throws).

---

## MODERATE -- Should fix for polish

### M1. No `PackageIcon` in the NuGet package

**Where**: `src/SharpSync/SharpSync.csproj`.

**What**: No `PackageIcon` property and no icon file. Packages without icons look unprofessional on NuGet.org.

**Fix**: Add a 128x128 PNG icon and reference it:
```xml
<PackageIcon>icon.png</PackageIcon>
<!-- ... -->
<None Include="..\..\icon.png" Pack="true" PackagePath="" />
```

### M2. No `CHANGELOG.md`

**Where**: Repository root.

**What**: For a v1.0.0 release, consumers expect a changelog documenting what's in the release. Follow [Keep a Changelog](https://keepachangelog.com/) format.

### M3. `Array.Empty<T>()` instead of `[]` collection expressions

**Where**:
- `src/SharpSync/Core/SyncPlan.cs:15`
- `src/SharpSync/Sync/SyncEngine.cs:295, 341`

**What**: Uses `Array.Empty<SyncPlanAction>()` where the C# 12 collection expression `[]` is preferred for .NET 8.

### M4. `await Task.CompletedTask` antipattern

**Where**:
- `src/SharpSync/Storage/LocalFileStorage.cs:191, 211, 238`
- `src/SharpSync/Core/DefaultConflictResolver.cs:31`
- `src/SharpSync/Core/SmartConflictResolver.cs:117`

**What**: `await Task.CompletedTask` adds an unnecessary state machine allocation while providing no actual asynchrony. The comment "Make it truly async" is incorrect -- `Task.CompletedTask` completes synchronously.

**Fix**: Remove the `async` keyword and return `Task.FromResult(value)` directly, or use `ValueTask<T>`.

### M5. `StorageProgressEventArgs` uses mutable setters

**Where**: `src/SharpSync/Storage/StorageProgressEventArgs.cs`, lines 10-30.

**What**: All properties have public `set` accessors. Event args should be immutable by convention -- multiple subscribers could mutate the same instance. Compare with `FileProgressEventArgs` and `SyncProgressEventArgs` which correctly use read-only properties.

**Fix**: Change to `init` or constructor-initialized read-only properties.

### M6. `SyncOptions.ExcludePatterns` exposes concrete `List<string>`

**Where**: `src/SharpSync/Core/SyncOptions.cs:60`.

**What**: Initialized as `new List<string>()` (old-style syntax) and exposes the concrete `List<string>` type.

**Fix**: Change initialization to `[]` and consider changing the property type to `IList<string>`.

### M7. `SyncItem.Metadata` exposes `Dictionary<string, object>`

**Where**: `src/SharpSync/Core/SyncItem.cs:45`.

**What**: Exposes a mutable concrete `Dictionary<string, object>` directly. Uses `object` as the value type, requiring casting by consumers. No documentation on expected keys.

**Fix**: Document expected metadata keys. Consider `IDictionary<string, string>` for a simpler public API.

### M8. `ChangeSet` race condition from parallel mutation

**Where**: `src/SharpSync/Sync/ChangeSet.cs` and `src/SharpSync/Sync/SyncEngine.cs:630-635`.

**What**: `ChangeSet` contains `List<T>` and `HashSet<T>` properties that are mutated from parallel tasks. `ScanDirectoryRecursiveAsync` launches tasks via `Task.WhenAll` (line 635), and each task modifies the shared `ChangeSet` (adding to `LocalPaths`, `RemotePaths`, `Additions`, `Modifications`). `List<T>` and `HashSet<T>` are not thread-safe -- this is a race condition that can cause data corruption or exceptions.

**Fix**: Use `ConcurrentBag<T>` / `ConcurrentDictionary` for thread-safe collections, or add locking around mutations to the `ChangeSet`.

---

## NITPICK -- Would be nice to fix

### N1. `SmartConflictResolver` references "Nimbus" by name

**Where**: `src/SharpSync/Core/SmartConflictResolver.cs:12`.

**What**: XML doc says "Nimbus can implement this to show dialogs". Library public API docs should be consumer-agnostic.

**Fix**: Change to "Desktop clients can implement this to show UI dialogs."

### N2. Inconsistent collection initialization

**Where**: Throughout the codebase.

**What**: Mixes `new List<T>()`, `new()`, and `[]` syntax. Example: `SyncFilter.cs` uses `new()`, `SyncOptions.cs` uses `new List<string>()`, `OAuth2Config.cs` uses `new()` for dictionary.

**Fix**: Standardize on `[]` collection expression syntax (C# 12 / .NET 8).

### N3. `OAuth2Config` property initialization inconsistency

**Where**: `src/SharpSync/Auth/OAuth2Config.cs`, lines 35 and 40.

**What**: `Scopes` uses `Array.Empty<string>()` while `AdditionalParameters` uses `new()`. Two properties on the same type with different patterns.

**Fix**: Use `[]` for both.

### N4. `GetSyncPlanAsync` catches `Exception` and returns empty plan silently

**Where**: `src/SharpSync/Sync/SyncEngine.cs:339`.

**What**: Catches all exceptions and returns an empty plan, giving the caller no indication that something went wrong. If there's a network error or database failure, the consumer receives an empty plan and concludes there are no changes.

**Fix**: Log the exception and consider either propagating it or adding an error property to `SyncPlan`.

### N5. `SyncEngine` constructor has 9 parameters

**Where**: `src/SharpSync/Sync/SyncEngine.cs:138-148`.

**What**: 4 required + 5 optional parameters. While optional parameters mitigate the pain, this is a code smell. An options/builder pattern would be more idiomatic.

**Fix**: Consider a `SyncEngineOptions` class or builder pattern for the optional parameters. Not required for v1.0 but worth considering.

### N6. Inconsistent XML doc summary trailing periods

**Where**: Multiple files.

**What**: Some `<summary>` elements end with a period, others do not. For example, `ISyncStorage.cs` has no periods while `SyncEngine.cs` sometimes does.

**Fix**: Consistently end all `<summary>` text with a period.

### N7. `ConflictAnalysis.TimeDifference` is `double` instead of `TimeSpan`

**Where**: `src/SharpSync/Core/ConflictAnalysis.cs:60`.

**What**: `TimeDifference` is typed as `double` representing seconds. Using `TimeSpan` would be more idiomatic and self-documenting.

### N8. Test class uses `.GetAwaiter().GetResult()` in constructor

**Where**: `tests/SharpSync.Tests/Sync/SyncEngineTests.cs:28`.

**What**: The test class constructor calls `_database.InitializeAsync().GetAwaiter().GetResult()`. xUnit supports `IAsyncLifetime` for async setup.

---

## SUGGESTIONS -- Not issues, but improvements for future versions

### SG1. Add `Directory.Build.props` for shared build properties

Common properties (`Nullable`, `ImplicitUsings`, `AnalysisLevel`, analyzers) are duplicated between `SharpSync.csproj` and `SharpSync.Tests.csproj`. Centralize with `Directory.Build.props`.

### SG2. Consider multi-targeting `net9.0`

The library targets only `net8.0`. Since .NET 8 LTS is supported until November 2027, this is reasonable. Adding `net9.0` or `net10.0` (when available) would expand reach.

### SG3. Consider making `ISyncStorage` extend `IDisposable`

All non-local storage implementations implement `IDisposable` separately. Consumers programming against `ISyncStorage` must cast to `IDisposable` for cleanup. Adding `IDisposable` (or `IAsyncDisposable`) to the interface would be more ergonomic.

### SG4. Add XML doc `<exception>` tags to public methods

Public methods document exceptions in `<remarks>` but do not use the standard `<exception>` tags that IntelliSense presents to developers.

### SG5. Add `[assembly: CLSCompliant(true)]`

Signals the API is usable from all .NET languages (VB.NET, F#, etc.).

### SG6. Consider publishing API docs via DocFX or similar

For a v1.0 library with this much API surface, published API documentation would help adoption.

---

## Priority order for fixes

1. **C1** -- `ConfigureAwait(false)` everywhere (release-blocker, deadlock risk)
2. **C2** -- `SyncOptions.Clone()` deep copy (release-blocker, data corruption)
3. **M8** -- `ChangeSet` thread safety (race condition causing potential crashes)
4. **S4** -- `SqliteSyncDatabase.Dispose()` async (deadlock risk)
5. **S5** -- Silent catch blocks (debuggability)
6. **S6** -- Seal or fix dispose pattern (API correctness)
7. **S7** -- Dead code in cancellation handlers (cleanup)
8. **S1** -- SourceLink + deterministic builds (NuGet best practice)
9. **S2** -- `SyncPlan` allocation waste (performance)
10. **S3** -- ReDoS protection (security)
11. **N1** -- Remove "Nimbus" reference (API neutrality)
12. Everything else in order of severity
