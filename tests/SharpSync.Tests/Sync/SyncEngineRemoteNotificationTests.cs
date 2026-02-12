using Oire.SharpSync.Core;
using Oire.SharpSync.Database;
using Oire.SharpSync.Storage;
using Oire.SharpSync.Sync;

namespace Oire.SharpSync.Tests.Sync;

/// <summary>
/// Tests for remote change notification functionality of SyncEngine.
/// Verifies NotifyRemoteChangeAsync, NotifyRemoteChangeBatchAsync, NotifyRemoteRenameAsync,
/// ClearPendingLocalChanges, ClearPendingRemoteChanges, and GetPendingOperationsAsync with
/// both local and remote changes.
/// </summary>
public class SyncEngineRemoteNotificationTests: IDisposable {
    private readonly string _localRootPath;
    private readonly string _remoteRootPath;
    private readonly string _dbPath;
    private readonly LocalFileStorage _localStorage;
    private readonly LocalFileStorage _remoteStorage;
    private readonly SqliteSyncDatabase _database;
    private readonly SyncEngine _syncEngine;

    public SyncEngineRemoteNotificationTests() {
        _localRootPath = Path.Combine(Path.GetTempPath(), "SharpSyncRemoteNotifyTests", "Local", Guid.NewGuid().ToString());
        _remoteRootPath = Path.Combine(Path.GetTempPath(), "SharpSyncRemoteNotifyTests", "Remote", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_localRootPath);
        Directory.CreateDirectory(_remoteRootPath);

        _dbPath = Path.Combine(Path.GetTempPath(), "SharpSyncRemoteNotifyTests", $"sync_{Guid.NewGuid()}.db");
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

    #region NotifyRemoteChangeAsync Tests

    [Fact]
    public async Task NotifyRemoteChangeAsync_Created_ProducesDownloadPendingOperation() {
        // Arrange
        await File.WriteAllTextAsync(Path.Combine(_remoteRootPath, "remote_new.txt"), "remote content");

        // Act
        await _syncEngine.NotifyRemoteChangeAsync("remote_new.txt", ChangeType.Created);
        var pending = await _syncEngine.GetPendingOperationsAsync();

        // Assert
        var op = Assert.Single(pending, p => p.Path == "remote_new.txt");
        Assert.Equal(SyncActionType.Download, op.ActionType);
        Assert.Equal(ChangeSource.Remote, op.Source);
    }

    [Fact]
    public async Task NotifyRemoteChangeAsync_Changed_ProducesDownloadPendingOperation() {
        // Arrange
        await File.WriteAllTextAsync(Path.Combine(_remoteRootPath, "remote_mod.txt"), "modified content");

        // Act
        await _syncEngine.NotifyRemoteChangeAsync("remote_mod.txt", ChangeType.Changed);
        var pending = await _syncEngine.GetPendingOperationsAsync();

        // Assert
        var op = Assert.Single(pending, p => p.Path == "remote_mod.txt");
        Assert.Equal(SyncActionType.Download, op.ActionType);
        Assert.Equal(ChangeSource.Remote, op.Source);
    }

    [Fact]
    public async Task NotifyRemoteChangeAsync_Deleted_ProducesDeleteLocalPendingOperation() {
        // Act
        await _syncEngine.NotifyRemoteChangeAsync("remote_deleted.txt", ChangeType.Deleted);
        var pending = await _syncEngine.GetPendingOperationsAsync();

        // Assert
        var op = Assert.Single(pending, p => p.Path == "remote_deleted.txt");
        Assert.Equal(SyncActionType.DeleteLocal, op.ActionType);
        Assert.Equal(ChangeSource.Remote, op.Source);
    }

    [Fact]
    public async Task NotifyRemoteChangeAsync_DeleteSupersedes_PreviousCreated() {
        // Arrange
        await _syncEngine.NotifyRemoteChangeAsync("file.txt", ChangeType.Created);

        // Act - Delete supersedes created
        await _syncEngine.NotifyRemoteChangeAsync("file.txt", ChangeType.Deleted);
        var pending = await _syncEngine.GetPendingOperationsAsync();

        // Assert
        var op = Assert.Single(pending, p => p.Path == "file.txt");
        Assert.Equal(SyncActionType.DeleteLocal, op.ActionType);
    }

    [Fact]
    public async Task NotifyRemoteChangeAsync_CreateAfterDelete_BecomesChanged() {
        // Arrange
        await File.WriteAllTextAsync(Path.Combine(_remoteRootPath, "file.txt"), "content");
        await _syncEngine.NotifyRemoteChangeAsync("file.txt", ChangeType.Deleted);

        // Act - Create after delete becomes "Changed"
        await _syncEngine.NotifyRemoteChangeAsync("file.txt", ChangeType.Created);
        var pending = await _syncEngine.GetPendingOperationsAsync();

        // Assert
        var op = Assert.Single(pending, p => p.Path == "file.txt");
        Assert.Equal(SyncActionType.Download, op.ActionType);
        Assert.Equal("Changed", op.Reason);
    }

    [Fact]
    public async Task NotifyRemoteChangeAsync_AfterDispose_ThrowsObjectDisposedException() {
        _syncEngine.Dispose();
        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => _syncEngine.NotifyRemoteChangeAsync("file.txt", ChangeType.Created));
    }

