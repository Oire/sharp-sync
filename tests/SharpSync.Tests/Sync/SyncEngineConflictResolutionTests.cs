namespace Oire.SharpSync.Tests.Sync;

public class SyncEngineConflictResolutionTests: IDisposable {
    private readonly string _localRootPath;
    private readonly string _remoteRootPath;
    private readonly string _dbPath;
    private readonly LocalFileStorage _localStorage;
    private readonly LocalFileStorage _remoteStorage;
    private readonly SqliteSyncDatabase _database;

    public SyncEngineConflictResolutionTests() {
        _localRootPath = Path.Combine(Path.GetTempPath(), "SharpSyncTests", "Local", Guid.NewGuid().ToString());
        _remoteRootPath = Path.Combine(Path.GetTempPath(), "SharpSyncTests", "Remote", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_localRootPath);
        Directory.CreateDirectory(_remoteRootPath);

        _dbPath = Path.Combine(Path.GetTempPath(), "SharpSyncTests", $"sync_{Guid.NewGuid()}.db");
        _localStorage = new LocalFileStorage(_localRootPath);
        _remoteStorage = new LocalFileStorage(_remoteRootPath);
        _database = new SqliteSyncDatabase(_dbPath);
        _database.InitializeAsync().GetAwaiter().GetResult();
    }

