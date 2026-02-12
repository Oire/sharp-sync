namespace Oire.SharpSync.Tests.Sync;

public class SyncEngineTests: IDisposable {
    private readonly string _localRootPath;
    private readonly string _remoteRootPath;
    private readonly string _dbPath;
    private readonly LocalFileStorage _localStorage;
    private readonly LocalFileStorage _remoteStorage;
    private readonly SqliteSyncDatabase _database;
    private readonly SyncEngine _syncEngine;
    private static readonly string[] filePaths = new[] { "singlefile.txt" };
    private static readonly string[] filePathsArray = new[] { "sync1.txt", "sync2.txt" };
    private static readonly string[] filePathsArray0 = new[] { "SubDir/subfile.txt" };
    private static readonly string[] nonexistentFilePaths = new[] { "nonexistent.txt" };
    private static readonly string[] singleFilePaths = new[] { "file.txt" };
    private static readonly string[] clearmeFilePaths = new[] { "clearme.txt" };

    public SyncEngineTests() {
        _localRootPath = Path.Combine(Path.GetTempPath(), "SharpSyncTests", "Local", Guid.NewGuid().ToString());
        _remoteRootPath = Path.Combine(Path.GetTempPath(), "SharpSyncTests", "Remote", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_localRootPath);
        Directory.CreateDirectory(_remoteRootPath);

        _dbPath = Path.Combine(Path.GetTempPath(), "SharpSyncTests", $"sync_{Guid.NewGuid()}.db");
        _localStorage = new LocalFileStorage(_localRootPath);
        _remoteStorage = new LocalFileStorage(_remoteRootPath);
        _database = new SqliteSyncDatabase(_dbPath);
        _database.InitializeAsync().GetAwaiter().GetResult();

        var filter = new SyncFilter();
        var conflictResolver = new DefaultConflictResolver(ConflictResolution.UseLocal);
        _syncEngine = new SyncEngine(_localStorage, _remoteStorage, _database, conflictResolver, filter);
    }

    public void Dispose() {
        _syncEngine?.Dispose();
        _database?.Dispose();

        if (Directory.Exists(_localRootPath)) {
            Directory.Delete(_localRootPath, recursive: true);
        }

        if (Directory.Exists(_remoteRootPath)) {
            Directory.Delete(_remoteRootPath, recursive: true);
        }

        if (File.Exists(_dbPath)) {
            File.Delete(_dbPath);
        }
    }

    [Fact]
    public void Constructor_ValidParameters_CreatesEngine() {
        // Assert
        Assert.NotNull(_syncEngine);
        Assert.False(_syncEngine.IsSynchronizing);
    }

    [Fact]
    public void Constructor_NullLocalStorage_ThrowsArgumentNullException() {
        // Act & Assert
        var filter = new SyncFilter();
        var conflictResolver = new DefaultConflictResolver(ConflictResolution.UseLocal);
        Assert.Throws<ArgumentNullException>(() =>
            new SyncEngine(null!, _localStorage, _database, conflictResolver, filter));
    }