    [Fact]
    public async Task NotifyRemoteChangeAsync_FilteredPath_IsIgnored() {
        // Arrange - Create engine with filter that excludes .tmp files
        var filter = new SyncFilter();
        filter.AddExclusionPattern("*.tmp");
        var conflictResolver = new DefaultConflictResolver(ConflictResolution.UseLocal);
        using var engine = new SyncEngine(_localStorage, _remoteStorage, _database, conflictResolver, filter);

        // Act
        await engine.NotifyRemoteChangeAsync("file.tmp", ChangeType.Created);
        var pending = await engine.GetPendingOperationsAsync();

        // Assert
        Assert.DoesNotContain(pending, p => p.Path == "file.tmp");
    }

    #endregion

    #region NotifyRemoteChangeBatchAsync Tests

    [Fact]
    public async Task NotifyRemoteChangeBatchAsync_MultipleChanges_AllTracked() {
        // Arrange
        await File.WriteAllTextAsync(Path.Combine(_remoteRootPath, "batch1.txt"), "content1");
        await File.WriteAllTextAsync(Path.Combine(_remoteRootPath, "batch2.txt"), "content2");

        var changes = new List<ChangeInfo> {
            new("batch1.txt", ChangeType.Created),
            new("batch2.txt", ChangeType.Changed),
            new("batch3.txt", ChangeType.Deleted)
        };

        // Act
        await _syncEngine.NotifyRemoteChangeBatchAsync(changes);
        var pending = await _syncEngine.GetPendingOperationsAsync();

        // Assert
        Assert.Equal(3, pending.Count);
        Assert.Contains(pending, p => p.Path == "batch1.txt" && p.ActionType == SyncActionType.Download);
        Assert.Contains(pending, p => p.Path == "batch2.txt" && p.ActionType == SyncActionType.Download);
        Assert.Contains(pending, p => p.Path == "batch3.txt" && p.ActionType == SyncActionType.DeleteLocal);
    }