    public void Dispose() {
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

    private SyncEngine CreateEngine(ConflictResolution resolution) {
        var filter = new SyncFilter();
        var resolver = new DefaultConflictResolver(resolution);
        return new SyncEngine(_localStorage, _remoteStorage, _database, resolver, filter);
    }

    private async Task CreateConflict(SyncEngine engine, string fileName, string localContent, string remoteContent) {
        // Create file and do initial sync
        var localPath = Path.Combine(_localRootPath, fileName);
        await File.WriteAllTextAsync(localPath, "initial content");
        await engine.SynchronizeAsync();

        // Modify both sides to create a conflict
        await Task.Delay(100);
        await File.WriteAllTextAsync(localPath, localContent);
        await File.WriteAllTextAsync(Path.Combine(_remoteRootPath, fileName), remoteContent);
    }

    #region Conflict Resolution Branch Tests

    [Fact]
    public async Task ConflictResolution_UseLocal_RemoteGetsLocalContent() {
        // Arrange
        using var engine = CreateEngine(ConflictResolution.UseLocal);
        await CreateConflict(engine, "conflict.txt", "local wins", "remote loses");

        // Act
        var result = await engine.SynchronizeAsync(new SyncOptions { UpdateExisting = true });

        // Assert
        Assert.True(result.Success);
        var remoteContent = await File.ReadAllTextAsync(Path.Combine(_remoteRootPath, "conflict.txt"));
        Assert.Equal("local wins", remoteContent);
    }

    [Fact]
    public async Task ConflictResolution_UseRemote_LocalGetsRemoteContent() {
        // Arrange
        using var engine = CreateEngine(ConflictResolution.UseRemote);
        await CreateConflict(engine, "conflict.txt", "local loses", "remote wins");

        // Act
        var result = await engine.SynchronizeAsync(new SyncOptions { UpdateExisting = true });

        // Assert
        Assert.True(result.Success);
        var localContent = await File.ReadAllTextAsync(Path.Combine(_localRootPath, "conflict.txt"));
        Assert.Equal("remote wins", localContent);
    }

    [Fact]
    public async Task ConflictResolution_Skip_BothFilesUnchanged() {
        // Arrange
        using var engine = CreateEngine(ConflictResolution.Skip);
        await CreateConflict(engine, "conflict.txt", "local content", "remote content");

        // Act
        var result = await engine.SynchronizeAsync(new SyncOptions { UpdateExisting = true });

        // Assert
        Assert.True(result.Success);
        Assert.True(result.FilesSkipped > 0);
        var localContent = await File.ReadAllTextAsync(Path.Combine(_localRootPath, "conflict.txt"));
        var remoteContent = await File.ReadAllTextAsync(Path.Combine(_remoteRootPath, "conflict.txt"));
        Assert.Equal("local content", localContent);
        Assert.Equal("remote content", remoteContent);
    }

    [Fact]
    public async Task ConflictResolution_RenameLocal_LocalFileRenamed() {
        // Arrange
        using var engine = CreateEngine(ConflictResolution.RenameLocal);
        await CreateConflict(engine, "conflict.txt", "local content", "remote content");

        // Act
        var result = await engine.SynchronizeAsync(new SyncOptions { UpdateExisting = true });

        // Assert
        Assert.True(result.Success);

        // The original local path should now have the remote content (downloaded)
        var originalContent = await File.ReadAllTextAsync(Path.Combine(_localRootPath, "conflict.txt"));
        Assert.Equal("remote content", originalContent);

        // The renamed file should contain the local content with machine name
        var machineName = Environment.MachineName;
        var renamedPath = Path.Combine(_localRootPath, $"conflict ({machineName}).txt");
        Assert.True(File.Exists(renamedPath), $"Expected renamed file at: {renamedPath}");
        var renamedContent = await File.ReadAllTextAsync(renamedPath);
        Assert.Equal("local content", renamedContent);
    }

    [Fact]
    public async Task ConflictResolution_RenameRemote_RemoteFileRenamed() {
        // Arrange
        using var engine = CreateEngine(ConflictResolution.RenameRemote);
        await CreateConflict(engine, "conflict.txt", "local content", "remote content");

        // Act
        var result = await engine.SynchronizeAsync(new SyncOptions { UpdateExisting = true });

        // Assert
        Assert.True(result.Success);

        // The original remote path should now have the local content (uploaded)
        var originalContent = await File.ReadAllTextAsync(Path.Combine(_remoteRootPath, "conflict.txt"));
        Assert.Equal("local content", originalContent);

        // The renamed file should contain the remote content
        // Since remote storage is LocalFileStorage with a local path, GetDomainFromUrl returns "remote" fallback
        var renamedPath = Path.Combine(_remoteRootPath, "conflict (remote).txt");
        Assert.True(File.Exists(renamedPath), $"Expected renamed file at: {renamedPath}");
        var renamedContent = await File.ReadAllTextAsync(renamedPath);
        Assert.Equal("remote content", renamedContent);
    }

    [Fact]
    public async Task ConflictResolution_Ask_FilesConflictedIncremented() {
        // Arrange — Ask is the default which increments FilesConflicted
        using var engine = CreateEngine(ConflictResolution.Ask);
        await CreateConflict(engine, "conflict.txt", "local content", "remote content");

        // Act
        var result = await engine.SynchronizeAsync(new SyncOptions { UpdateExisting = true });

        // Assert
        Assert.True(result.Success);
        Assert.True(result.FilesConflicted > 0);
    }

    #endregion

    #region Unique Name Generation Tests

    [Fact]
    public async Task ConflictResolution_RenameLocal_WithExistingConflictFile_GeneratesNumberedName() {
        // Arrange
        using var engine = CreateEngine(ConflictResolution.RenameLocal);

        var machineName = Environment.MachineName;

        // Create initial file and sync
        var localPath = Path.Combine(_localRootPath, "conflict.txt");
        await File.WriteAllTextAsync(localPath, "initial content");
        await engine.SynchronizeAsync();

        // Pre-create the conflict-named file so the numbered version is used
        var existingConflictPath = Path.Combine(_localRootPath, $"conflict ({machineName}).txt");
        await File.WriteAllTextAsync(existingConflictPath, "existing conflict");

        // Now create a conflict
        await Task.Delay(100);
        await File.WriteAllTextAsync(localPath, "local content");
        await File.WriteAllTextAsync(Path.Combine(_remoteRootPath, "conflict.txt"), "remote content");

        // Act
        var result = await engine.SynchronizeAsync(new SyncOptions { UpdateExisting = true });

        // Assert
        Assert.True(result.Success);
        var numberedPath = Path.Combine(_localRootPath, $"conflict ({machineName} 2).txt");
        Assert.True(File.Exists(numberedPath), $"Expected numbered conflict file at: {numberedPath}");
        var numberedContent = await File.ReadAllTextAsync(numberedPath);
        Assert.Equal("local content", numberedContent);
    }

    #endregion

    #region Deletion Conflict Tests

    [Fact]
    public async Task DeletionConflict_DeletedLocallyModifiedRemotely_DetectsConflict() {
        // Arrange
        using var engine = CreateEngine(ConflictResolution.Skip);
        var fileName = "delconflict.txt";
        var localPath = Path.Combine(_localRootPath, fileName);

        // Create file and sync
        await File.WriteAllTextAsync(localPath, "initial content");
        await engine.SynchronizeAsync();

        // Manipulate DB state: set RemoteModified ahead of LocalModified
        // This simulates the scenario where remote was modified after local was synced
        var state = await _database.GetSyncStateAsync(fileName);
        Assert.NotNull(state);
        state!.RemoteModified = state.LocalModified!.Value.AddSeconds(10);
        await _database.UpdateSyncStateAsync(state);

        // Delete locally — file still exists remotely with "newer" tracked timestamp
        File.Delete(localPath);

        // Track the conflict type detected
        ConflictType? detectedType = null;
        engine.ConflictDetected += (sender, args) => {
            detectedType = args.ConflictType;
        };

        // Act
        var result = await engine.SynchronizeAsync(new SyncOptions { UpdateExisting = true });

        // Assert
        Assert.True(result.Success);
        Assert.Equal(ConflictType.DeletedLocallyModifiedRemotely, detectedType);
    }

    [Fact]
    public async Task DeletionConflict_ModifiedLocallyDeletedRemotely_DetectsConflict() {
        // Arrange
        using var engine = CreateEngine(ConflictResolution.Skip);
        var fileName = "delconflict.txt";
        var localPath = Path.Combine(_localRootPath, fileName);
        var remotePath = Path.Combine(_remoteRootPath, fileName);

        // Create file and sync
        await File.WriteAllTextAsync(localPath, "initial content");
        await engine.SynchronizeAsync();

        // Manipulate DB state: set LocalModified ahead of RemoteModified
        // This simulates the scenario where local was modified after remote was synced
        var state = await _database.GetSyncStateAsync(fileName);
        Assert.NotNull(state);
        state!.LocalModified = state.RemoteModified!.Value.AddSeconds(10);
        await _database.UpdateSyncStateAsync(state);

        // Delete remotely — file still exists locally with "newer" tracked timestamp
        File.Delete(remotePath);

        // Track the conflict type detected
        ConflictType? detectedType = null;
        engine.ConflictDetected += (sender, args) => {
            detectedType = args.ConflictType;
        };

        // Act
        var result = await engine.SynchronizeAsync(new SyncOptions { UpdateExisting = true });

        // Assert
        Assert.True(result.Success);
        Assert.Equal(ConflictType.ModifiedLocallyDeletedRemotely, detectedType);
    }

    #endregion
}
