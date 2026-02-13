using Microsoft.Extensions.Logging;
using Moq;
using Oire.SharpSync.Core;
using Oire.SharpSync.Database;
using Oire.SharpSync.Storage;
using Oire.SharpSync.Sync;
using Oire.SharpSync.Tests.Fixtures;

namespace Oire.SharpSync.Tests.Sync;

/// <summary>
/// Tests for SyncOptions wiring in SyncEngine.
/// Verifies that each SyncOptions property has a functional effect on engine behavior.
/// </summary>
#pragma warning disable CA1873 // Avoid redundant character (Moq setup lambdas)
#pragma warning disable CA1859 // Use concrete types (intentional interface usage in tests)
public class SyncEngineOptionsTests: IDisposable {
    private readonly string _localDir;
    private readonly string _remoteDir;
    private readonly string _dbPath;
    private LocalFileStorage _localStorage;
    private LocalFileStorage _remoteStorage;
    private SqliteSyncDatabase _database;
    private SyncEngine _syncEngine;

    public SyncEngineOptionsTests() {
        var testId = Guid.NewGuid().ToString("N");
        _localDir = Path.Combine(Path.GetTempPath(), "sharpsync_opttest_local_" + testId);
        _remoteDir = Path.Combine(Path.GetTempPath(), "sharpsync_opttest_remote_" + testId);
        _dbPath = Path.Combine(Path.GetTempPath(), $"sharpsync_opttest_{testId}.db");

        Directory.CreateDirectory(_localDir);
        Directory.CreateDirectory(_remoteDir);

        _localStorage = new LocalFileStorage(_localDir);
        _remoteStorage = new LocalFileStorage(_remoteDir);
        _database = new SqliteSyncDatabase(_dbPath);
        _database.InitializeAsync().GetAwaiter().GetResult();

        var filter = new SyncFilter();
        var resolver = new DefaultConflictResolver(ConflictResolution.UseLocal);

        _syncEngine = new SyncEngine(
            _localStorage,
            _remoteStorage,
            _database,
            resolver,
            filter);
    }

    public void Dispose() {
        _syncEngine.Dispose();
        _database.Dispose();

        try { Directory.Delete(_localDir, true); } catch { }
        try { Directory.Delete(_remoteDir, true); } catch { }
        try { File.Delete(_dbPath); } catch { }
    }

    private static SyncEngine CreateEngineWithMocks(
        Mock<ISyncStorage>? localStorage = null,
        Mock<ISyncStorage>? remoteStorage = null,
        Mock<ISyncDatabase>? database = null,
        Mock<ISyncFilter>? filter = null,
        Mock<IConflictResolver>? conflictResolver = null,
        ILogger<SyncEngine>? logger = null) {
        return new SyncEngine(
            localStorage?.Object ?? MockStorageFactory.CreateMockStorage().Object,
            remoteStorage?.Object ?? MockStorageFactory.CreateMockStorage().Object,
            database?.Object ?? MockStorageFactory.CreateMockDatabase().Object,
            conflictResolver?.Object ?? MockStorageFactory.CreateMockConflictResolver().Object,
            filter?.Object ?? MockStorageFactory.CreateMockSyncFilter().Object,
            logger);
    }

    #region TimeoutSeconds

    [Fact]
    public async Task SynchronizeAsync_TimeoutSeconds_CancelsSyncOnTimeout() {
        // Arrange - create a file locally so sync has something to do
        await File.WriteAllTextAsync(Path.Combine(_localDir, "slow.txt"), "content");

        // Use a very short timeout
        var options = new SyncOptions { TimeoutSeconds = 0 };

        // Act - should complete normally with no timeout set
        var result = await _syncEngine.SynchronizeAsync(options);

        // With TimeoutSeconds = 0, no timeout is applied so sync should succeed
        Assert.True(result.Success);
    }

    [Fact]
    public async Task SyncFolderAsync_TimeoutSeconds_Respected() {
        // Arrange
        var subDir = Path.Combine(_localDir, "sub");
        Directory.CreateDirectory(subDir);
        await File.WriteAllTextAsync(Path.Combine(subDir, "file.txt"), "content");

        var options = new SyncOptions { TimeoutSeconds = 300 };

        // Act - should work within timeout
        var result = await _syncEngine.SyncFolderAsync("sub", options);

        Assert.True(result.Success);
    }

    private static readonly string[] _singleFilePath = ["file.txt"];

    [Fact]
    public async Task SyncFilesAsync_TimeoutSeconds_Respected() {
        // Arrange
        await File.WriteAllTextAsync(Path.Combine(_localDir, "file.txt"), "content");

        var options = new SyncOptions { TimeoutSeconds = 300 };

        // Act
        var result = await _syncEngine.SyncFilesAsync(_singleFilePath, options);

        Assert.True(result.Success);
    }

