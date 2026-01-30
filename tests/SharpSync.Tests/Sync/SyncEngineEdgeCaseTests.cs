namespace Oire.SharpSync.Tests.Sync;

public class SyncEngineEdgeCaseTests: IDisposable {
    private static readonly string[] cancelFilePaths = new[] { "cancelfile.txt" };
    private readonly string _localRootPath;
    private readonly string _remoteRootPath;
    private readonly string _dbPath;
    private readonly LocalFileStorage _localStorage;
    private readonly LocalFileStorage _remoteStorage;
    private readonly SqliteSyncDatabase _database;

    public SyncEngineEdgeCaseTests() {
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

    #region GetDomainFromUrl Tests

    [Theory]
    [InlineData("https://cloud.example.com/dav/", "cloud.example.com")]
    [InlineData("http://localhost:8080/files/", "localhost")]
    [InlineData("https://disk.cx/remote.php/dav/files/user/", "disk.cx")]
    [InlineData("/local/path", "remote")]
    [InlineData("not-a-url", "remote")]
    [InlineData(@"C:\Users\User\SyncFolder", "remote")]
    public void GetDomainFromUrl_ReturnsExpectedDomain(string url, string expected) {
        // Act
        var result = SyncEngine.GetDomainFromUrl(url);

        // Assert
        Assert.Equal(expected, result);
    }

    #endregion

    #region DeleteExtraneous Tests

    [Fact]
    public async Task DeleteExtraneous_RemoteFileNotLocal_MarkedForDeletion() {
        // Arrange
        var filter = new SyncFilter();
        var resolver = new DefaultConflictResolver(ConflictResolution.UseLocal);
        using var engine = new SyncEngine(_localStorage, _remoteStorage, _database, filter, resolver);

        // Create file on both sides and sync to establish tracked state
        var localPath = Path.Combine(_localRootPath, "tracked.txt");
        await File.WriteAllTextAsync(localPath, "content");
        await engine.SynchronizeAsync();

        // Delete locally but leave remote — simulates extraneous remote file
        File.Delete(localPath);

        // Act
        var result = await engine.SynchronizeAsync(new SyncOptions { DeleteExtraneous = true });

        // Assert
        Assert.True(result.Success);
        Assert.False(File.Exists(Path.Combine(_remoteRootPath, "tracked.txt")));
    }

    #endregion

    #region HasChangedAsync Edge Cases

    [Fact]
    public async Task HasChanged_NullLocalModifiedInTrackedState_DetectsChange() {
        // Arrange — Sync a file, then manipulate DB to clear LocalModified
        var filter = new SyncFilter();
        var resolver = new DefaultConflictResolver(ConflictResolution.UseLocal);
        using var engine = new SyncEngine(_localStorage, _remoteStorage, _database, filter, resolver);

        var localPath = Path.Combine(_localRootPath, "nullmod.txt");
        await File.WriteAllTextAsync(localPath, "initial content");
        await engine.SynchronizeAsync();

        // Set LocalModified to null in the database to simulate "was deleted, now exists"
        var state = await _database.GetSyncStateAsync("nullmod.txt");
        Assert.NotNull(state);
        state!.LocalModified = null;
        await _database.UpdateSyncStateAsync(state);

        // Modify the file so there's something to detect
        await Task.Delay(100);
        await File.WriteAllTextAsync(localPath, "changed content");

        // Act — engine should detect this as changed (null LocalModified → true)
        var plan = await engine.GetSyncPlanAsync();

        // Assert — should show an upload action for the file
        Assert.Contains(plan.Actions, a => a.Path == "nullmod.txt" && a.ActionType == SyncActionType.Upload);
    }

    [Fact]
    public async Task HasChanged_SizeMismatchOnly_DetectsChange() {
        // Arrange
        var filter = new SyncFilter();
        var resolver = new DefaultConflictResolver(ConflictResolution.UseLocal);
        using var engine = new SyncEngine(_localStorage, _remoteStorage, _database, filter, resolver);

        var localPath = Path.Combine(_localRootPath, "sizemismatch.txt");
        await File.WriteAllTextAsync(localPath, "short");
        await engine.SynchronizeAsync();

        // Modify the file content to change size but preserve the same timestamp
        var originalWriteTime = File.GetLastWriteTimeUtc(localPath);
        await File.WriteAllTextAsync(localPath, "much longer content that changes the file size");
        File.SetLastWriteTimeUtc(localPath, originalWriteTime);

        // Act
        var plan = await engine.GetSyncPlanAsync();

        // Assert — should detect the size change
        Assert.Contains(plan.Actions, a => a.Path == "sizemismatch.txt" && a.ActionType == SyncActionType.Upload);
    }

    #endregion

    #region IncorporatePendingChanges — Deleted Path

    [Fact]
    public async Task IncorporatePendingChanges_DeletedPath_IncludesDeleteRemoteAction() {
        // Arrange
        var filter = new SyncFilter();
        var resolver = new DefaultConflictResolver(ConflictResolution.UseLocal);
        using var engine = new SyncEngine(_localStorage, _remoteStorage, _database, filter, resolver);

        // Create file and sync
        var localPath = Path.Combine(_localRootPath, "todelete.txt");
        await File.WriteAllTextAsync(localPath, "content");
        await engine.SynchronizeAsync();

        // Delete the local file
        File.Delete(localPath);

        // Notify the deletion
        await engine.NotifyLocalChangeAsync("todelete.txt", ChangeType.Deleted);

        // Act
        var plan = await engine.GetSyncPlanAsync();

        // Assert — plan should include a DeleteRemote action for the file
        Assert.Contains(plan.Actions, a => a.Path == "todelete.txt" && a.ActionType == SyncActionType.DeleteRemote);
    }

    #endregion

    #region Selective Sync Edge Cases

    [Fact]
    public async Task SyncFolderAsync_CancellationRequested_ThrowsOperationCanceledException() {
        // Arrange
        Directory.CreateDirectory(Path.Combine(_localRootPath, "CancelFolder"));
        await File.WriteAllTextAsync(Path.Combine(_localRootPath, "CancelFolder", "file.txt"), "content");

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var filter = new SyncFilter();
        var resolver = new DefaultConflictResolver(ConflictResolution.UseLocal);
        using var engine = new SyncEngine(_localStorage, _remoteStorage, _database, filter, resolver);

        // Act & Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => engine.SyncFolderAsync("CancelFolder", cancellationToken: cts.Token));
    }

    [Fact]
    public async Task SyncFilesAsync_CancellationRequested_ThrowsOperationCanceledException() {
        // Arrange
        await File.WriteAllTextAsync(Path.Combine(_localRootPath, "cancelfile.txt"), "content");

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var filter = new SyncFilter();
        var resolver = new DefaultConflictResolver(ConflictResolution.UseLocal);
        using var engine = new SyncEngine(_localStorage, _remoteStorage, _database, filter, resolver);

        // Act & Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => engine.SyncFilesAsync(cancelFilePaths, cancellationToken: cts.Token));
    }

    [Fact]
    public async Task NotifyLocalRenameAsync_SamePath_HandlesGracefully() {
        // Arrange
        var filter = new SyncFilter();
        var resolver = new DefaultConflictResolver(ConflictResolution.UseLocal);
        using var engine = new SyncEngine(_localStorage, _remoteStorage, _database, filter, resolver);

        await File.WriteAllTextAsync(Path.Combine(_localRootPath, "samename.txt"), "content");

        // Act — rename to same path should not crash
        await engine.NotifyLocalRenameAsync("samename.txt", "samename.txt");
        var pending = await engine.GetPendingOperationsAsync();

        // Assert — implementation-dependent, but must not throw
        Assert.NotNull(pending);
    }

    [Fact]
    public async Task NotifyLocalRenameAsync_NormalizesBackslashPaths() {
        // Arrange
        var filter = new SyncFilter();
        var resolver = new DefaultConflictResolver(ConflictResolution.UseLocal);
        using var engine = new SyncEngine(_localStorage, _remoteStorage, _database, filter, resolver);

        Directory.CreateDirectory(Path.Combine(_localRootPath, "folder"));
        await File.WriteAllTextAsync(Path.Combine(_localRootPath, "folder", "new.txt"), "content");

        // Act — use backslash paths
        await engine.NotifyLocalRenameAsync("folder\\old.txt", "folder\\new.txt");
        var pending = await engine.GetPendingOperationsAsync();

        // Assert — paths should be normalized to forward slashes
        Assert.Contains(pending, p => p.Path == "folder/old.txt");
        Assert.Contains(pending, p => p.Path == "folder/new.txt");
    }

    [Fact]
    public async Task SyncFolderAsync_WhileSyncRunning_ThrowsInvalidOperationException() {
        // Arrange
        var filter = new SyncFilter();
        var resolver = new DefaultConflictResolver(ConflictResolution.UseLocal);
        using var engine = new SyncEngine(_localStorage, _remoteStorage, _database, filter, resolver);

        for (int i = 0; i < 10; i++) {
            await File.WriteAllTextAsync(Path.Combine(_localRootPath, $"block_{i}.txt"), new string('x', 10000));
        }

        Directory.CreateDirectory(Path.Combine(_localRootPath, "SubFolder"));
        await File.WriteAllTextAsync(Path.Combine(_localRootPath, "SubFolder", "sub.txt"), "content");

        var syncStarted = new TaskCompletionSource();
        engine.ProgressChanged += (s, e) => {
            if (e.Operation != SyncOperation.Scanning) {
                syncStarted.TrySetResult();
            }
        };

        // Act — start full sync
        var syncTask = Task.Run(() => engine.SynchronizeAsync());
        await Task.WhenAny(syncStarted.Task, Task.Delay(TimeSpan.FromSeconds(5)));

        // Attempt folder sync while full sync is running
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => engine.SyncFolderAsync("SubFolder"));

        await syncTask;

        // Assert
        Assert.Contains("already in progress", exception.Message.ToLower());
    }

    #endregion
}