    [Fact]
    public async Task NotifyRemoteChangeBatchAsync_AfterDispose_ThrowsObjectDisposedException() {
        _syncEngine.Dispose();
        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => _syncEngine.NotifyRemoteChangeBatchAsync(new[] { new ChangeInfo("file.txt", ChangeType.Created) }));
    }

    #endregion

    #region NotifyRemoteRenameAsync Tests

    [Fact]
    public async Task NotifyRemoteRenameAsync_ProducesDeleteAndDownload() {
        // Arrange
        await File.WriteAllTextAsync(Path.Combine(_remoteRootPath, "new_name.txt"), "content");

        // Act
        await _syncEngine.NotifyRemoteRenameAsync("old_name.txt", "new_name.txt");
        var pending = await _syncEngine.GetPendingOperationsAsync();

        // Assert
        Assert.Equal(2, pending.Count);

        var deleteOp = Assert.Single(pending, p => p.Path == "old_name.txt");
        Assert.Equal(SyncActionType.DeleteLocal, deleteOp.ActionType);
        Assert.Equal("new_name.txt", deleteOp.RenamedTo);

        var downloadOp = Assert.Single(pending, p => p.Path == "new_name.txt");
        Assert.Equal(SyncActionType.Download, downloadOp.ActionType);
        Assert.Equal("old_name.txt", downloadOp.RenamedFrom);
    }

    [Fact]
    public async Task NotifyRemoteRenameAsync_BothOpsAreRemoteSource() {
        // Arrange
        await File.WriteAllTextAsync(Path.Combine(_remoteRootPath, "renamed.txt"), "content");

        // Act
        await _syncEngine.NotifyRemoteRenameAsync("original.txt", "renamed.txt");
        var pending = await _syncEngine.GetPendingOperationsAsync();

        // Assert
        Assert.All(pending, p => Assert.Equal(ChangeSource.Remote, p.Source));
    }

    [Fact]
    public async Task NotifyRemoteRenameAsync_AfterDispose_ThrowsObjectDisposedException() {
        _syncEngine.Dispose();
        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => _syncEngine.NotifyRemoteRenameAsync("old.txt", "new.txt"));
    }

    #endregion

    #region GetPendingOperationsAsync Mixed Local/Remote Tests

    [Fact]
    public async Task GetPendingOperationsAsync_MixedLocalAndRemote_ReturnsBoth() {
        // Arrange
        await File.WriteAllTextAsync(Path.Combine(_localRootPath, "local_file.txt"), "local");
        await File.WriteAllTextAsync(Path.Combine(_remoteRootPath, "remote_file.txt"), "remote");

        await _syncEngine.NotifyLocalChangeAsync("local_file.txt", ChangeType.Created);
        await _syncEngine.NotifyRemoteChangeAsync("remote_file.txt", ChangeType.Created);

        // Act
        var pending = await _syncEngine.GetPendingOperationsAsync();

        // Assert
        Assert.Equal(2, pending.Count);

        var localOp = Assert.Single(pending, p => p.Path == "local_file.txt");
        Assert.Equal(SyncActionType.Upload, localOp.ActionType);
        Assert.Equal(ChangeSource.Local, localOp.Source);

        var remoteOp = Assert.Single(pending, p => p.Path == "remote_file.txt");
        Assert.Equal(SyncActionType.Download, remoteOp.ActionType);
        Assert.Equal(ChangeSource.Remote, remoteOp.Source);
    }

    #endregion

    #region ClearPendingLocalChanges / ClearPendingRemoteChanges Tests

    [Fact]
    public async Task ClearPendingLocalChanges_OnlyClearsLocal() {
        // Arrange
        await File.WriteAllTextAsync(Path.Combine(_localRootPath, "local.txt"), "local");
        await File.WriteAllTextAsync(Path.Combine(_remoteRootPath, "remote.txt"), "remote");

        await _syncEngine.NotifyLocalChangeAsync("local.txt", ChangeType.Created);
        await _syncEngine.NotifyRemoteChangeAsync("remote.txt", ChangeType.Created);

        // Act
        _syncEngine.ClearPendingLocalChanges();
        var pending = await _syncEngine.GetPendingOperationsAsync();

        // Assert - Only remote changes remain
        var op = Assert.Single(pending);
        Assert.Equal("remote.txt", op.Path);
        Assert.Equal(ChangeSource.Remote, op.Source);
    }

    [Fact]
    public async Task ClearPendingRemoteChanges_OnlyClearsRemote() {
        // Arrange
        await File.WriteAllTextAsync(Path.Combine(_localRootPath, "local.txt"), "local");
        await File.WriteAllTextAsync(Path.Combine(_remoteRootPath, "remote.txt"), "remote");

        await _syncEngine.NotifyLocalChangeAsync("local.txt", ChangeType.Created);
        await _syncEngine.NotifyRemoteChangeAsync("remote.txt", ChangeType.Created);

        // Act
        _syncEngine.ClearPendingRemoteChanges();
        var pending = await _syncEngine.GetPendingOperationsAsync();

        // Assert - Only local changes remain
        var op = Assert.Single(pending);
        Assert.Equal("local.txt", op.Path);
        Assert.Equal(ChangeSource.Local, op.Source);
    }

    [Fact]
    public async Task ClearPendingRemoteChanges_WhenEmpty_DoesNotThrow() {
        // Act & Assert
        _syncEngine.ClearPendingRemoteChanges();
        var pending = await _syncEngine.GetPendingOperationsAsync();
        Assert.Empty(pending);
    }

    [Fact]
    public void ClearPendingLocalChanges_AfterDispose_ThrowsObjectDisposedException() {
        _syncEngine.Dispose();
        Assert.Throws<ObjectDisposedException>(() => _syncEngine.ClearPendingLocalChanges());
    }

    [Fact]
    public void ClearPendingRemoteChanges_AfterDispose_ThrowsObjectDisposedException() {
        _syncEngine.Dispose();
        Assert.Throws<ObjectDisposedException>(() => _syncEngine.ClearPendingRemoteChanges());
    }

    [Fact]
    public async Task ClearBoth_RemovesAllPending() {
        // Arrange
        await _syncEngine.NotifyLocalChangeAsync("local.txt", ChangeType.Created);
        await _syncEngine.NotifyRemoteChangeAsync("remote.txt", ChangeType.Created);

        // Act
        _syncEngine.ClearPendingLocalChanges();
        _syncEngine.ClearPendingRemoteChanges();
        var pending = await _syncEngine.GetPendingOperationsAsync();

        // Assert
        Assert.Empty(pending);
    }

    #endregion

    #region Path Normalization Tests

    [Fact]
    public async Task NotifyRemoteChangeAsync_NormalizesBackslashes() {
        // Arrange
        await File.WriteAllTextAsync(Path.Combine(_remoteRootPath, "file.txt"), "content");

        // Act
        await _syncEngine.NotifyRemoteChangeAsync("path\\to\\file.txt", ChangeType.Created);
        var pending = await _syncEngine.GetPendingOperationsAsync();

        // Assert - Path should be normalized with forward slashes
        var op = Assert.Single(pending);
        Assert.Equal("path/to/file.txt", op.Path);
    }

    [Fact]
    public async Task NotifyRemoteChangeAsync_TrimsLeadingTrailingSlashes() {
        // Act
        await _syncEngine.NotifyRemoteChangeAsync("/file.txt/", ChangeType.Deleted);
        var pending = await _syncEngine.GetPendingOperationsAsync();

        // Assert
        var op = Assert.Single(pending);
        Assert.Equal("file.txt", op.Path);
    }

    #endregion

    #region Thread Safety Tests

    [Fact]
    public async Task NotifyRemoteChangeAsync_ConcurrentCalls_AllSucceed() {
        // Arrange
        var errors = new List<Exception>();
        var tasks = Enumerable.Range(0, 50).Select(i => Task.Run(async () => {
            try {
                await _syncEngine.NotifyRemoteChangeAsync($"concurrent_{i}.txt", ChangeType.Created);
            } catch (Exception ex) {
                lock (errors) {
                    errors.Add(ex);
                }
            }
        })).ToArray();

        // Act
        await Task.WhenAll(tasks);

        // Assert
        Assert.Empty(errors);
        var pending = await _syncEngine.GetPendingOperationsAsync();
        Assert.Equal(50, pending.Count);
    }

    [Fact]
    public async Task ClearPendingRemoteChanges_ConcurrentWithNotify_DoesNotThrow() {
        // Arrange
        var errors = new List<Exception>();

        // Act - Simultaneously notify and clear
        var notifyTasks = Enumerable.Range(0, 20).Select(i => Task.Run(async () => {
            try {
                await _syncEngine.NotifyRemoteChangeAsync($"file_{i}.txt", ChangeType.Created);
            } catch (Exception ex) {
                lock (errors) {
                    errors.Add(ex);
                }
            }
        }));

        var clearTasks = Enumerable.Range(0, 5).Select(_ => Task.Run(() => {
            try {
                _syncEngine.ClearPendingRemoteChanges();
            } catch (Exception ex) {
                lock (errors) {
                    errors.Add(ex);
                }
            }
        }));

        await Task.WhenAll(notifyTasks.Concat(clearTasks));

        // Assert
        Assert.Empty(errors);
    }

    #endregion

    #region GetPendingOperationsAsync Remote Branch Tests

    [Fact]
    public async Task GetPendingOperationsAsync_RemoteRenamed_ProducesDownloadAction() {
        // Arrange - Directly notify a Renamed change type
        // The Renamed enum value is handled in the switch as SyncActionType.Download
        await File.WriteAllTextAsync(Path.Combine(_remoteRootPath, "renamed_file.txt"), "content");
        await _syncEngine.NotifyRemoteChangeAsync("renamed_file.txt", ChangeType.Renamed);

        // Act
        var pending = await _syncEngine.GetPendingOperationsAsync();

        // Assert
        var op = Assert.Single(pending, p => p.Path == "renamed_file.txt");
        Assert.Equal(SyncActionType.Download, op.ActionType);
        Assert.Equal(ChangeSource.Remote, op.Source);
        Assert.Equal("Renamed", op.Reason);
    }

    [Fact]
    public async Task GetPendingOperationsAsync_RemoteDeletedItem_SizeIsZero() {
        // Arrange - Deleted items don't try to get remote item info
        await _syncEngine.NotifyRemoteChangeAsync("removed.txt", ChangeType.Deleted);

        // Act
        var pending = await _syncEngine.GetPendingOperationsAsync();

        // Assert
        var op = Assert.Single(pending, p => p.Path == "removed.txt");
        Assert.Equal(0, op.Size);
        Assert.False(op.IsDirectory);
    }

    [Fact]
    public async Task GetPendingOperationsAsync_RemoteItemThrows_StillIncludesPending() {
        // Arrange - Notify a Created change for a file that doesn't exist on remote
        // When GetItemAsync throws (file not found), the pending operation should still
        // be included but with default size/isDirectory
        await _syncEngine.NotifyRemoteChangeAsync("missing_on_remote.txt", ChangeType.Created);

        // Act - File doesn't exist in _remoteRootPath, so GetItemAsync will throw
        var pending = await _syncEngine.GetPendingOperationsAsync();

        // Assert - Should still have the pending operation with defaults
        var op = Assert.Single(pending, p => p.Path == "missing_on_remote.txt");
        Assert.Equal(SyncActionType.Download, op.ActionType);
        Assert.Equal(0, op.Size);
    }

    #endregion

    #region NotifyRemoteChangeBatchAsync Merge Logic Tests

    [Fact]
    public async Task NotifyRemoteChangeBatchAsync_FilteredPath_IsIgnored() {
        // Arrange - Create engine with filter that excludes .log files
        var filter = new SyncFilter();
        filter.AddExclusionPattern("*.log");
        var conflictResolver = new DefaultConflictResolver(ConflictResolution.UseLocal);
        using var engine = new SyncEngine(_localStorage, _remoteStorage, _database, conflictResolver, filter);

        var changes = new List<ChangeInfo> {
            new("debug.log", ChangeType.Created),
            new("access.log", ChangeType.Changed)
        };

        // Act
        await engine.NotifyRemoteChangeBatchAsync(changes);
        var pending = await engine.GetPendingOperationsAsync();

        // Assert
        Assert.DoesNotContain(pending, p => p.Path == "debug.log");
        Assert.DoesNotContain(pending, p => p.Path == "access.log");
    }

    [Fact]
    public async Task NotifyRemoteChangeBatchAsync_DeleteSupersedes_PreviousCreated() {
        // Arrange - First batch: create, Second batch: delete same path
        await _syncEngine.NotifyRemoteChangeBatchAsync(new[] { new ChangeInfo("file.txt", ChangeType.Created) });

        // Act - Delete in second batch supersedes created
        await _syncEngine.NotifyRemoteChangeBatchAsync(new[] { new ChangeInfo("file.txt", ChangeType.Deleted) });
        var pending = await _syncEngine.GetPendingOperationsAsync();

        // Assert
        var op = Assert.Single(pending, p => p.Path == "file.txt");
        Assert.Equal(SyncActionType.DeleteLocal, op.ActionType);
    }

    [Fact]
    public async Task NotifyRemoteChangeBatchAsync_CreateAfterDelete_BecomesChanged() {
        // Arrange
        await File.WriteAllTextAsync(Path.Combine(_remoteRootPath, "file.txt"), "content");
        await _syncEngine.NotifyRemoteChangeBatchAsync(new[] { new ChangeInfo("file.txt", ChangeType.Deleted) });

        // Act - Create after delete in batch becomes Changed
        await _syncEngine.NotifyRemoteChangeBatchAsync(new[] { new ChangeInfo("file.txt", ChangeType.Created) });
        var pending = await _syncEngine.GetPendingOperationsAsync();

        // Assert
        var op = Assert.Single(pending, p => p.Path == "file.txt");
        Assert.Equal(SyncActionType.Download, op.ActionType);
        Assert.Equal("Changed", op.Reason);
    }

    #endregion
}