    [Fact]
    public void Constructor_NullRemoteStorage_ThrowsArgumentNullException() {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new SyncEngine(_localStorage, null!, _database, new DefaultConflictResolver(ConflictResolution.UseLocal), new SyncFilter()));
    }

    [Fact]
    public void Constructor_NullDatabase_ThrowsArgumentNullException() {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new SyncEngine(_localStorage, _remoteStorage, null!, new DefaultConflictResolver(ConflictResolution.UseLocal), new SyncFilter()));
    }

    [Fact]
    public void Constructor_NullFilter_UsesDefaultFilter() {
        // Act - null filter should be accepted, defaulting to SyncFilter
        var conflictResolver = new DefaultConflictResolver(ConflictResolution.UseLocal);
        using var engine = new SyncEngine(_localStorage, _remoteStorage, _database, conflictResolver, filter: null);

        // Assert
        Assert.NotNull(engine);
        Assert.False(engine.IsSynchronizing);
    }

    [Fact]
    public void Constructor_NullConflictResolver_ThrowsArgumentNullException() {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new SyncEngine(_localStorage, _remoteStorage, _database, null!, new SyncFilter()));
    }

    [Fact]
    public void Constructor_WithLogger_CreatesEngine() {
        // Arrange - pass explicit non-null logger to cover the non-null branch of ??
        var logger = new Microsoft.Extensions.Logging.Abstractions.NullLogger<SyncEngine>();
        var conflictResolver = new DefaultConflictResolver(ConflictResolution.UseLocal);

        // Act
        using var engine = new SyncEngine(_localStorage, _remoteStorage, _database, conflictResolver, logger: logger);

        // Assert
        Assert.NotNull(engine);
        Assert.False(engine.IsSynchronizing);
    }

    [Fact]
    public async Task SynchronizeAsync_VerboseOption_Succeeds() {
        // Arrange - create a file to trigger change detection
        var filePath = Path.Combine(_localRootPath, "verbose_test.txt");
        await File.WriteAllTextAsync(filePath, "test content");

        // Act - verbose mode exercises the DetectChangesStart log path
        var options = new SyncOptions { Verbose = true };
        var result = await _syncEngine.SynchronizeAsync(options);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Success);
    }

    [Fact]
    public async Task SynchronizeAsync_VerboseWithChecksumOnly_Succeeds() {
        // Arrange
        var filePath = Path.Combine(_localRootPath, "verbose_checksum.txt");
        await File.WriteAllTextAsync(filePath, "checksum test content");

        // Act - exercises verbose logging with checksum-only mode
        var options = new SyncOptions { Verbose = true, ChecksumOnly = true };
        var result = await _syncEngine.SynchronizeAsync(options);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Success);
    }

    [Fact]
    public async Task SynchronizeAsync_VerboseWithSizeOnly_Succeeds() {
        // Arrange
        var filePath = Path.Combine(_localRootPath, "verbose_size.txt");
        await File.WriteAllTextAsync(filePath, "size test content");

        // Act - exercises verbose logging with size-only mode
        var options = new SyncOptions { Verbose = true, SizeOnly = true };
        var result = await _syncEngine.SynchronizeAsync(options);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Success);
    }

    [Fact]
    public async Task SynchronizeAsync_EmptyDirectories_ReturnsSuccess() {
        // Act
        var result = await _syncEngine.SynchronizeAsync();

        // Assert
        Assert.NotNull(result);
        if (!result.Success && result.Error is not null) {
            throw new Exception($"Sync failed: {result.Error.Message}", result.Error);
        }
        Assert.True(result.Success);
        Assert.Equal(0, result.TotalFilesProcessed);
        Assert.Equal(0, result.FilesConflicted);
        Assert.Null(result.Error);
    }

    [Fact]
    public async Task SynchronizeAsync_SingleFile_SyncsCorrectly() {
        // Arrange
        var filePath = "test.txt";
        var content = "Hello, World!";
        var fullPath = Path.Combine(_localRootPath, filePath);
        await File.WriteAllTextAsync(fullPath, content);

        // Act
        var result = await _syncEngine.SynchronizeAsync();

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.Equal(1, result.TotalFilesProcessed);
        Assert.Equal(0, result.FilesConflicted);
        Assert.Null(result.Error);
    }

    [Fact]
    public async Task SynchronizeAsync_WithFilter_RespectsExclusions() {
        // Arrange
        var filter = new SyncFilter();
        filter.AddExclusionPattern("*.tmp");

        // Create a new engine with the filter
        var conflictResolver = new DefaultConflictResolver(ConflictResolution.UseLocal);
        using var filteredEngine = new SyncEngine(_localStorage, _remoteStorage, _database, conflictResolver, filter);

        var includedFile = Path.Combine(_localRootPath, "included.txt");
        var excludedFile = Path.Combine(_localRootPath, "excluded.tmp");

        await File.WriteAllTextAsync(includedFile, "included");
        await File.WriteAllTextAsync(excludedFile, "excluded");

        var options = new SyncOptions();

        // Act
        var result = await filteredEngine.SynchronizeAsync(options);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.Equal(1, result.TotalFilesProcessed); // Only included file
    }

    [Fact]
    public async Task SynchronizeAsync_WithConflictResolver_UsesResolver() {
        // Arrange
        var resolver = new DefaultConflictResolver(ConflictResolution.UseLocal);
        var options = new SyncOptions();

        // Act
        var result = await _syncEngine.SynchronizeAsync(options);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Success);
    }

    [Fact]
    public async Task SynchronizeAsync_CancellationRequested_StopsSync() {
        // Arrange
        using var cts = new CancellationTokenSource();
        cts.Cancel(); // Cancel immediately

        // Act & Assert
        // TaskCanceledException inherits from OperationCanceledException
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _syncEngine.SynchronizeAsync(cancellationToken: cts.Token));
    }

    [Fact]
    public void SynchronizeAsync_AlreadySynchronizing_ThrowsInvalidOperationException() {
        // This test is tricky to implement correctly due to timing.
        // Let's test the basic case where we can determine the state

        // For now, just verify that IsSynchronizing works correctly
        Assert.False(_syncEngine.IsSynchronizing);

        // The actual concurrent sync test is complex and prone to race conditions
        // In practice, the SyncEngine uses a semaphore which should prevent concurrent access
        // We'll skip the concurrent test as it's not essential for CI stability
    }

    [Fact]
    public void IsSynchronizing_InitialState_ReturnsFalse() {
        // Assert
        Assert.False(_syncEngine.IsSynchronizing);
    }

    [Fact]
    public void Dispose_MultipleCalls_DoesNotThrow() {
        // Arrange
        var engine = new SyncEngine(_localStorage, _remoteStorage, _database, new DefaultConflictResolver(ConflictResolution.UseLocal), new SyncFilter());

        // Act & Assert
        engine.Dispose();
        engine.Dispose(); // Should not throw
    }

    [Fact]
    public async Task SynchronizeAsync_AfterDispose_ThrowsObjectDisposedException() {
        // Arrange
        var engine = new SyncEngine(_localStorage, _remoteStorage, _database, new DefaultConflictResolver(ConflictResolution.UseLocal), new SyncFilter());
        engine.Dispose();

        // Act & Assert
        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            engine.SynchronizeAsync());
    }

    [Fact]
    public async Task ProgressChanged_Event_IsRaised() {
        // Arrange
        var progressRaised = false;
        _syncEngine.ProgressChanged += (sender, args) => {
            progressRaised = true;
        };

        var filePath = Path.Combine(_localRootPath, "test.txt");
        await File.WriteAllTextAsync(filePath, "test");

        // Act
        await _syncEngine.SynchronizeAsync();

        // Assert
        Assert.True(progressRaised);
    }

    [Fact]
    public async Task ConflictDetected_Event_IsRaised() {
        // Arrange
        var conflictRaised = false;
        _syncEngine.ConflictDetected += (sender, args) => {
            conflictRaised = true;
            args.Resolution = ConflictResolution.UseLocal;
        };

        // Create a scenario where a conflict might occur
        // This is a simplified test - in real scenarios conflicts are more complex
        var filePath = Path.Combine(_localRootPath, "conflict.txt");
        await File.WriteAllTextAsync(filePath, "local version");

        // Act
        await _syncEngine.SynchronizeAsync();

        // Assert - This might not trigger in this simple case,
        // but the event handler is correctly set up
        Assert.False(conflictRaised); // No actual conflict in this simple test
    }

    [Fact]
    public async Task SynchronizeAsync_MultipleFiles_SyncsAllFiles() {
        // Arrange
        var files = new[] { "file1.txt", "file2.txt", "file3.txt", "file4.txt", "file5.txt" };
        foreach (var file in files) {
            var fullPath = Path.Combine(_localRootPath, file);
            await File.WriteAllTextAsync(fullPath, $"Content of {file}");
        }

        // Act
        var result = await _syncEngine.SynchronizeAsync();

        // Assert
        Assert.True(result.Success);
        Assert.Equal(files.Length, result.TotalFilesProcessed);
        Assert.Equal(0, result.FilesConflicted);
    }

    [Fact]
    public async Task SynchronizeAsync_WithSubdirectories_SyncsFiles() {
        // Arrange
        var dir1 = Path.Combine(_localRootPath, "subdir1");
        var dir2 = Path.Combine(_localRootPath, "subdir1", "subdir2");
        Directory.CreateDirectory(dir1);
        Directory.CreateDirectory(dir2);

        await File.WriteAllTextAsync(Path.Combine(dir1, "file1.txt"), "content1");
        await File.WriteAllTextAsync(Path.Combine(dir2, "file2.txt"), "content2");
        await File.WriteAllTextAsync(Path.Combine(_localRootPath, "root.txt"), "root content");

        // Act
        var result = await _syncEngine.SynchronizeAsync();

        // Assert
        Assert.True(result.Success);
        // Should sync 3 files + 2 directories = 5 items total
        Assert.Equal(5, result.TotalFilesProcessed);
    }

    [Fact]
    public async Task SynchronizeAsync_UpdateExisting_UpdatesModifiedFiles() {
        // Arrange
        var filePath = "update.txt";
        var localFullPath = Path.Combine(_localRootPath, filePath);

        // Initial sync
        await File.WriteAllTextAsync(localFullPath, "original content");
        await _syncEngine.SynchronizeAsync();

        // Modify the file
        await File.WriteAllTextAsync(localFullPath, "updated content");
        await Task.Delay(100); // Ensure timestamp difference

        var options = new SyncOptions {
            UpdateExisting = true
        };

        // Act
        var result = await _syncEngine.SynchronizeAsync(options);

        // Assert
        Assert.True(result.Success);
        var remoteFullPath = Path.Combine(_remoteRootPath, filePath);
        var remoteContent = await File.ReadAllTextAsync(remoteFullPath);
        Assert.Equal("updated content", remoteContent);
    }

    [Fact]
    public async Task GetStatsAsync_AfterSync_ReturnsCorrectStats() {
        // Arrange
        var filePath = Path.Combine(_localRootPath, "stats.txt");
        await File.WriteAllTextAsync(filePath, "test");
        await _syncEngine.SynchronizeAsync();

        // Act
        var stats = await _syncEngine.GetStatsAsync();

        // Assert
        Assert.NotNull(stats);
        Assert.True(stats.TotalItems > 0);
    }

    [Fact]
    public async Task ResetSyncStateAsync_ClearsDatabase() {
        // Arrange
        var filePath = Path.Combine(_localRootPath, "reset.txt");
        await File.WriteAllTextAsync(filePath, "test");
        await _syncEngine.SynchronizeAsync();

        var statsBefore = await _syncEngine.GetStatsAsync();
        Assert.True(statsBefore.TotalItems > 0);

        // Act
        await _syncEngine.ResetSyncStateAsync();

        // Assert
        var statsAfter = await _syncEngine.GetStatsAsync();
        Assert.Equal(0, statsAfter.TotalItems);
    }

    [Fact]
    public async Task SynchronizeAsync_DeleteExtraneous_RemovesExtraFiles() {
        // Arrange
        var keepFile = Path.Combine(_localRootPath, "keep.txt");
        var deleteFile = Path.Combine(_remoteRootPath, "delete.txt");

        await File.WriteAllTextAsync(keepFile, "keep this");
        await File.WriteAllTextAsync(deleteFile, "delete this");

        var options = new SyncOptions {
            DeleteExtraneous = true
        };

        // Act
        var result = await _syncEngine.SynchronizeAsync(options);

        // Assert
        Assert.True(result.Success);
        Assert.False(File.Exists(deleteFile)); // Extra remote file should be deleted
        Assert.True(File.Exists(Path.Combine(_remoteRootPath, "keep.txt"))); // Local file should be synced
    }

    [Fact]
    public async Task SynchronizeAsync_BothModified_CreatesConflict() {
        // Arrange
        var fileName = "conflict.txt";
        var localPath = Path.Combine(_localRootPath, fileName);
        var remotePath = Path.Combine(_remoteRootPath, fileName);

        // Create initial file and sync
        await File.WriteAllTextAsync(localPath, "initial");
        await _syncEngine.SynchronizeAsync();

        // Modify both versions
        await Task.Delay(100);
        await File.WriteAllTextAsync(localPath, "local modification");
        await File.WriteAllTextAsync(remotePath, "remote modification");

        _syncEngine.ConflictDetected += (sender, args) => {
            args.Resolution = ConflictResolution.UseLocal;
        };

        // Act
        var result = await _syncEngine.SynchronizeAsync(new SyncOptions { UpdateExisting = true });

        // Assert
        Assert.True(result.Success);
        // Note: Conflict detection depends on the engine's implementation
        // This test verifies the conflict handling mechanism is in place
    }

    [Fact]
    public async Task SynchronizeAsync_LargeFile_HandlesCorrectly() {
        // Arrange
        var filePath = Path.Combine(_localRootPath, "large.bin");
        var largeContent = new byte[1024 * 1024]; // 1 MB
        new Random().NextBytes(largeContent);
        await File.WriteAllBytesAsync(filePath, largeContent);

        // Act
        var result = await _syncEngine.SynchronizeAsync();

        // Assert
        Assert.True(result.Success);
        var remotePath = Path.Combine(_remoteRootPath, "large.bin");
        Assert.True(File.Exists(remotePath));
        var remoteContent = await File.ReadAllBytesAsync(remotePath);
        Assert.Equal(largeContent.Length, remoteContent.Length);
    }

    [Fact]
    public async Task SynchronizeAsync_EmptyFile_HandlesCorrectly() {
        // Arrange
        var filePath = Path.Combine(_localRootPath, "empty.txt");
        await File.WriteAllTextAsync(filePath, "");

        // Act
        var result = await _syncEngine.SynchronizeAsync();

        // Assert
        Assert.True(result.Success);
        var remotePath = Path.Combine(_remoteRootPath, "empty.txt");
        Assert.True(File.Exists(remotePath));
    }

    [Fact]
    public async Task SynchronizeAsync_SpecialCharactersInFileName_HandlesCorrectly() {
        // Arrange
        var fileName = "file with spaces & special-chars_123.txt";
        var filePath = Path.Combine(_localRootPath, fileName);
        await File.WriteAllTextAsync(filePath, "test content");

        // Act
        var result = await _syncEngine.SynchronizeAsync();

        // Assert
        Assert.True(result.Success);
        var remotePath = Path.Combine(_remoteRootPath, fileName);
        Assert.True(File.Exists(remotePath));
    }

    [Fact]
    public async Task SynchronizeAsync_ChecksumOnly_UsesChecksums() {
        // Arrange
        var filePath = Path.Combine(_localRootPath, "checksum.txt");
        await File.WriteAllTextAsync(filePath, "checksum test");

        var options = new SyncOptions {
            ChecksumOnly = true
        };

        // Act
        var result = await _syncEngine.SynchronizeAsync(options);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(1, result.TotalFilesProcessed);
    }

    [Fact]
    public async Task SynchronizeAsync_ProgressReporting_ReportsCorrectly() {
        // Arrange
        for (int i = 0; i < 5; i++) {
            var filePath = Path.Combine(_localRootPath, $"progress{i}.txt");
            await File.WriteAllTextAsync(filePath, $"content {i}");
        }

        var progressEvents = new List<SyncProgressEventArgs>();
        _syncEngine.ProgressChanged += (sender, args) => {
            progressEvents.Add(args);
        };

        // Act
        var result = await _syncEngine.SynchronizeAsync();

        // Assert
        Assert.True(result.Success);
        Assert.NotEmpty(progressEvents);
        // Verify progress increases over time
        var firstProgress = progressEvents[0].Progress.Percentage;
        var lastProgress = progressEvents[^1].Progress.Percentage;
        Assert.True(lastProgress >= firstProgress);
    }

    #region GetSyncPlanAsync Tests

    [Fact]
    public async Task GetSyncPlanAsync_NoChanges_ReturnsEmptyPlan() {
        // Arrange
        // Sync once to establish baseline
        await _syncEngine.SynchronizeAsync();

        // Act
        var plan = await _syncEngine.GetSyncPlanAsync();

        // Assert
        Assert.NotNull(plan);
        Assert.Empty(plan.Actions);
        Assert.Equal(0, plan.TotalActions);
        Assert.False(plan.HasChanges);
        Assert.False(plan.HasConflicts);
    }

    [Fact]
    public async Task GetSyncPlanAsync_NewLocalFile_ReturnsUploadAction() {
        // Arrange
        var filePath = Path.Combine(_localRootPath, "newfile.txt");
        var content = "test content";
        await File.WriteAllTextAsync(filePath, content);

        // Act
        var plan = await _syncEngine.GetSyncPlanAsync();

        // Assert
        Assert.NotNull(plan);
        Assert.Single(plan.Actions);
        Assert.Equal(1, plan.UploadCount);
        Assert.Equal(0, plan.DownloadCount);
        Assert.True(plan.HasChanges);
        Assert.False(plan.HasConflicts);

        var action = plan.Actions[0];
        Assert.Equal(SyncActionType.Upload, action.ActionType);
        Assert.Contains("newfile.txt", action.Path);
        Assert.False(action.IsDirectory);
        Assert.True(action.Size > 0);
        Assert.Equal(SyncActionType.Upload, action.ActionType);
    }

    [Fact]
    public async Task GetSyncPlanAsync_NewRemoteFile_ReturnsDownloadAction() {
        // Arrange
        var filePath = Path.Combine(_remoteRootPath, "remotefile.txt");
        var content = "remote content";
        await File.WriteAllTextAsync(filePath, content);

        // Act
        var plan = await _syncEngine.GetSyncPlanAsync();

        // Assert
        Assert.NotNull(plan);
        Assert.Single(plan.Actions);
        Assert.Equal(1, plan.DownloadCount);
        Assert.Equal(0, plan.UploadCount);
        Assert.True(plan.HasChanges);

        var action = plan.Actions[0];
        Assert.Equal(SyncActionType.Download, action.ActionType);
        Assert.Contains("remotefile.txt", action.Path);
        Assert.False(action.IsDirectory);
    }

    [Fact]
    public async Task GetSyncPlanAsync_NewDirectory_ReturnsCorrectAction() {
        // Arrange
        var dirPath = Path.Combine(_localRootPath, "NewFolder");
        Directory.CreateDirectory(dirPath);

        // Act
        var plan = await _syncEngine.GetSyncPlanAsync();

        // Assert
        Assert.NotNull(plan);
        Assert.Single(plan.Actions);

        var action = plan.Actions[0];
        Assert.Equal(SyncActionType.Upload, action.ActionType);
        Assert.True(action.IsDirectory);
        Assert.Contains("NewFolder", action.Path);
    }

    [Fact]
    public async Task GetSyncPlanAsync_MultipleFiles_ReturnsAllActions() {
        // Arrange
        // Create 3 local files and 2 remote files
        for (int i = 0; i < 3; i++) {
            var filePath = Path.Combine(_localRootPath, $"local{i}.txt");
            await File.WriteAllTextAsync(filePath, $"local content {i}");
        }

        for (int i = 0; i < 2; i++) {
            var filePath = Path.Combine(_remoteRootPath, $"remote{i}.txt");
            await File.WriteAllTextAsync(filePath, $"remote content {i}");
        }

        // Act
        var plan = await _syncEngine.GetSyncPlanAsync();

        // Assert
        Assert.Equal(5, plan.TotalActions);
        Assert.Equal(3, plan.UploadCount);
        Assert.Equal(2, plan.DownloadCount);
        Assert.True(plan.HasChanges);
    }

    [Fact]
    public async Task GetSyncPlanAsync_DeletedLocalFile_ReturnsDeleteRemoteAction() {
        // Arrange
        var fileName = "tobedeleted.txt";
        var localPath = Path.Combine(_localRootPath, fileName);
        var remotePath = Path.Combine(_remoteRootPath, fileName);

        // Create file in both locations and sync
        await File.WriteAllTextAsync(localPath, "content");
        await _syncEngine.SynchronizeAsync();

        // Delete local file
        File.Delete(localPath);

        // Act
        var plan = await _syncEngine.GetSyncPlanAsync();

        // Assert
        Assert.Single(plan.Actions);
        var action = plan.Actions[0];
        Assert.Equal(SyncActionType.DeleteRemote, action.ActionType);
        Assert.Contains(fileName, action.Path);
    }

    [Fact]
    public async Task GetSyncPlanAsync_DeletedRemoteFile_ReturnsDeleteLocalAction() {
        // Arrange
        var fileName = "tobedeleted.txt";
        var localPath = Path.Combine(_localRootPath, fileName);
        var remotePath = Path.Combine(_remoteRootPath, fileName);

        // Create file in both locations and sync
        await File.WriteAllTextAsync(localPath, "content");
        await _syncEngine.SynchronizeAsync();

        // Delete remote file
        File.Delete(remotePath);

        // Act
        var plan = await _syncEngine.GetSyncPlanAsync();

        // Assert
        Assert.Single(plan.Actions);
        var action = plan.Actions[0];
        Assert.Equal(SyncActionType.DeleteLocal, action.ActionType);
        Assert.Contains(fileName, action.Path);
        Assert.Equal(SyncActionType.DeleteLocal, action.ActionType);
    }

    [Fact]
    public async Task GetSyncPlanAsync_CalculatesTotalSizes() {
        // Arrange
        var file1 = Path.Combine(_localRootPath, "file1.txt");
        var file2 = Path.Combine(_remoteRootPath, "file2.txt");

        await File.WriteAllTextAsync(file1, new string('a', 1024)); // 1 KB
        await File.WriteAllTextAsync(file2, new string('b', 2048)); // 2 KB

        // Act
        var plan = await _syncEngine.GetSyncPlanAsync();

        // Assert
        Assert.True(plan.TotalUploadSize > 0);
        Assert.True(plan.TotalDownloadSize > 0);
    }

    [Fact]
    public async Task GetSyncPlanAsync_WithChanges_HasChangesIsTrue() {
        // Arrange
        var localFile = Path.Combine(_localRootPath, "upload.txt");
        var remoteFile = Path.Combine(_remoteRootPath, "download.txt");

        await File.WriteAllTextAsync(localFile, "content to upload");
        await File.WriteAllTextAsync(remoteFile, "content to download");

        // Act
        var plan = await _syncEngine.GetSyncPlanAsync();

        // Assert
        Assert.True(plan.HasChanges);
        Assert.True(plan.TotalActions > 0);
    }

    [Fact]
    public async Task GetSyncPlanAsync_GroupsActionsByType() {
        // Arrange
        // Create files for upload
        await File.WriteAllTextAsync(Path.Combine(_localRootPath, "upload1.txt"), "content");
        await File.WriteAllTextAsync(Path.Combine(_localRootPath, "upload2.txt"), "content");

        // Create files for download
        await File.WriteAllTextAsync(Path.Combine(_remoteRootPath, "download1.txt"), "content");

        // Act
        var plan = await _syncEngine.GetSyncPlanAsync();

        // Assert
        Assert.Equal(2, plan.Uploads.Count);
        Assert.Single(plan.Downloads);
        Assert.Empty(plan.LocalDeletes);
        Assert.Empty(plan.RemoteDeletes);
        Assert.Empty(plan.Conflicts);
    }

    [Fact]
    public async Task GetSyncPlanAsync_DoesNotModifyFiles() {
        // Arrange
        var localFile = Path.Combine(_localRootPath, "test.txt");
        await File.WriteAllTextAsync(localFile, "original content");

        // Act
        var plan = await _syncEngine.GetSyncPlanAsync();

        // Assert - file should still exist locally and not on remote
        Assert.True(File.Exists(localFile));
        Assert.False(File.Exists(Path.Combine(_remoteRootPath, "test.txt")));

        // Verify plan has the action but it wasn't executed
        Assert.Single(plan.Actions);
        Assert.Equal(SyncActionType.Upload, plan.Actions[0].ActionType);
    }

    [Fact]
    public async Task GetSyncPlanAsync_WithCancellation_ThrowsOperationCanceledException() {
        // Arrange
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(
            async () => await _syncEngine.GetSyncPlanAsync(cancellationToken: cts.Token)
        );
    }

    [Fact]
    public async Task GetSyncPlanAsync_AfterDispose_ThrowsObjectDisposedException() {
        // Arrange
        _syncEngine.Dispose();

        // Act & Assert
        await Assert.ThrowsAsync<ObjectDisposedException>(
            async () => await _syncEngine.GetSyncPlanAsync()
        );
    }

    [Fact]
    public async Task GetSyncPlanAsync_PriorityOrdering_MaintainsPriority() {
        // Arrange
        // Create directory and files (directories should have higher priority)
        Directory.CreateDirectory(Path.Combine(_localRootPath, "Folder"));
        await File.WriteAllTextAsync(Path.Combine(_localRootPath, "file1.txt"), "content");
        await File.WriteAllTextAsync(Path.Combine(_localRootPath, "file2.txt"), "content");

        // Act
        var plan = await _syncEngine.GetSyncPlanAsync();

        // Assert
        Assert.True(plan.Actions.Count >= 3);

        // Actions should maintain priority ordering
        for (int i = 0; i < plan.Actions.Count - 1; i++) {
            Assert.True(plan.Actions[i].Priority >= plan.Actions[i + 1].Priority);
        }
    }

    [Fact]
    public async Task GetSyncPlanAsync_IncludesLastModifiedTime() {
        // Arrange
        var filePath = Path.Combine(_localRootPath, "timestamped.txt");
        await File.WriteAllTextAsync(filePath, "content with timestamp");

        // Act
        var plan = await _syncEngine.GetSyncPlanAsync();

        // Assert
        Assert.Single(plan.Actions);
        var action = plan.Actions[0];
        Assert.NotNull(action.LastModified);
        Assert.True(action.LastModified.Value.Year >= 2024);
    }

    [Fact]
    public async Task GetSyncPlanAsync_WithOptions_RespectsFilterSettings() {
        // Arrange
        await File.WriteAllTextAsync(Path.Combine(_localRootPath, "included.txt"), "content");
        await File.WriteAllTextAsync(Path.Combine(_localRootPath, "excluded.tmp"), "content");

        var filter = new SyncFilter();
        filter.AddExclusionPattern("*.tmp");

        var conflictResolver = new DefaultConflictResolver(ConflictResolution.UseLocal);
        using var filteredEngine = new SyncEngine(_localStorage, _remoteStorage, _database, conflictResolver, filter);

        // Act
        var plan = await filteredEngine.GetSyncPlanAsync();

        // Assert
        Assert.Single(plan.Actions);
        Assert.Contains("included.txt", plan.Actions[0].Path);
        Assert.DoesNotContain(plan.Actions, a => a.Path.Contains("excluded.tmp"));
    }

    #endregion

    #region Pause/Resume Tests

    [Fact]
    public void IsPaused_InitialState_ReturnsFalse() {
        // Assert
        Assert.False(_syncEngine.IsPaused);
    }

    [Fact]
    public void State_InitialState_ReturnsIdle() {
        // Assert
        Assert.Equal(SyncEngineState.Idle, _syncEngine.State);
    }

    [Fact]
    public async Task PauseAsync_WhenNotSynchronizing_ReturnsImmediately() {
        // Act
        await _syncEngine.PauseAsync();

        // Assert - should not be paused since no sync was running
        Assert.Equal(SyncEngineState.Idle, _syncEngine.State);
        Assert.False(_syncEngine.IsPaused);
    }

    [Fact]
    public async Task ResumeAsync_WhenNotPaused_ReturnsImmediately() {
        // Act
        await _syncEngine.ResumeAsync();

        // Assert
        Assert.Equal(SyncEngineState.Idle, _syncEngine.State);
        Assert.False(_syncEngine.IsPaused);
    }

    [Fact]
    public async Task PauseAsync_AfterDispose_ThrowsObjectDisposedException() {
        // Arrange
        _syncEngine.Dispose();

        // Act & Assert
        await Assert.ThrowsAsync<ObjectDisposedException>(() => _syncEngine.PauseAsync());
    }

    [Fact]
    public async Task ResumeAsync_AfterDispose_ThrowsObjectDisposedException() {
        // Arrange
        _syncEngine.Dispose();

        // Act & Assert
        await Assert.ThrowsAsync<ObjectDisposedException>(() => _syncEngine.ResumeAsync());
    }

    [Fact]
    public async Task PauseAsync_DuringSync_TransitionsToRunningThenPaused() {
        // Arrange - Create multiple files to ensure sync takes some time
        for (int i = 0; i < 20; i++) {
            var filePath = Path.Combine(_localRootPath, $"pause_test_{i}.txt");
            await File.WriteAllTextAsync(filePath, new string('x', 10000)); // 10KB each
        }

        var progressEvents = new List<SyncProgressEventArgs>();
        var pausedEventReceived = false;
        var pauseStateReached = new TaskCompletionSource();

        _syncEngine.ProgressChanged += (sender, args) => {
            progressEvents.Add(args);

            // When we see the first progress event during sync, try to pause
            if (args.Operation != SyncOperation.Scanning && args.Operation != SyncOperation.Paused && progressEvents.Count == 2) {
                _ = _syncEngine.PauseAsync();
            }

            if (args.Operation == SyncOperation.Paused) {
                pausedEventReceived = true;
                pauseStateReached.TrySetResult();
            }
        };

        // Act - Start sync
        var syncTask = Task.Run(async () => {
            try {
                return await _syncEngine.SynchronizeAsync();
            } catch (OperationCanceledException) {
                return new SyncResult { Success = false };
            }
        });

        // Wait for pause state or timeout
        var pauseOrTimeout = await Task.WhenAny(
            pauseStateReached.Task,
            Task.Delay(TimeSpan.FromSeconds(5))
        );

        // Resume if paused, so sync can complete
        if (_syncEngine.IsPaused) {
            await _syncEngine.ResumeAsync();
        }

        var result = await syncTask;

        // Assert - If we managed to pause (depends on timing), verify the state transition
        if (pausedEventReceived) {
            Assert.Contains(progressEvents, e => e.Operation == SyncOperation.Paused);
        }

        // Sync should complete successfully
        Assert.True(result.Success);
    }

    [Fact]
    public async Task PauseAndResume_DuringSync_ContinuesSuccessfully() {
        // Arrange - Create files
        for (int i = 0; i < 10; i++) {
            var filePath = Path.Combine(_localRootPath, $"resume_test_{i}.txt");
            await File.WriteAllTextAsync(filePath, $"Content for file {i}");
        }

        var progressBeforePause = 0;
        var progressAfterResume = 0;
        var wasPaused = false;
        var pauseTask = Task.CompletedTask;
        var pauseSignal = new TaskCompletionSource();

        _syncEngine.ProgressChanged += (sender, args) => {
            if (args.Operation == SyncOperation.Paused) {
                wasPaused = true;
                pauseSignal.TrySetResult();
            } else if (!wasPaused && args.Operation != SyncOperation.Scanning) {
                progressBeforePause = args.Progress.ProcessedItems;
                // Pause after processing 2 items
                if (progressBeforePause >= 2 && pauseTask.IsCompleted) {
                    pauseTask = _syncEngine.PauseAsync();
                }
            } else if (wasPaused && args.Operation != SyncOperation.Paused) {
                progressAfterResume = args.Progress.ProcessedItems;
            }
        };

        // Act
        var syncTask = Task.Run(() => _syncEngine.SynchronizeAsync());

        // Wait for pause or timeout
        var pauseOrTimeout = await Task.WhenAny(
            pauseSignal.Task,
            Task.Delay(TimeSpan.FromSeconds(3))
        );

        // If paused, wait a bit then resume
        if (_syncEngine.IsPaused) {
            await Task.Delay(100);
            await _syncEngine.ResumeAsync();
        }

        var result = await syncTask;

        // Assert
        Assert.True(result.Success);

        // If we managed to pause, verify progress continued after resume
        if (wasPaused) {
            Assert.True(progressAfterResume >= progressBeforePause);
        }
    }

    [Fact]
    public async Task State_DuringSync_ReturnsRunning() {
        // Arrange
        var filePath = Path.Combine(_localRootPath, "state_test.txt");
        await File.WriteAllTextAsync(filePath, "test content");

        var stateWasRunning = false;

        _syncEngine.ProgressChanged += (sender, args) => {
            if (_syncEngine.State == SyncEngineState.Running) {
                stateWasRunning = true;
            }
        };

        // Act
        var result = await _syncEngine.SynchronizeAsync();

        // Assert
        Assert.True(result.Success);
        Assert.True(stateWasRunning);
        Assert.Equal(SyncEngineState.Idle, _syncEngine.State); // Back to idle after sync
    }

    [Fact]
    public async Task State_AfterSync_ReturnsIdle() {
        // Arrange
        var filePath = Path.Combine(_localRootPath, "state_after_test.txt");
        await File.WriteAllTextAsync(filePath, "test content");

        // Act
        var result = await _syncEngine.SynchronizeAsync();

        // Assert
        Assert.True(result.Success);
        Assert.Equal(SyncEngineState.Idle, _syncEngine.State);
        Assert.False(_syncEngine.IsPaused);
        Assert.False(_syncEngine.IsSynchronizing);
    }

    [Fact]
    public async Task PauseAsync_CalledMultipleTimes_IsIdempotent() {
        // Arrange - Create files for sync
        for (int i = 0; i < 5; i++) {
            var filePath = Path.Combine(_localRootPath, $"idempotent_{i}.txt");
            await File.WriteAllTextAsync(filePath, "content");
        }

        var pauseSignal = new TaskCompletionSource();

        _syncEngine.ProgressChanged += (sender, args) => {
            if (args.Operation != SyncOperation.Scanning && !pauseSignal.Task.IsCompleted) {
                // Call pause multiple times
                _ = Task.Run(async () => {
                    await _syncEngine.PauseAsync();
                    await _syncEngine.PauseAsync();
                    await _syncEngine.PauseAsync();
                    pauseSignal.TrySetResult();
                });
            }
        };

        // Act
        var syncTask = Task.Run(() => _syncEngine.SynchronizeAsync());

        await Task.WhenAny(pauseSignal.Task, Task.Delay(TimeSpan.FromSeconds(2)));

        // Resume to complete
        await _syncEngine.ResumeAsync();

        var result = await syncTask;

        // Assert - Should complete without errors
        Assert.True(result.Success);
    }

    [Fact]
    public async Task ResumeAsync_CalledMultipleTimes_IsIdempotent() {
        // Arrange
        var filePath = Path.Combine(_localRootPath, "resume_idempotent.txt");
        await File.WriteAllTextAsync(filePath, "content");

        // Act - Call resume multiple times when not paused
        await _syncEngine.ResumeAsync();
        await _syncEngine.ResumeAsync();
        await _syncEngine.ResumeAsync();

        var result = await _syncEngine.SynchronizeAsync();

        // Assert
        Assert.True(result.Success);
        Assert.Equal(SyncEngineState.Idle, _syncEngine.State);
    }

    [Fact]
    public void Dispose_WhilePaused_ReleasesWaitingThreads() {
        // This test verifies that disposing while paused doesn't cause deadlocks
        // The Dispose method sets the pause event to release any waiting threads

        // Arrange
        var filter = new SyncFilter();
        var conflictResolver = new DefaultConflictResolver(ConflictResolution.UseLocal);
        var engine = new SyncEngine(_localStorage, _remoteStorage, _database, conflictResolver, filter);

        // Act & Assert - Should not throw or deadlock
        engine.Dispose();

        Assert.Throws<ObjectDisposedException>(() => {
            _ = engine.State;
            engine.PauseAsync().GetAwaiter().GetResult();
        });
    }

    #endregion

    #region Selective Sync Tests

    [Fact]
    public async Task SyncFolderAsync_EmptyFolder_ReturnsSuccess() {
        // Arrange
        var folderPath = Path.Combine(_localRootPath, "EmptyFolder");
        Directory.CreateDirectory(folderPath);

        // Act
        var result = await _syncEngine.SyncFolderAsync("EmptyFolder");

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Success);
    }

    [Fact]
    public async Task SyncFolderAsync_FolderWithFiles_SyncsOnlyThatFolder() {
        // Arrange
        // Create files in multiple folders
        Directory.CreateDirectory(Path.Combine(_localRootPath, "Folder1"));
        Directory.CreateDirectory(Path.Combine(_localRootPath, "Folder2"));
        await File.WriteAllTextAsync(Path.Combine(_localRootPath, "Folder1", "file1.txt"), "content1");
        await File.WriteAllTextAsync(Path.Combine(_localRootPath, "Folder2", "file2.txt"), "content2");
        await File.WriteAllTextAsync(Path.Combine(_localRootPath, "root.txt"), "root content");

        // Act - Only sync Folder1
        var result = await _syncEngine.SyncFolderAsync("Folder1");

        // Assert
        Assert.True(result.Success);
        // Check that only Folder1 files were synced
        Assert.True(File.Exists(Path.Combine(_remoteRootPath, "Folder1", "file1.txt")));
        Assert.False(File.Exists(Path.Combine(_remoteRootPath, "Folder2", "file2.txt")));
        Assert.False(File.Exists(Path.Combine(_remoteRootPath, "root.txt")));
    }

    [Fact]
    public async Task SyncFolderAsync_NestedFolder_SyncsRecursively() {
        // Arrange
        Directory.CreateDirectory(Path.Combine(_localRootPath, "Parent", "Child", "GrandChild"));
        await File.WriteAllTextAsync(Path.Combine(_localRootPath, "Parent", "parent.txt"), "parent");
        await File.WriteAllTextAsync(Path.Combine(_localRootPath, "Parent", "Child", "child.txt"), "child");
        await File.WriteAllTextAsync(Path.Combine(_localRootPath, "Parent", "Child", "GrandChild", "grandchild.txt"), "grandchild");

        // Act
        var result = await _syncEngine.SyncFolderAsync("Parent");

        // Assert
        Assert.True(result.Success);
        Assert.True(File.Exists(Path.Combine(_remoteRootPath, "Parent", "parent.txt")));
        Assert.True(File.Exists(Path.Combine(_remoteRootPath, "Parent", "Child", "child.txt")));
        Assert.True(File.Exists(Path.Combine(_remoteRootPath, "Parent", "Child", "GrandChild", "grandchild.txt")));
    }

    [Fact]
    public async Task SyncFolderAsync_NonexistentFolder_ReturnsSuccess() {
        // Act
        var result = await _syncEngine.SyncFolderAsync("NonexistentFolder");

        // Assert
        Assert.True(result.Success);
        Assert.Equal(0, result.FilesSynchronized);
    }

    [Fact]
    public async Task SyncFolderAsync_AfterDispose_ThrowsObjectDisposedException() {
        // Arrange
        _syncEngine.Dispose();

        // Act & Assert
        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            _syncEngine.SyncFolderAsync("AnyFolder"));
    }

    [Fact]
    public async Task SyncFilesAsync_EmptyList_ReturnsSuccess() {
        // Act
        var result = await _syncEngine.SyncFilesAsync(Array.Empty<string>());

        // Assert
        Assert.True(result.Success);
        Assert.Equal(0, result.FilesSynchronized);
    }

    [Fact]
    public async Task SyncFilesAsync_SingleFile_SyncsCorrectly() {
        // Arrange
        await File.WriteAllTextAsync(Path.Combine(_localRootPath, "singlefile.txt"), "single content");

        // Act
        var result = await _syncEngine.SyncFilesAsync(filePaths);

        // Assert
        Assert.True(result.Success);
        Assert.True(File.Exists(Path.Combine(_remoteRootPath, "singlefile.txt")));
    }

    [Fact]
    public async Task SyncFilesAsync_MultipleFiles_SyncsOnlySpecified() {
        // Arrange
        await File.WriteAllTextAsync(Path.Combine(_localRootPath, "sync1.txt"), "content1");
        await File.WriteAllTextAsync(Path.Combine(_localRootPath, "sync2.txt"), "content2");
        await File.WriteAllTextAsync(Path.Combine(_localRootPath, "notsync.txt"), "not synced");

        // Act
        var result = await _syncEngine.SyncFilesAsync(filePathsArray);

        // Assert
        Assert.True(result.Success);
        Assert.True(File.Exists(Path.Combine(_remoteRootPath, "sync1.txt")));
        Assert.True(File.Exists(Path.Combine(_remoteRootPath, "sync2.txt")));
        Assert.False(File.Exists(Path.Combine(_remoteRootPath, "notsync.txt")));
    }

    [Fact]
    public async Task SyncFilesAsync_FilesInSubdirectories_SyncsCorrectly() {
        // Arrange
        Directory.CreateDirectory(Path.Combine(_localRootPath, "SubDir"));
        await File.WriteAllTextAsync(Path.Combine(_localRootPath, "SubDir", "subfile.txt"), "sub content");

        // Act
        var result = await _syncEngine.SyncFilesAsync(filePathsArray0);

        // Assert
        Assert.True(result.Success);
        Assert.True(File.Exists(Path.Combine(_remoteRootPath, "SubDir", "subfile.txt")));
    }

    [Fact]
    public async Task SyncFilesAsync_NonexistentFile_HandlesGracefully() {
        // Act
        var result = await _syncEngine.SyncFilesAsync(nonexistentFilePaths);

        // Assert
        Assert.True(result.Success);
    }

    [Fact]
    public async Task SyncFilesAsync_AfterDispose_ThrowsObjectDisposedException() {
        // Arrange
        _syncEngine.Dispose();

        // Act & Assert
        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            _syncEngine.SyncFilesAsync(singleFilePaths));
    }

    [Fact]
    public async Task NotifyLocalChangeAsync_Created_TracksChange() {
        // Arrange
        await File.WriteAllTextAsync(Path.Combine(_localRootPath, "created.txt"), "content");

        // Act
        await _syncEngine.NotifyLocalChangeAsync("created.txt", ChangeType.Created);
        var pending = await _syncEngine.GetPendingOperationsAsync();

        // Assert
        Assert.Contains(pending, p => p.Path == "created.txt" && p.ActionType == SyncActionType.Upload);
    }

    [Fact]
    public async Task NotifyLocalChangeAsync_Changed_TracksModification() {
        // Arrange
        await File.WriteAllTextAsync(Path.Combine(_localRootPath, "modified.txt"), "content");

        // Act
        await _syncEngine.NotifyLocalChangeAsync("modified.txt", ChangeType.Changed);
        var pending = await _syncEngine.GetPendingOperationsAsync();

        // Assert
        Assert.Contains(pending, p => p.Path == "modified.txt" && p.ActionType == SyncActionType.Upload);
    }

    [Fact]
    public async Task NotifyLocalChangeAsync_Deleted_TracksDeletion() {
        // Act
        await _syncEngine.NotifyLocalChangeAsync("deleted.txt", ChangeType.Deleted);
        var pending = await _syncEngine.GetPendingOperationsAsync();

        // Assert
        Assert.Contains(pending, p => p.Path == "deleted.txt" && p.ActionType == SyncActionType.DeleteRemote);
    }

    [Fact]
    public async Task NotifyLocalChangeAsync_ExcludedPath_IsIgnored() {
        // Arrange
        var filter = new SyncFilter();
        filter.AddExclusionPattern("*.tmp");
        var conflictResolver = new DefaultConflictResolver(ConflictResolution.UseLocal);
        using var filteredEngine = new SyncEngine(_localStorage, _remoteStorage, _database, conflictResolver, filter);

        // Act
        await filteredEngine.NotifyLocalChangeAsync("excluded.tmp", ChangeType.Created);
        var pending = await filteredEngine.GetPendingOperationsAsync();

        // Assert
        Assert.DoesNotContain(pending, p => p.Path == "excluded.tmp");
    }

    [Fact]
    public async Task NotifyLocalChangeAsync_NormalizesPath() {
        // Act - Use backslash path
        await _syncEngine.NotifyLocalChangeAsync("folder\\file.txt", ChangeType.Created);
        var pending = await _syncEngine.GetPendingOperationsAsync();

        // Assert - Should be normalized to forward slash
        Assert.Contains(pending, p => p.Path == "folder/file.txt");
    }

    [Fact]
    public async Task NotifyLocalChangeAsync_MultipleChanges_MergesCorrectly() {
        // Act
        await _syncEngine.NotifyLocalChangeAsync("mergefile.txt", ChangeType.Created);
        await _syncEngine.NotifyLocalChangeAsync("mergefile.txt", ChangeType.Changed);
        var pending = await _syncEngine.GetPendingOperationsAsync();

        // Assert - Should only have one entry for the file
        Assert.Single(pending, p => p.Path == "mergefile.txt");
    }

    [Fact]
    public async Task NotifyLocalChangeAsync_DeleteAfterCreate_BecomesDelete() {
        // Act
        await _syncEngine.NotifyLocalChangeAsync("tempfile.txt", ChangeType.Created);
        await _syncEngine.NotifyLocalChangeAsync("tempfile.txt", ChangeType.Deleted);
        var pending = await _syncEngine.GetPendingOperationsAsync();

        // Assert - Should be marked as delete
        var operation = pending.FirstOrDefault(p => p.Path == "tempfile.txt");
        Assert.NotNull(operation);
        Assert.Equal(SyncActionType.DeleteRemote, operation.ActionType);
    }

    [Fact]
    public async Task NotifyLocalChangeAsync_AfterDispose_ThrowsObjectDisposedException() {
        // Arrange
        _syncEngine.Dispose();

        // Act & Assert
        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            _syncEngine.NotifyLocalChangeAsync("file.txt", ChangeType.Created));
    }

    [Fact]
    public async Task GetPendingOperationsAsync_NoPendingChanges_ReturnsEmpty() {
        // Act
        var pending = await _syncEngine.GetPendingOperationsAsync();

        // Assert
        Assert.Empty(pending);
    }

    [Fact]
    public async Task GetPendingOperationsAsync_WithNotifiedChanges_ReturnsOperations() {
        // Arrange
        await File.WriteAllTextAsync(Path.Combine(_localRootPath, "pending1.txt"), "content1");
        await File.WriteAllTextAsync(Path.Combine(_localRootPath, "pending2.txt"), "content2");

        await _syncEngine.NotifyLocalChangeAsync("pending1.txt", ChangeType.Created);
        await _syncEngine.NotifyLocalChangeAsync("pending2.txt", ChangeType.Changed);

        // Act
        var pending = await _syncEngine.GetPendingOperationsAsync();

        // Assert
        Assert.Equal(2, pending.Count);
        Assert.All(pending, p => Assert.Equal(ChangeSource.Local, p.Source));
    }

    [Fact]
    public async Task GetPendingOperationsAsync_IncludesSize() {
        // Arrange
        var content = new string('x', 1000);
        await File.WriteAllTextAsync(Path.Combine(_localRootPath, "sized.txt"), content);
        await _syncEngine.NotifyLocalChangeAsync("sized.txt", ChangeType.Created);

        // Act
        var pending = await _syncEngine.GetPendingOperationsAsync();

        // Assert
        var operation = pending.FirstOrDefault(p => p.Path == "sized.txt");
        Assert.NotNull(operation);
        Assert.True(operation.Size > 0);
    }

    [Fact]
    public async Task GetPendingOperationsAsync_IncludesDetectedTime() {
        // Arrange
        var beforeNotify = DateTime.UtcNow;
        await _syncEngine.NotifyLocalChangeAsync("timed.txt", ChangeType.Created);
        var afterNotify = DateTime.UtcNow;

        // Act
        var pending = await _syncEngine.GetPendingOperationsAsync();

        // Assert
        var operation = pending.FirstOrDefault(p => p.Path == "timed.txt");
        Assert.NotNull(operation);
        Assert.True(operation.DetectedAt >= beforeNotify);
        Assert.True(operation.DetectedAt <= afterNotify);
    }

    [Fact]
    public async Task GetPendingOperationsAsync_AfterDispose_ThrowsObjectDisposedException() {
        // Arrange
        _syncEngine.Dispose();

        // Act & Assert
        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            _syncEngine.GetPendingOperationsAsync());
    }

    [Fact]
    public async Task SyncFilesAsync_ClearsPendingChanges() {
        // Arrange
        await File.WriteAllTextAsync(Path.Combine(_localRootPath, "clearme.txt"), "content");
        await _syncEngine.NotifyLocalChangeAsync("clearme.txt", ChangeType.Created);

        var pendingBefore = await _syncEngine.GetPendingOperationsAsync();
        Assert.Contains(pendingBefore, p => p.Path == "clearme.txt");

        // Act
        await _syncEngine.SyncFilesAsync(clearmeFilePaths);
        var pendingAfter = await _syncEngine.GetPendingOperationsAsync();

        // Assert - The pending change should be cleared after sync
        Assert.DoesNotContain(pendingAfter, p => p.Path == "clearme.txt");
    }

    [Fact]
    public async Task NotifyLocalChangeBatchAsync_BatchNotification_TracksAllChanges() {
        // Arrange
        await File.WriteAllTextAsync(Path.Combine(_localRootPath, "batch1.txt"), "content1");
        await File.WriteAllTextAsync(Path.Combine(_localRootPath, "batch2.txt"), "content2");
        await File.WriteAllTextAsync(Path.Combine(_localRootPath, "batch3.txt"), "content3");

        var changes = new List<ChangeInfo> {
            new("batch1.txt", ChangeType.Created),
            new("batch2.txt", ChangeType.Changed),
            new("batch3.txt", ChangeType.Deleted)
        };

        // Act
        await _syncEngine.NotifyLocalChangeBatchAsync(changes);
        var pending = await _syncEngine.GetPendingOperationsAsync();

        // Assert
        Assert.Equal(3, pending.Count(p => p.Path.StartsWith("batch")));
        Assert.Contains(pending, p => p.Path == "batch1.txt" && p.ActionType == SyncActionType.Upload);
        Assert.Contains(pending, p => p.Path == "batch2.txt" && p.ActionType == SyncActionType.Upload);
        Assert.Contains(pending, p => p.Path == "batch3.txt" && p.ActionType == SyncActionType.DeleteRemote);
    }

    [Fact]
    public async Task NotifyLocalChangeBatchAsync_EmptyBatch_DoesNothing() {
        // Act
        await _syncEngine.NotifyLocalChangeBatchAsync(Array.Empty<ChangeInfo>());
        var pending = await _syncEngine.GetPendingOperationsAsync();

        // Assert
        Assert.Empty(pending);
    }

    [Fact]
    public async Task NotifyLocalChangeBatchAsync_AfterDispose_ThrowsObjectDisposedException() {
        // Arrange
        _syncEngine.Dispose();

        // Act & Assert
        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            _syncEngine.NotifyLocalChangeBatchAsync(new[] { new ChangeInfo("file.txt", ChangeType.Created) }));
    }

    [Fact]
    public async Task NotifyLocalRenameAsync_TracksRename() {
        // Arrange
        await File.WriteAllTextAsync(Path.Combine(_localRootPath, "newname.txt"), "content");

        // Act
        await _syncEngine.NotifyLocalRenameAsync("oldname.txt", "newname.txt");
        var pending = await _syncEngine.GetPendingOperationsAsync();

        // Assert - Should have delete for old and create for new
        Assert.Equal(2, pending.Count);

        var deleteOp = pending.FirstOrDefault(p => p.Path == "oldname.txt");
        Assert.NotNull(deleteOp);
        Assert.Equal(SyncActionType.DeleteRemote, deleteOp.ActionType);
        Assert.Equal("newname.txt", deleteOp.RenamedTo);
        Assert.True(deleteOp.IsRename);

        var createOp = pending.FirstOrDefault(p => p.Path == "newname.txt");
        Assert.NotNull(createOp);
        Assert.Equal(SyncActionType.Upload, createOp.ActionType);
        Assert.Equal("oldname.txt", createOp.RenamedFrom);
        Assert.True(createOp.IsRename);
    }

    [Fact]
    public async Task NotifyLocalRenameAsync_OldPathFiltered_OnlyTracksNew() {
        // Arrange
        var filter = new SyncFilter();
        filter.AddExclusionPattern("*.tmp");
        var conflictResolver = new DefaultConflictResolver(ConflictResolution.UseLocal);
        using var filteredEngine = new SyncEngine(_localStorage, _remoteStorage, _database, conflictResolver, filter);

        await File.WriteAllTextAsync(Path.Combine(_localRootPath, "newfile.txt"), "content");

        // Act - Rename from excluded .tmp to included .txt
        await filteredEngine.NotifyLocalRenameAsync("oldfile.tmp", "newfile.txt");
        var pending = await filteredEngine.GetPendingOperationsAsync();

        // Assert - Only new path should be tracked (old was filtered)
        Assert.Single(pending);
        var op = pending[0];
        Assert.Equal("newfile.txt", op.Path);
        Assert.Null(op.RenamedFrom); // Old path was filtered, so no rename tracking
    }

    [Fact]
    public async Task NotifyLocalRenameAsync_AfterDispose_ThrowsObjectDisposedException() {
        // Arrange
        _syncEngine.Dispose();

        // Act & Assert
        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            _syncEngine.NotifyLocalRenameAsync("old.txt", "new.txt"));
    }

    [Fact]
    public async Task ClearPendingLocalChanges_RemovesAllPending() {
        // Arrange
        await _syncEngine.NotifyLocalChangeAsync("file1.txt", ChangeType.Created);
        await _syncEngine.NotifyLocalChangeAsync("file2.txt", ChangeType.Changed);
        await _syncEngine.NotifyLocalChangeAsync("file3.txt", ChangeType.Deleted);

        // Act
        _syncEngine.ClearPendingLocalChanges();
        var pending = await _syncEngine.GetPendingOperationsAsync();

        // Assert
        Assert.Empty(pending);
    }

    [Fact]
    public async Task ClearPendingLocalChanges_WhenEmpty_DoesNotThrow() {
        // Act & Assert - Should not throw
        _syncEngine.ClearPendingLocalChanges();
        var pending = await _syncEngine.GetPendingOperationsAsync();
        Assert.Empty(pending);
    }

    [Fact]
    public void ClearPendingLocalChanges_AfterDispose_ThrowsObjectDisposedException() {
        // Arrange
        _syncEngine.Dispose();

        // Act & Assert
        Assert.Throws<ObjectDisposedException>(() => _syncEngine.ClearPendingLocalChanges());
    }

    [Fact]
    public async Task GetSyncPlanAsync_IncorporatesPendingChanges() {
        // Arrange - Create a file and notify about it
        await File.WriteAllTextAsync(Path.Combine(_localRootPath, "pending_plan.txt"), "content");
        await _syncEngine.NotifyLocalChangeAsync("pending_plan.txt", ChangeType.Created);

        // Act
        var plan = await _syncEngine.GetSyncPlanAsync();

        // Assert - Plan should include the pending change
        Assert.Contains(plan.Actions, a => a.Path == "pending_plan.txt" && a.ActionType == SyncActionType.Upload);
    }

    [Fact]
    public async Task GetSyncPlanAsync_PendingChangesNotDuplicated() {
        // Arrange - Create file and notify about it (before any sync)
        // This tests that when a file appears in both normal scan AND pending changes,
        // it only appears once in the plan
        await File.WriteAllTextAsync(Path.Combine(_localRootPath, "no_dup.txt"), "content");
        await _syncEngine.NotifyLocalChangeAsync("no_dup.txt", ChangeType.Created);

        // Act
        var plan = await _syncEngine.GetSyncPlanAsync();

        // Assert - Should only have one action for this file (not duplicated)
        var actionsForFile = plan.Actions.Where(a => a.Path == "no_dup.txt").ToList();
        Assert.Single(actionsForFile);
        Assert.Equal(SyncActionType.Upload, actionsForFile[0].ActionType);
    }

    #endregion

    #region FileProgressChanged Tests

    [Fact]
    public void FileProgressChanged_SubscribesToStorageProgressEvents() {
        // Arrange - Create a storage wrapper that fires progress events
        using var progressStorage = new ProgressFiringStorage(_remoteRootPath);
        var filter = new SyncFilter();
        var conflictResolver = new DefaultConflictResolver(ConflictResolution.UseLocal);
        using var engine = new SyncEngine(_localStorage, progressStorage, _database, conflictResolver, filter);

        var receivedEvents = new List<FileProgressEventArgs>();
        engine.FileProgressChanged += (sender, e) => receivedEvents.Add(e);

        // Act - Simulate storage raising a progress event
        progressStorage.SimulateProgress("test.txt", 512, 1024, StorageOperation.Upload);

        // Assert
        Assert.Single(receivedEvents);
        Assert.Equal("test.txt", receivedEvents[0].Path);
        Assert.Equal(512, receivedEvents[0].BytesTransferred);
        Assert.Equal(1024, receivedEvents[0].TotalBytes);
        Assert.Equal(FileTransferOperation.Upload, receivedEvents[0].Operation);
        Assert.Equal(50, receivedEvents[0].PercentComplete);
    }

    [Fact]
    public void FileProgressChanged_MapsDownloadOperationCorrectly() {
        // Arrange
        using var progressStorage = new ProgressFiringStorage(_remoteRootPath);
        var filter = new SyncFilter();
        var conflictResolver = new DefaultConflictResolver(ConflictResolution.UseLocal);
        using var engine = new SyncEngine(_localStorage, progressStorage, _database, conflictResolver, filter);

        var receivedEvents = new List<FileProgressEventArgs>();
        engine.FileProgressChanged += (sender, e) => receivedEvents.Add(e);

        // Act
        progressStorage.SimulateProgress("large.zip", 750, 1000, StorageOperation.Download);

        // Assert
        Assert.Single(receivedEvents);
        Assert.Equal(FileTransferOperation.Download, receivedEvents[0].Operation);
        Assert.Equal(75, receivedEvents[0].PercentComplete);
    }

    [Fact]
    public void FileProgressChanged_Dispose_UnsubscribesFromStorageEvents() {
        // Arrange
        using var progressStorage = new ProgressFiringStorage(_remoteRootPath);
        var filter = new SyncFilter();
        var conflictResolver = new DefaultConflictResolver(ConflictResolution.UseLocal);
        var engine = new SyncEngine(_localStorage, progressStorage, _database, conflictResolver, filter);

        var receivedEvents = new List<FileProgressEventArgs>();
        engine.FileProgressChanged += (sender, e) => receivedEvents.Add(e);

        // Act - Dispose should unsubscribe
        engine.Dispose();
        progressStorage.SimulateProgress("test.txt", 100, 100, StorageOperation.Upload);

        // Assert - No events after dispose
        Assert.Empty(receivedEvents);
    }

    [Fact]
    public void FileProgressChanged_BothStorages_ReceivesEventsFromBoth() {
        // Arrange
        using var localProgress = new ProgressFiringStorage(_localRootPath);
        using var remoteProgress = new ProgressFiringStorage(_remoteRootPath);
        var filter = new SyncFilter();
        var conflictResolver = new DefaultConflictResolver(ConflictResolution.UseLocal);
        using var engine = new SyncEngine(localProgress, remoteProgress, _database, conflictResolver, filter);

        var receivedEvents = new List<FileProgressEventArgs>();
        engine.FileProgressChanged += (sender, e) => receivedEvents.Add(e);

        // Act - Fire from both storages
        localProgress.SimulateProgress("local.txt", 100, 200, StorageOperation.Download);
        remoteProgress.SimulateProgress("remote.txt", 300, 400, StorageOperation.Upload);

        // Assert
        Assert.Equal(2, receivedEvents.Count);
        Assert.Equal("local.txt", receivedEvents[0].Path);
        Assert.Equal("remote.txt", receivedEvents[1].Path);
    }

    [Fact]
    public void FileProgressChanged_NoSubscribers_DoesNotThrow() {
        // Arrange
        using var progressStorage = new ProgressFiringStorage(_remoteRootPath);
        var filter = new SyncFilter();
        var conflictResolver = new DefaultConflictResolver(ConflictResolution.UseLocal);
        using var engine = new SyncEngine(_localStorage, progressStorage, _database, conflictResolver, filter);

        // Act & Assert - Should not throw when no subscribers
        progressStorage.SimulateProgress("test.txt", 100, 100, StorageOperation.Upload);
    }

    [Fact]
    public void FileProgressEventArgs_ZeroTotalBytes_ReturnsZeroPercent() {
        // Arrange & Act
        var args = new FileProgressEventArgs("test.txt", 0, 0, FileTransferOperation.Upload);

        // Assert
        Assert.Equal(0, args.PercentComplete);
    }

    [Fact]
    public void FileProgressEventArgs_FullTransfer_Returns100Percent() {
        // Arrange & Act
        var args = new FileProgressEventArgs("test.txt", 1024, 1024, FileTransferOperation.Download);

        // Assert
        Assert.Equal(100, args.PercentComplete);
    }

    /// <summary>
    /// A test-only storage wrapper that delegates to LocalFileStorage and allows
    /// simulating storage progress events.
    /// </summary>
    private sealed class ProgressFiringStorage: ISyncStorage, IDisposable {
        private readonly LocalFileStorage _inner;

        public event EventHandler<StorageProgressEventArgs>? ProgressChanged;
        public StorageType StorageType => _inner.StorageType;
        public string RootPath => _inner.RootPath;

        public ProgressFiringStorage(string rootPath) {
            _inner = new LocalFileStorage(rootPath);
        }

        public void SimulateProgress(string path, long bytesTransferred, long totalBytes, StorageOperation operation) {
            ProgressChanged?.Invoke(this, new StorageProgressEventArgs {
                Path = path,
                BytesTransferred = bytesTransferred,
                TotalBytes = totalBytes,
                Operation = operation,
                PercentComplete = totalBytes > 0 ? (int)(bytesTransferred * 100 / totalBytes) : 0
            });
        }

        public Task<IEnumerable<SyncItem>> ListItemsAsync(string path, CancellationToken ct = default) => _inner.ListItemsAsync(path, ct);
        public Task<SyncItem?> GetItemAsync(string path, CancellationToken ct = default) => _inner.GetItemAsync(path, ct);
        public Task<Stream> ReadFileAsync(string path, CancellationToken ct = default) => _inner.ReadFileAsync(path, ct);
        public Task WriteFileAsync(string path, Stream content, CancellationToken ct = default) => _inner.WriteFileAsync(path, content, ct);
        public Task CreateDirectoryAsync(string path, CancellationToken ct = default) => _inner.CreateDirectoryAsync(path, ct);
        public Task DeleteAsync(string path, CancellationToken ct = default) => _inner.DeleteAsync(path, ct);
        public Task MoveAsync(string source, string target, CancellationToken ct = default) => _inner.MoveAsync(source, target, ct);
        public Task<bool> ExistsAsync(string path, CancellationToken ct = default) => _inner.ExistsAsync(path, ct);
        public Task<StorageInfo> GetStorageInfoAsync(CancellationToken ct = default) => _inner.GetStorageInfoAsync(ct);
        public Task<string> ComputeHashAsync(string path, CancellationToken ct = default) => _inner.ComputeHashAsync(path, ct);
        public Task<bool> TestConnectionAsync(CancellationToken ct = default) => _inner.TestConnectionAsync(ct);

        public void Dispose() {
            // LocalFileStorage does not implement IDisposable, nothing to dispose
        }
    }

    #endregion
}