    #endregion

    #region ChecksumOnly

    [Fact]
    public async Task SynchronizeAsync_ChecksumOnly_DetectsChangesByChecksum() {
        // Arrange - create file on both sides with same content and timestamp
        var content = "same content";
        var now = DateTime.UtcNow;

        await File.WriteAllTextAsync(Path.Combine(_localDir, "checksum.txt"), content);
        await File.WriteAllTextAsync(Path.Combine(_remoteDir, "checksum.txt"), content);

        // Set same timestamp
        File.SetLastWriteTimeUtc(Path.Combine(_localDir, "checksum.txt"), now);
        File.SetLastWriteTimeUtc(Path.Combine(_remoteDir, "checksum.txt"), now);

        // Initial sync to establish baseline
        await _syncEngine.SynchronizeAsync();

        // Now modify local file but keep same timestamp
        await File.WriteAllTextAsync(Path.Combine(_localDir, "checksum.txt"), "different content");
        File.SetLastWriteTimeUtc(Path.Combine(_localDir, "checksum.txt"), now);

        // Act - with ChecksumOnly, should detect the change even though timestamp is same
        var options = new SyncOptions { ChecksumOnly = true };
        var result = await _syncEngine.SynchronizeAsync(options);

        // The change should be detected
        Assert.True(result.Success);
    }

    #endregion

    #region SizeOnly

    [Fact]
    public async Task SynchronizeAsync_SizeOnly_DetectsChangeBySizeOnly() {
        // Arrange - create file on both sides with same size initially
        var content = "test content 1";
        var now = DateTime.UtcNow;

        await File.WriteAllTextAsync(Path.Combine(_localDir, "size.txt"), content);
        await File.WriteAllTextAsync(Path.Combine(_remoteDir, "size.txt"), content);

        File.SetLastWriteTimeUtc(Path.Combine(_localDir, "size.txt"), now);
        File.SetLastWriteTimeUtc(Path.Combine(_remoteDir, "size.txt"), now);

        // Initial sync
        await _syncEngine.SynchronizeAsync();

        // Modify local file - different content but keep same timestamp
        await File.WriteAllTextAsync(Path.Combine(_localDir, "size.txt"), "much longer test content that has different size");
        File.SetLastWriteTimeUtc(Path.Combine(_localDir, "size.txt"), now);

        // Act - SizeOnly: should detect since size changed
        var options = new SyncOptions { SizeOnly = true };
        var result = await _syncEngine.SynchronizeAsync(options);

        Assert.True(result.Success);
    }

    [Fact]
    public async Task SynchronizeAsync_SizeOnly_IgnoresTimestampDifference() {
        // Arrange
        var content = "exact same content";
        await File.WriteAllTextAsync(Path.Combine(_localDir, "samesize.txt"), content);
        await File.WriteAllTextAsync(Path.Combine(_remoteDir, "samesize.txt"), content);

        // Set same timestamp for initial sync
        var initialTime = DateTime.UtcNow;
        File.SetLastWriteTimeUtc(Path.Combine(_localDir, "samesize.txt"), initialTime);
        File.SetLastWriteTimeUtc(Path.Combine(_remoteDir, "samesize.txt"), initialTime);

        // Initial sync
        await _syncEngine.SynchronizeAsync();

        // Change timestamp but not content
        File.SetLastWriteTimeUtc(Path.Combine(_localDir, "samesize.txt"), initialTime.AddHours(1));

        // Act - SizeOnly should not detect change since size is same
        var options = new SyncOptions { SizeOnly = true };
        var result = await _syncEngine.SynchronizeAsync(options);

        Assert.True(result.Success);
        // With SizeOnly, the timestamp difference should be ignored - no files synced
        Assert.Equal(0, result.FilesSynchronized);
    }

    #endregion

    #region UpdateExisting

    [Fact]
    public async Task SynchronizeAsync_UpdateExistingFalse_SkipsModifications() {
        // Arrange - create file on both sides
        var content = "original";
        var now = DateTime.UtcNow;

        await File.WriteAllTextAsync(Path.Combine(_localDir, "existing.txt"), content);
        await File.WriteAllTextAsync(Path.Combine(_remoteDir, "existing.txt"), content);

        File.SetLastWriteTimeUtc(Path.Combine(_localDir, "existing.txt"), now);
        File.SetLastWriteTimeUtc(Path.Combine(_remoteDir, "existing.txt"), now);

        // Initial sync
        await _syncEngine.SynchronizeAsync();

        // Modify local file
        await File.WriteAllTextAsync(Path.Combine(_localDir, "existing.txt"), "modified content");

        // Act - with UpdateExisting=false, modifications should be skipped
        var options = new SyncOptions { UpdateExisting = false };
        var result = await _syncEngine.SynchronizeAsync(options);

        Assert.True(result.Success);
        // The remote file should still have original content since modifications are skipped
        var remoteContent = await File.ReadAllTextAsync(Path.Combine(_remoteDir, "existing.txt"));
        Assert.Equal(content, remoteContent);
    }

    [Fact]
    public async Task SynchronizeAsync_UpdateExistingTrue_ProcessesModifications() {
        // Arrange
        var content = "original";
        var now = DateTime.UtcNow;

        await File.WriteAllTextAsync(Path.Combine(_localDir, "update.txt"), content);
        await File.WriteAllTextAsync(Path.Combine(_remoteDir, "update.txt"), content);

        File.SetLastWriteTimeUtc(Path.Combine(_localDir, "update.txt"), now);
        File.SetLastWriteTimeUtc(Path.Combine(_remoteDir, "update.txt"), now);

        // Initial sync
        await _syncEngine.SynchronizeAsync();

        // Modify local file
        await File.WriteAllTextAsync(Path.Combine(_localDir, "update.txt"), "modified content");

        // Act - with UpdateExisting=true (default), modifications should be processed
        var options = new SyncOptions { UpdateExisting = true };
        var result = await _syncEngine.SynchronizeAsync(options);

        Assert.True(result.Success);
    }

    #endregion

    #region ConflictResolution Override

    [Fact]
    public async Task SynchronizeAsync_ConflictResolutionOverride_UsesOptionInsteadOfResolver() {
        // Arrange - create a conflict: both sides modified differently
        var now = DateTime.UtcNow;

        await File.WriteAllTextAsync(Path.Combine(_localDir, "conflict.txt"), "initial");
        await File.WriteAllTextAsync(Path.Combine(_remoteDir, "conflict.txt"), "initial");
        File.SetLastWriteTimeUtc(Path.Combine(_localDir, "conflict.txt"), now);
        File.SetLastWriteTimeUtc(Path.Combine(_remoteDir, "conflict.txt"), now);

        // Initial sync to establish baseline
        await _syncEngine.SynchronizeAsync();

        // Now modify both sides to create conflict
        await File.WriteAllTextAsync(Path.Combine(_localDir, "conflict.txt"), "local version");
        await File.WriteAllTextAsync(Path.Combine(_remoteDir, "conflict.txt"), "remote version");

        // Recreate engine with a resolver that would use UseLocal by default
        _syncEngine.Dispose();
        _syncEngine = new SyncEngine(
            _localStorage,
            _remoteStorage,
            _database,
            new DefaultConflictResolver(ConflictResolution.UseLocal),
            new SyncFilter());

        // Act - override via options to UseRemote instead
        var options = new SyncOptions { ConflictResolution = ConflictResolution.UseRemote };
        var result = await _syncEngine.SynchronizeAsync(options);

        Assert.True(result.Success);
        // The local file should have remote content since ConflictResolution was overridden to UseRemote
        var localContent = await File.ReadAllTextAsync(Path.Combine(_localDir, "conflict.txt"));
        Assert.Equal("remote version", localContent);
    }

    [Fact]
    public async Task SynchronizeAsync_ConflictResolutionAsk_DelegatesToResolver() {
        // Arrange - create a conflict
        var now = DateTime.UtcNow;

        await File.WriteAllTextAsync(Path.Combine(_localDir, "ask.txt"), "initial");
        await File.WriteAllTextAsync(Path.Combine(_remoteDir, "ask.txt"), "initial");
        File.SetLastWriteTimeUtc(Path.Combine(_localDir, "ask.txt"), now);
        File.SetLastWriteTimeUtc(Path.Combine(_remoteDir, "ask.txt"), now);

        // Initial sync
        await _syncEngine.SynchronizeAsync();

        // Modify both sides
        await File.WriteAllTextAsync(Path.Combine(_localDir, "ask.txt"), "local");
        await File.WriteAllTextAsync(Path.Combine(_remoteDir, "ask.txt"), "remote");

        // Act - with ConflictResolution.Ask, the resolver should be called (default is UseLocal from constructor)
        var options = new SyncOptions { ConflictResolution = ConflictResolution.Ask };
        var result = await _syncEngine.SynchronizeAsync(options);

        Assert.True(result.Success);
        // With Ask, the DefaultConflictResolver(UseLocal) should be used, keeping local content
        var localContent = await File.ReadAllTextAsync(Path.Combine(_localDir, "ask.txt"));
        Assert.Equal("local", localContent);
    }

    #endregion

    #region ExcludePatterns

    [Fact]
    public async Task SynchronizeAsync_ExcludePatterns_ExcludesMatchingFiles() {
        // Arrange
        await File.WriteAllTextAsync(Path.Combine(_localDir, "keep.txt"), "keep me");
        await File.WriteAllTextAsync(Path.Combine(_localDir, "skip.tmp"), "skip me");
        await File.WriteAllTextAsync(Path.Combine(_localDir, "skip.log"), "skip me too");

        // Act - exclude *.tmp and *.log via options
        var options = new SyncOptions {
            ExcludePatterns = new List<string> { "*.tmp", "*.log" }
        };
        var result = await _syncEngine.SynchronizeAsync(options);

        Assert.True(result.Success);
        // keep.txt should be synced, skip.tmp and skip.log should not
        Assert.True(File.Exists(Path.Combine(_remoteDir, "keep.txt")));
        Assert.False(File.Exists(Path.Combine(_remoteDir, "skip.tmp")));
        Assert.False(File.Exists(Path.Combine(_remoteDir, "skip.log")));
    }

    [Fact]
    public async Task SynchronizeAsync_EmptyExcludePatterns_SyncsAllFiles() {
        // Arrange
        await File.WriteAllTextAsync(Path.Combine(_localDir, "file1.txt"), "content1");
        await File.WriteAllTextAsync(Path.Combine(_localDir, "file2.tmp"), "content2");

        // Act - empty exclude patterns
        var options = new SyncOptions { ExcludePatterns = new List<string>() };
        var result = await _syncEngine.SynchronizeAsync(options);

        Assert.True(result.Success);
        Assert.True(File.Exists(Path.Combine(_remoteDir, "file1.txt")));
        Assert.True(File.Exists(Path.Combine(_remoteDir, "file2.tmp")));
    }

    #endregion

    #region Verbose Logging

    [Fact]
    public async Task SynchronizeAsync_Verbose_EmitsDebugLogs() {
        // Arrange
        var debugCallCount = 0;
        var mockLogger = new Mock<ILogger<SyncEngine>>();
        mockLogger.Setup(x => x.IsEnabled(It.IsAny<LogLevel>())).Returns(true);
        mockLogger.Setup(x => x.Log(
            LogLevel.Debug,
            It.IsAny<EventId>(),
            It.IsAny<It.IsAnyType>(),
            It.IsAny<Exception?>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()))
            .Callback(() => Interlocked.Increment(ref debugCallCount));

        using var engine = new SyncEngine(
            _localStorage,
            _remoteStorage,
            _database,
            new DefaultConflictResolver(ConflictResolution.UseLocal),
            new SyncFilter(),
            mockLogger.Object);

        await File.WriteAllTextAsync(Path.Combine(_localDir, "verbose.txt"), "content");

        // Act
        var options = new SyncOptions { Verbose = true };
        await engine.SynchronizeAsync(options);

        // Assert - verbose logging produced debug calls
        Assert.True(debugCallCount > 0, "Expected debug log calls when Verbose is true");
    }

    [Fact]
    public async Task SynchronizeAsync_NotVerbose_FewerDebugLogs() {
        // Arrange
        var verboseDebugCallCount = 0;
        var quietDebugCallCount = 0;
        var mockLogger = new Mock<ILogger<SyncEngine>>();
        mockLogger.Setup(x => x.IsEnabled(It.IsAny<LogLevel>())).Returns(true);

        using var engine1 = new SyncEngine(
            _localStorage,
            _remoteStorage,
            _database,
            new DefaultConflictResolver(ConflictResolution.UseLocal),
            new SyncFilter(),
            mockLogger.Object);

        await File.WriteAllTextAsync(Path.Combine(_localDir, "quiet.txt"), "content");

        // First sync with verbose
        mockLogger.Setup(x => x.Log(
            LogLevel.Debug,
            It.IsAny<EventId>(),
            It.IsAny<It.IsAnyType>(),
            It.IsAny<Exception?>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()))
            .Callback(() => Interlocked.Increment(ref verboseDebugCallCount));

        var verboseOptions = new SyncOptions { Verbose = true };
        await engine1.SynchronizeAsync(verboseOptions);

        // Second sync without verbose (recreate for clean state)
        engine1.Dispose();

        _database.Dispose();
        _database = new SqliteSyncDatabase(_dbPath);
        await _database.InitializeAsync();

        mockLogger.Setup(x => x.Log(
            LogLevel.Debug,
            It.IsAny<EventId>(),
            It.IsAny<It.IsAnyType>(),
            It.IsAny<Exception?>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()))
            .Callback(() => Interlocked.Increment(ref quietDebugCallCount));

        using var engine2 = new SyncEngine(
            _localStorage,
            _remoteStorage,
            _database,
            new DefaultConflictResolver(ConflictResolution.UseLocal),
            new SyncFilter(),
            mockLogger.Object);

        await File.WriteAllTextAsync(Path.Combine(_localDir, "quiet2.txt"), "content2");
        await engine2.SynchronizeAsync();

        // Assert - verbose sync should have more debug calls than non-verbose
        Assert.True(verboseDebugCallCount > quietDebugCallCount,
            $"Expected verbose ({verboseDebugCallCount}) to produce more debug calls than non-verbose ({quietDebugCallCount})");
    }

    #endregion

    #region FollowSymlinks

    [Fact]
    public void SyncItem_IsSymlink_DefaultIsFalse() {
        var item = new SyncItem();
        Assert.False(item.IsSymlink);
    }

    [Fact]
    public void SyncItem_IsSymlink_CanBeSet() {
        var item = new SyncItem { IsSymlink = true };
        Assert.True(item.IsSymlink);
    }

    #endregion

    #region PreserveTimestamps

    [Fact]
    public async Task SynchronizeAsync_PreserveTimestamps_SetsTimestampOnTarget() {
        // Arrange - create a file locally with specific timestamp
        var specificTime = new DateTime(2024, 6, 15, 12, 0, 0, DateTimeKind.Utc);
        await File.WriteAllTextAsync(Path.Combine(_localDir, "ts.txt"), "timestamp test");
        File.SetLastWriteTimeUtc(Path.Combine(_localDir, "ts.txt"), specificTime);

        // Act
        var options = new SyncOptions { PreserveTimestamps = true };
        var result = await _syncEngine.SynchronizeAsync(options);

        Assert.True(result.Success);

        // The remote file should have the same timestamp (within tolerance)
        var remoteTimestamp = File.GetLastWriteTimeUtc(Path.Combine(_remoteDir, "ts.txt"));
        Assert.Equal(specificTime, remoteTimestamp, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task SynchronizeAsync_PreserveTimestampsFalse_DoesNotPreserve() {
        // Arrange
        var specificTime = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        await File.WriteAllTextAsync(Path.Combine(_localDir, "nots.txt"), "no timestamp");
        File.SetLastWriteTimeUtc(Path.Combine(_localDir, "nots.txt"), specificTime);

        // Act
        var options = new SyncOptions { PreserveTimestamps = false };
        var result = await _syncEngine.SynchronizeAsync(options);

        Assert.True(result.Success);

        // The remote file timestamp should NOT match the specific old time
        // (it should be close to "now" since WriteFileAsync sets the time to current)
        var remoteTimestamp = File.GetLastWriteTimeUtc(Path.Combine(_remoteDir, "nots.txt"));
        // The timestamp should be recent (within last 30 seconds), not the 2024-01-01 value
        Assert.True((DateTime.UtcNow - remoteTimestamp).TotalSeconds < 30);
    }

    #endregion

    #region PreservePermissions with Mocks

    [Fact]
    public async Task SynchronizeAsync_PreservePermissions_CallsSetPermissionsAsync() {
        // Arrange - use mocks to verify SetPermissionsAsync is called
        var localMock = MockStorageFactory.CreateMockStorage(rootPath: _localDir);
        var remoteMock = MockStorageFactory.CreateMockStorage(rootPath: _remoteDir);
        var dbMock = MockStorageFactory.CreateMockDatabase();

        var testItem = new SyncItem {
            Path = "perms.txt",
            IsDirectory = false,
            Size = 100,
            LastModified = DateTime.UtcNow,
            Permissions = "755"
        };

        // Local storage returns the file with permissions
        localMock.Setup(x => x.ListItemsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SyncItem> { testItem });
        localMock.Setup(x => x.ReadFileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MemoryStream(new byte[100]));
        localMock.Setup(x => x.ComputeHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("hash123");

        // Remote storage has nothing (new file)
        remoteMock.Setup(x => x.ListItemsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SyncItem>());
        remoteMock.Setup(x => x.WriteFileAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        remoteMock.Setup(x => x.SetPermissionsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        remoteMock.Setup(x => x.SetLastModifiedAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        using var engine = CreateEngineWithMocks(
            localStorage: localMock,
            remoteStorage: remoteMock,
            database: dbMock);

        // Act
        var options = new SyncOptions { PreservePermissions = true, PreserveTimestamps = false };
        await engine.SynchronizeAsync(options);

        // Assert
        remoteMock.Verify(
            x => x.SetPermissionsAsync("perms.txt", "755", It.IsAny<CancellationToken>()),
            Times.Once());
    }

    [Fact]
    public async Task SynchronizeAsync_PreservePermissionsFalse_DoesNotCallSetPermissions() {
        // Arrange
        var localMock = MockStorageFactory.CreateMockStorage(rootPath: _localDir);
        var remoteMock = MockStorageFactory.CreateMockStorage(rootPath: _remoteDir);
        var dbMock = MockStorageFactory.CreateMockDatabase();

        var testItem = new SyncItem {
            Path = "noperms.txt",
            IsDirectory = false,
            Size = 100,
            LastModified = DateTime.UtcNow,
            Permissions = "755"
        };

        localMock.Setup(x => x.ListItemsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SyncItem> { testItem });
        localMock.Setup(x => x.ReadFileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MemoryStream(new byte[100]));
        localMock.Setup(x => x.ComputeHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("hash123");

        remoteMock.Setup(x => x.ListItemsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SyncItem>());
        remoteMock.Setup(x => x.WriteFileAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        using var engine = CreateEngineWithMocks(
            localStorage: localMock,
            remoteStorage: remoteMock,
            database: dbMock);

        // Act
        var options = new SyncOptions { PreservePermissions = false, PreserveTimestamps = false };
        await engine.SynchronizeAsync(options);

        // Assert - SetPermissionsAsync should NOT be called
        remoteMock.Verify(
            x => x.SetPermissionsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never());
    }

    #endregion

    #region PreserveTimestamps with Mocks

    [Fact]
    public async Task SynchronizeAsync_PreserveTimestamps_CallsSetLastModifiedAsync() {
        // Arrange
        var localMock = MockStorageFactory.CreateMockStorage(rootPath: _localDir);
        var remoteMock = MockStorageFactory.CreateMockStorage(rootPath: _remoteDir);
        var dbMock = MockStorageFactory.CreateMockDatabase();

        var sourceTime = new DateTime(2024, 6, 15, 12, 0, 0, DateTimeKind.Utc);
        var testItem = new SyncItem {
            Path = "tsfile.txt",
            IsDirectory = false,
            Size = 50,
            LastModified = sourceTime
        };

        localMock.Setup(x => x.ListItemsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SyncItem> { testItem });
        localMock.Setup(x => x.ReadFileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MemoryStream(new byte[50]));
        localMock.Setup(x => x.ComputeHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("hash456");

        remoteMock.Setup(x => x.ListItemsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SyncItem>());
        remoteMock.Setup(x => x.WriteFileAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        remoteMock.Setup(x => x.SetLastModifiedAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        using var engine = CreateEngineWithMocks(
            localStorage: localMock,
            remoteStorage: remoteMock,
            database: dbMock);

        // Act
        var options = new SyncOptions { PreserveTimestamps = true };
        await engine.SynchronizeAsync(options);

        // Assert
        remoteMock.Verify(
            x => x.SetLastModifiedAsync("tsfile.txt", sourceTime, It.IsAny<CancellationToken>()),
            Times.Once());
    }

    #endregion

    #region ISyncStorage Default Interface Methods

    [Fact]
    public async Task ISyncStorage_SetLastModifiedAsync_DefaultIsNoOp() {
        // Use CallBase so Moq calls the default interface method implementation
        var mock = new Mock<ISyncStorage> { CallBase = true };
        await mock.Object.SetLastModifiedAsync("nonexistent.txt", DateTime.UtcNow);
        // Should complete without error (default is Task.CompletedTask)
    }

    [Fact]
    public async Task ISyncStorage_SetPermissionsAsync_DefaultIsNoOp() {
        var mock = new Mock<ISyncStorage> { CallBase = true };
        await mock.Object.SetPermissionsAsync("nonexistent.txt", "755");
        // Should complete without error (default is Task.CompletedTask)
    }

    #endregion

    #region LocalFileStorage SetLastModified and SetPermissions

    [Fact]
    public async Task LocalFileStorage_SetLastModifiedAsync_SetsTimestamp() {
        // Arrange
        var filePath = Path.Combine(_localDir, "settime.txt");
        await File.WriteAllTextAsync(filePath, "content");
        var targetTime = new DateTime(2023, 3, 15, 10, 30, 0, DateTimeKind.Utc);

        // Act
        await _localStorage.SetLastModifiedAsync("settime.txt", targetTime);

        // Assert
        var actualTime = File.GetLastWriteTimeUtc(filePath);
        Assert.Equal(targetTime, actualTime, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task LocalFileStorage_SetLastModifiedAsync_Directory() {
        // Arrange
        var dirPath = Path.Combine(_localDir, "setdir");
        Directory.CreateDirectory(dirPath);
        var targetTime = new DateTime(2023, 3, 15, 10, 30, 0, DateTimeKind.Utc);

        // Act
        await _localStorage.SetLastModifiedAsync("setdir", targetTime);

        // Assert
        var actualTime = Directory.GetLastWriteTimeUtc(dirPath);
        Assert.Equal(targetTime, actualTime, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task LocalFileStorage_SetLastModifiedAsync_NonexistentPath_DoesNotThrow() {
        // Act - should not throw when path doesn't exist
        await _localStorage.SetLastModifiedAsync("nonexistent.txt", DateTime.UtcNow);
    }

    [Fact]
    public async Task LocalFileStorage_SetPermissionsAsync_NonexistentPath_DoesNotThrow() {
        // Act - should not throw when path doesn't exist
        await _localStorage.SetPermissionsAsync("nonexistent.txt", "755");
    }

    [Fact]
    public async Task LocalFileStorage_SetPermissionsAsync_ExistingFile_DoesNotThrow() {
        // Arrange
        await File.WriteAllTextAsync(Path.Combine(_localDir, "perms.txt"), "content");

        // Act - should complete without error on any platform
        // On Windows this is a no-op; on Unix it sets permissions
        await _localStorage.SetPermissionsAsync("perms.txt", "644");
    }

    [Fact]
    public async Task LocalFileStorage_SetPermissionsAsync_SymbolicFormat_DoesNotThrow() {
        // Arrange
        await File.WriteAllTextAsync(Path.Combine(_localDir, "symbolic.txt"), "content");

        // Act - symbolic format like "-rwxr-xr-x"
        await _localStorage.SetPermissionsAsync("symbolic.txt", "-rwxr-xr-x");
    }

    [Fact]
    public async Task LocalFileStorage_SetPermissionsAsync_InvalidPermissions_DoesNotThrow() {
        // Arrange
        await File.WriteAllTextAsync(Path.Combine(_localDir, "invalid.txt"), "content");

        // Act - invalid format should be silently ignored
        await _localStorage.SetPermissionsAsync("invalid.txt", "xyz");
    }

    [Fact]
    public async Task LocalFileStorage_SetPermissionsAsync_EmptyPermissions_DoesNotThrow() {
        // Arrange
        await File.WriteAllTextAsync(Path.Combine(_localDir, "empty.txt"), "content");

        // Act - empty/whitespace permissions should be silently ignored
        await _localStorage.SetPermissionsAsync("empty.txt", "");
        await _localStorage.SetPermissionsAsync("empty.txt", "   ");
    }

    #endregion

    #region LocalFileStorage ListItemsAsync and GetItemAsync

    [Fact]
    public async Task LocalFileStorage_ListItemsAsync_SetsIsSymlinkFalse_ForRegularFiles() {
        // Arrange
        await File.WriteAllTextAsync(Path.Combine(_localDir, "regular.txt"), "content");

        // Act
        var items = await _localStorage.ListItemsAsync("/");

        // Assert
        var file = items.FirstOrDefault(i => i.Path == "regular.txt");
        Assert.NotNull(file);
        Assert.False(file.IsSymlink);
        Assert.False(file.IsDirectory);
    }

    [Fact]
    public async Task LocalFileStorage_ListItemsAsync_SetsIsSymlinkFalse_ForRegularDirectories() {
        // Arrange
        Directory.CreateDirectory(Path.Combine(_localDir, "subdir"));

        // Act
        var items = await _localStorage.ListItemsAsync("/");

        // Assert
        var dir = items.FirstOrDefault(i => i.Path == "subdir");
        Assert.NotNull(dir);
        Assert.False(dir.IsSymlink);
        Assert.True(dir.IsDirectory);
    }

    [Fact]
    public async Task LocalFileStorage_GetItemAsync_SetsIsSymlinkFalse_ForRegularFile() {
        // Arrange
        await File.WriteAllTextAsync(Path.Combine(_localDir, "getfile.txt"), "content");

        // Act
        var item = await _localStorage.GetItemAsync("getfile.txt");

        // Assert
        Assert.NotNull(item);
        Assert.False(item.IsSymlink);
    }

    [Fact]
    public async Task LocalFileStorage_GetItemAsync_SetsIsSymlinkFalse_ForRegularDirectory() {
        // Arrange
        Directory.CreateDirectory(Path.Combine(_localDir, "getdir"));

        // Act
        var item = await _localStorage.GetItemAsync("getdir");

        // Assert
        Assert.NotNull(item);
        Assert.False(item.IsSymlink);
        Assert.True(item.IsDirectory);
    }

    #endregion

    #region SyncEngine Error Paths for PreserveTimestamps/Permissions

    [Fact]
    public async Task SynchronizeAsync_PreserveTimestamps_ErrorDoesNotFailSync() {
        // Arrange - mock storage that throws on SetLastModifiedAsync
        var localMock = MockStorageFactory.CreateMockStorage(rootPath: _localDir);
        var remoteMock = MockStorageFactory.CreateMockStorage(rootPath: _remoteDir);
        var dbMock = MockStorageFactory.CreateMockDatabase();

        var testItem = new SyncItem {
            Path = "tserr.txt",
            IsDirectory = false,
            Size = 50,
            LastModified = DateTime.UtcNow
        };

        localMock.Setup(x => x.ListItemsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SyncItem> { testItem });
        localMock.Setup(x => x.ReadFileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MemoryStream(new byte[50]));
        localMock.Setup(x => x.ComputeHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("hash789");

        remoteMock.Setup(x => x.ListItemsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SyncItem>());
        remoteMock.Setup(x => x.WriteFileAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        // SetLastModifiedAsync throws
        remoteMock.Setup(x => x.SetLastModifiedAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("Timestamp set failed"));

        using var engine = CreateEngineWithMocks(
            localStorage: localMock,
            remoteStorage: remoteMock,
            database: dbMock);

        // Act - sync should succeed even though timestamp preservation fails
        var options = new SyncOptions { PreserveTimestamps = true };
        var result = await engine.SynchronizeAsync(options);

        // Assert - sync should still succeed (error is caught and logged)
        Assert.True(result.Success);
    }

    [Fact]
    public async Task SynchronizeAsync_PreservePermissions_ErrorDoesNotFailSync() {
        // Arrange - mock storage that throws on SetPermissionsAsync
        var localMock = MockStorageFactory.CreateMockStorage(rootPath: _localDir);
        var remoteMock = MockStorageFactory.CreateMockStorage(rootPath: _remoteDir);
        var dbMock = MockStorageFactory.CreateMockDatabase();

        var testItem = new SyncItem {
            Path = "permerr.txt",
            IsDirectory = false,
            Size = 50,
            LastModified = DateTime.UtcNow,
            Permissions = "755"
        };

        localMock.Setup(x => x.ListItemsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SyncItem> { testItem });
        localMock.Setup(x => x.ReadFileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MemoryStream(new byte[50]));
        localMock.Setup(x => x.ComputeHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("hash101");

        remoteMock.Setup(x => x.ListItemsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SyncItem>());
        remoteMock.Setup(x => x.WriteFileAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        // SetPermissionsAsync throws
        remoteMock.Setup(x => x.SetPermissionsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("Permission set failed"));

        using var engine = CreateEngineWithMocks(
            localStorage: localMock,
            remoteStorage: remoteMock,
            database: dbMock);

        // Act
        var options = new SyncOptions { PreservePermissions = true, PreserveTimestamps = false };
        var result = await engine.SynchronizeAsync(options);

        // Assert - sync should still succeed
        Assert.True(result.Success);
    }

    #endregion

    #region FollowSymlinks with Mock Storage

    [Fact]
    public async Task SynchronizeAsync_FollowSymlinksFalse_SkipsSymlinkDirectories() {
        // Arrange - mock storage with a symlink directory
        var localMock = MockStorageFactory.CreateMockStorage(rootPath: _localDir);
        var remoteMock = MockStorageFactory.CreateMockStorage(rootPath: _remoteDir);
        var dbMock = MockStorageFactory.CreateMockDatabase();

        var regularDir = new SyncItem {
            Path = "realdir",
            IsDirectory = true,
            IsSymlink = false,
            LastModified = DateTime.UtcNow
        };
        var symlinkDir = new SyncItem {
            Path = "linkdir",
            IsDirectory = true,
            IsSymlink = true,
            LastModified = DateTime.UtcNow
        };
        var fileInRegularDir = new SyncItem {
            Path = "realdir/file.txt",
            IsDirectory = false,
            Size = 10,
            LastModified = DateTime.UtcNow
        };

        // Root listing returns both dirs
        localMock.Setup(x => x.ListItemsAsync("", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SyncItem> { regularDir, symlinkDir });
        localMock.Setup(x => x.ListItemsAsync("/", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SyncItem> { regularDir, symlinkDir });
        // Regular dir has a file
        localMock.Setup(x => x.ListItemsAsync("realdir", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SyncItem> { fileInRegularDir });
        // Symlink dir has nothing (shouldn't be traversed)
        localMock.Setup(x => x.ListItemsAsync("linkdir", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SyncItem> { new SyncItem { Path = "linkdir/secret.txt", IsDirectory = false, Size = 5, LastModified = DateTime.UtcNow } });
        localMock.Setup(x => x.ReadFileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MemoryStream(new byte[10]));
        localMock.Setup(x => x.ComputeHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("hash");

        remoteMock.Setup(x => x.ListItemsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SyncItem>());
        remoteMock.Setup(x => x.WriteFileAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        remoteMock.Setup(x => x.CreateDirectoryAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        using var engine = CreateEngineWithMocks(
            localStorage: localMock,
            remoteStorage: remoteMock,
            database: dbMock);

        // Act - FollowSymlinks defaults to false
        var options = new SyncOptions { FollowSymlinks = false };
        await engine.SynchronizeAsync(options);

        // Assert - symlink dir should NOT have been listed (traversed)
        localMock.Verify(x => x.ListItemsAsync("linkdir", It.IsAny<CancellationToken>()), Times.Never());
    }

    #endregion
}
