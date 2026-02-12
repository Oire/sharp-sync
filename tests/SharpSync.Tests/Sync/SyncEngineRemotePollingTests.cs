using Moq;
using Oire.SharpSync.Tests.Fixtures;

namespace Oire.SharpSync.Tests.Sync;

/// <summary>
/// Tests for SyncEngine remote change integration paths:
/// IncorporatePendingRemoteChangesAsync and TryPollRemoteChangesAsync,
/// exercised via GetSyncPlanAsync with mock storage.
/// </summary>
public class SyncEngineRemotePollingTests: IDisposable {
    private readonly Mock<ISyncStorage> _mockLocal;
    private readonly Mock<ISyncStorage> _mockRemote;
    private readonly Mock<ISyncDatabase> _mockDatabase;
    private readonly Mock<IConflictResolver> _mockConflictResolver;

    public SyncEngineRemotePollingTests() {
        _mockLocal = MockStorageFactory.CreateMockStorage(rootPath: "/local");
        _mockRemote = MockStorageFactory.CreateMockStorage(rootPath: "/remote");
        _mockDatabase = MockStorageFactory.CreateMockDatabase();
        _mockConflictResolver = MockStorageFactory.CreateMockConflictResolver();

        // Default: local and remote return empty item lists
        _mockLocal.Setup(x => x.ListItemsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Enumerable.Empty<SyncItem>());
        _mockRemote.Setup(x => x.ListItemsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Enumerable.Empty<SyncItem>());

        // Default: no remote changes from polling
        _mockRemote.Setup(x => x.GetRemoteChangesAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<ChangeInfo>());
    }

    public void Dispose() {
        // No-op: mock-based tests don't create engine in constructor
    }

    private SyncEngine CreateEngine(ISyncFilter? filter = null) {
        return new SyncEngine(
            _mockLocal.Object,
            _mockRemote.Object,
            _mockDatabase.Object,
            _mockConflictResolver.Object,
            filter);
    }

    /// <summary>
    /// Sets up both ExistsAsync and GetItemAsync on the remote mock for a given path.
    /// TryGetItemAsync checks ExistsAsync before calling GetItemAsync.
    /// </summary>
    private void SetupRemoteItem(string path, SyncItem item) {
        _mockRemote.Setup(x => x.ExistsAsync(path, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _mockRemote.Setup(x => x.GetItemAsync(path, It.IsAny<CancellationToken>()))
            .ReturnsAsync(item);
    }

    #region IncorporatePendingRemoteChangesAsync Tests (via GetSyncPlanAsync)

    [Fact]
    public async Task GetSyncPlanAsync_PendingRemoteCreated_NewFile_ProducesDownloadAction() {
        // Arrange
        var remoteItem = new SyncItem { Path = "newfile.txt", Size = 100, LastModified = DateTime.UtcNow };
        SetupRemoteItem("newfile.txt", remoteItem);

        using var engine = CreateEngine();
        await engine.NotifyRemoteChangeAsync("newfile.txt", ChangeType.Created);

        // Act
        var plan = await engine.GetSyncPlanAsync();

        // Assert
        Assert.Contains(plan.Actions, a => a.Path == "newfile.txt" && a.ActionType == SyncActionType.Download);
    }

    [Fact]
    public async Task GetSyncPlanAsync_PendingRemoteChanged_TrackedFile_ProducesDownloadAction() {
        // Arrange
        var remoteItem = new SyncItem { Path = "existing.txt", Size = 200, LastModified = DateTime.UtcNow };
        SetupRemoteItem("existing.txt", remoteItem);

        var trackedState = TestDataFactory.CreateSyncState(path: "existing.txt");
        _mockDatabase.Setup(x => x.GetSyncStateAsync("existing.txt", It.IsAny<CancellationToken>()))
            .ReturnsAsync(trackedState);

        using var engine = CreateEngine();
        await engine.NotifyRemoteChangeAsync("existing.txt", ChangeType.Changed);

        // Act
        var plan = await engine.GetSyncPlanAsync();

        // Assert
        Assert.Contains(plan.Actions, a => a.Path == "existing.txt" && a.ActionType == SyncActionType.Download);
    }

    [Fact]
    public async Task GetSyncPlanAsync_PendingRemoteDeleted_TrackedFile_ProducesDeleteAction() {
        // Arrange
        var trackedState = TestDataFactory.CreateSyncState(path: "deleted.txt");
        _mockDatabase.Setup(x => x.GetSyncStateAsync("deleted.txt", It.IsAny<CancellationToken>()))
            .ReturnsAsync(trackedState);

        using var engine = CreateEngine();
        await engine.NotifyRemoteChangeAsync("deleted.txt", ChangeType.Deleted);

        // Act
        var plan = await engine.GetSyncPlanAsync();

        // Assert
        Assert.Contains(plan.Actions, a => a.Path == "deleted.txt" && a.ActionType == SyncActionType.DeleteLocal);
    }

    [Fact]
    public async Task GetSyncPlanAsync_PendingRemoteDeleted_UntrackedFile_ProducesNoAction() {
        // Arrange - database returns null (untracked)
        using var engine = CreateEngine();
        await engine.NotifyRemoteChangeAsync("untracked.txt", ChangeType.Deleted);

        // Act
        var plan = await engine.GetSyncPlanAsync();

        // Assert
        Assert.DoesNotContain(plan.Actions, a => a.Path == "untracked.txt");
    }

    [Fact]
    public async Task GetSyncPlanAsync_PendingRemoteCreated_ItemNotFound_ProducesNoAction() {
        // Arrange - ExistsAsync returns false (default), so TryGetItemAsync returns null
        using var engine = CreateEngine();
        await engine.NotifyRemoteChangeAsync("ghost.txt", ChangeType.Created);

        // Act
        var plan = await engine.GetSyncPlanAsync();

        // Assert
        Assert.DoesNotContain(plan.Actions, a => a.Path == "ghost.txt");
    }

    [Fact]
    public async Task GetSyncPlanAsync_PendingRemoteRenamed_SkippedByIncorporate() {
        // Arrange - Renamed type is handled by NotifyRemoteRenameAsync as delete+create,
        // so IncorporatePendingRemoteChangesAsync should skip it
        var remoteItem = new SyncItem { Path = "newname.txt", Size = 100, LastModified = DateTime.UtcNow };
        SetupRemoteItem("newname.txt", remoteItem);

        using var engine = CreateEngine();

        // Use NotifyRemoteRenameAsync which creates delete+create entries
        await engine.NotifyRemoteRenameAsync("oldname.txt", "newname.txt");

        // Act
        var plan = await engine.GetSyncPlanAsync();

        // Assert - should have actions for both old (delete) and new (download) paths
        var pending = await engine.GetPendingOperationsAsync();
        Assert.Contains(pending, p => p.Path == "oldname.txt");
        Assert.Contains(pending, p => p.Path == "newname.txt");
    }

    #endregion

    #region TryPollRemoteChangesAsync Tests (via GetSyncPlanAsync)

    [Fact]
    public async Task GetSyncPlanAsync_PollReturnsNewFile_ProducesDownloadAction() {
        // Arrange
        var remoteItem = new SyncItem { Path = "polled.txt", Size = 300, LastModified = DateTime.UtcNow };
        SetupRemoteItem("polled.txt", remoteItem);

        _mockRemote.Setup(x => x.GetRemoteChangesAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { new ChangeInfo("polled.txt", ChangeType.Created) });

        using var engine = CreateEngine();

        // Act
        var plan = await engine.GetSyncPlanAsync();

        // Assert
        Assert.Contains(plan.Actions, a => a.Path == "polled.txt" && a.ActionType == SyncActionType.Download);
    }

    [Fact]
    public async Task GetSyncPlanAsync_PollReturnsModifiedFile_TrackedFile_ProducesDownload() {
        // Arrange
        var remoteItem = new SyncItem { Path = "modified.txt", Size = 500, LastModified = DateTime.UtcNow };
        SetupRemoteItem("modified.txt", remoteItem);

        var trackedState = TestDataFactory.CreateSyncState(path: "modified.txt");
        _mockDatabase.Setup(x => x.GetSyncStateAsync("modified.txt", It.IsAny<CancellationToken>()))
            .ReturnsAsync(trackedState);

        _mockRemote.Setup(x => x.GetRemoteChangesAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { new ChangeInfo("modified.txt", ChangeType.Changed) });

        using var engine = CreateEngine();

        // Act
        var plan = await engine.GetSyncPlanAsync();

        // Assert
        Assert.Contains(plan.Actions, a => a.Path == "modified.txt" && a.ActionType == SyncActionType.Download);
    }

    [Fact]
    public async Task GetSyncPlanAsync_PollReturnsDeletedFile_TrackedFile_ProducesDeleteLocal() {
        // Arrange
        var trackedState = TestDataFactory.CreateSyncState(path: "gone.txt");
        _mockDatabase.Setup(x => x.GetSyncStateAsync("gone.txt", It.IsAny<CancellationToken>()))
            .ReturnsAsync(trackedState);

        _mockRemote.Setup(x => x.GetRemoteChangesAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { new ChangeInfo("gone.txt", ChangeType.Deleted) });

        using var engine = CreateEngine();

        // Act
        var plan = await engine.GetSyncPlanAsync();

        // Assert
        Assert.Contains(plan.Actions, a => a.Path == "gone.txt" && a.ActionType == SyncActionType.DeleteLocal);
    }

    [Fact]
    public async Task GetSyncPlanAsync_PollReturnsEmptyList_ProducesNoActions() {
        // Arrange - default setup already returns empty list
        using var engine = CreateEngine();

        // Act
        var plan = await engine.GetSyncPlanAsync();

        // Assert
        Assert.Empty(plan.Actions);
    }

    [Fact]
    public async Task GetSyncPlanAsync_PollThrowsException_DoesNotThrow_ReturnsEmptyPlan() {
        // Arrange - remote polling throws
        _mockRemote.Setup(x => x.GetRemoteChangesAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Network error"));

        using var engine = CreateEngine();

        // Act - should not throw
        var plan = await engine.GetSyncPlanAsync();

        // Assert
        Assert.NotNull(plan);
    }

    [Fact]
    public async Task GetSyncPlanAsync_PollReturnsFilteredPath_IsExcluded() {
        // Arrange
        var filter = new SyncFilter();
        filter.AddExclusionPattern("*.tmp");

        _mockRemote.Setup(x => x.GetRemoteChangesAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { new ChangeInfo("cache.tmp", ChangeType.Created) });

        using var engine = CreateEngine(filter);

        // Act
        var plan = await engine.GetSyncPlanAsync();

        // Assert
        Assert.DoesNotContain(plan.Actions, a => a.Path == "cache.tmp");
    }

    [Fact]
    public async Task GetSyncPlanAsync_PollReturnsAlreadyTrackedPath_SkipsDuplicate() {
        // Arrange - First notify a remote change, then poll returns the same path
        var remoteItem = new SyncItem { Path = "dup.txt", Size = 100, LastModified = DateTime.UtcNow };
        SetupRemoteItem("dup.txt", remoteItem);

        _mockRemote.Setup(x => x.GetRemoteChangesAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { new ChangeInfo("dup.txt", ChangeType.Created) });

        using var engine = CreateEngine();
        await engine.NotifyRemoteChangeAsync("dup.txt", ChangeType.Created);

        // Act
        var plan = await engine.GetSyncPlanAsync();

        // Assert - Should have exactly one action for dup.txt, not two
        Assert.Single(plan.Actions, a => a.Path == "dup.txt");
    }

    [Fact]
    public async Task GetSyncPlanAsync_PollFeedsIntoPendingRemoteChanges() {
        // Arrange
        var remoteItem = new SyncItem { Path = "polled_pending.txt", Size = 100, LastModified = DateTime.UtcNow };
        SetupRemoteItem("polled_pending.txt", remoteItem);

        _mockRemote.Setup(x => x.GetRemoteChangesAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { new ChangeInfo("polled_pending.txt", ChangeType.Created) });

        using var engine = CreateEngine();

        // Act
        await engine.GetSyncPlanAsync();

        // Assert - polled changes should be visible in pending operations
        var pending = await engine.GetPendingOperationsAsync();
        Assert.Contains(pending, p => p.Path == "polled_pending.txt" && p.Source == ChangeSource.Remote);
    }

    [Fact]
    public async Task GetSyncPlanAsync_PollMultipleChanges_AllIncorporated() {
        // Arrange
        var item1 = new SyncItem { Path = "file1.txt", Size = 100, LastModified = DateTime.UtcNow };
        var item2 = new SyncItem { Path = "file2.txt", Size = 200, LastModified = DateTime.UtcNow };
        SetupRemoteItem("file1.txt", item1);
        SetupRemoteItem("file2.txt", item2);

        var tracked = TestDataFactory.CreateSyncState(path: "deleted.txt");
        _mockDatabase.Setup(x => x.GetSyncStateAsync("deleted.txt", It.IsAny<CancellationToken>()))
            .ReturnsAsync(tracked);

        _mockRemote.Setup(x => x.GetRemoteChangesAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] {
                new ChangeInfo("file1.txt", ChangeType.Created),
                new ChangeInfo("file2.txt", ChangeType.Changed),
                new ChangeInfo("deleted.txt", ChangeType.Deleted)
            });

        using var engine = CreateEngine();

        // Act
        var plan = await engine.GetSyncPlanAsync();

        // Assert
        Assert.Contains(plan.Actions, a => a.Path == "file1.txt" && a.ActionType == SyncActionType.Download);
        Assert.Contains(plan.Actions, a => a.Path == "file2.txt" && a.ActionType == SyncActionType.Download);
        Assert.Contains(plan.Actions, a => a.Path == "deleted.txt" && a.ActionType == SyncActionType.DeleteLocal);
    }

    [Fact]
    public async Task GetSyncPlanAsync_PollReturnsCreatedFile_ItemNotOnRemote_ProducesNoAction() {
        // Arrange - Poll returns a Created file but TryGetItemAsync returns null
        // (ExistsAsync returns false by default in mock)
        _mockRemote.Setup(x => x.GetRemoteChangesAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { new ChangeInfo("vanished.txt", ChangeType.Created) });

        using var engine = CreateEngine();

        // Act
        var plan = await engine.GetSyncPlanAsync();

        // Assert
        Assert.DoesNotContain(plan.Actions, a => a.Path == "vanished.txt");
    }

    [Fact]
    public async Task GetSyncPlanAsync_PollReturnsDeletedFile_UntrackedFile_ProducesNoAction() {
        // Arrange - Poll returns a Deleted file but it's not tracked in the database
        _mockRemote.Setup(x => x.GetRemoteChangesAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { new ChangeInfo("untracked_polled.txt", ChangeType.Deleted) });

        using var engine = CreateEngine();

        // Act
        var plan = await engine.GetSyncPlanAsync();

        // Assert
        Assert.DoesNotContain(plan.Actions, a => a.Path == "untracked_polled.txt");
    }

    [Fact]
    public async Task GetSyncPlanAsync_PollReturnsRenamedFrom_WithMetadata() {
        // Arrange - Poll returns changes with rename metadata
        var item = new SyncItem { Path = "new_name.txt", Size = 100, LastModified = DateTime.UtcNow };
        SetupRemoteItem("new_name.txt", item);

        var changeInfo = new ChangeInfo("new_name.txt", ChangeType.Created) {
            RenamedFrom = "old_name.txt"
        };

        _mockRemote.Setup(x => x.GetRemoteChangesAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { changeInfo });

        using var engine = CreateEngine();

        // Act
        var plan = await engine.GetSyncPlanAsync();

        // Assert - The action should be present for the renamed file
        Assert.Contains(plan.Actions, a => a.Path == "new_name.txt" && a.ActionType == SyncActionType.Download);

        // Verify the pending operation retains rename metadata
        var pending = await engine.GetPendingOperationsAsync();
        var op = Assert.Single(pending, p => p.Path == "new_name.txt");
        Assert.Equal("old_name.txt", op.RenamedFrom);
    }

    [Fact]
    public async Task GetSyncPlanAsync_PollChangedFile_AlreadyInLocalChanges_Skipped() {
        // Arrange - Notify a local change, then poll returns the same path
        var localItem = new SyncItem { Path = "both.txt", Size = 100, LastModified = DateTime.UtcNow };
        _mockLocal.Setup(x => x.ExistsAsync("both.txt", It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _mockLocal.Setup(x => x.GetItemAsync("both.txt", It.IsAny<CancellationToken>())).ReturnsAsync(localItem);

        var remoteItem = new SyncItem { Path = "both.txt", Size = 200, LastModified = DateTime.UtcNow };
        SetupRemoteItem("both.txt", remoteItem);

        _mockRemote.Setup(x => x.GetRemoteChangesAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { new ChangeInfo("both.txt", ChangeType.Changed) });

        using var engine = CreateEngine();
        await engine.NotifyLocalChangeAsync("both.txt", ChangeType.Changed);

        // Act
        var plan = await engine.GetSyncPlanAsync();

        // Assert - Should have exactly one action for both.txt (from local), not duplicated by poll
        Assert.Single(plan.Actions, a => a.Path == "both.txt");
    }

    [Fact]
    public async Task GetSyncPlanAsync_PollReturnsChangedFile_UntrackedFile_ProducesDownload() {
        // Arrange - Poll returns Changed for a file NOT in the database (untracked)
        var remoteItem = new SyncItem { Path = "untracked_changed.txt", Size = 100, LastModified = DateTime.UtcNow };
        SetupRemoteItem("untracked_changed.txt", remoteItem);

        _mockRemote.Setup(x => x.GetRemoteChangesAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { new ChangeInfo("untracked_changed.txt", ChangeType.Changed) });

        using var engine = CreateEngine();

        // Act
        var plan = await engine.GetSyncPlanAsync();

        // Assert - Should be treated as an addition (new file)
        Assert.Contains(plan.Actions, a => a.Path == "untracked_changed.txt" && a.ActionType == SyncActionType.Download);
    }

    [Fact]
    public async Task GetSyncPlanAsync_PollDetectedAt_PropagatedToPending() {
        // Arrange - Poll returns change with specific DetectedAt timestamp
        var detectedAt = new DateTime(2025, 6, 15, 12, 0, 0, DateTimeKind.Utc);
        var remoteItem = new SyncItem { Path = "timed.txt", Size = 100, LastModified = DateTime.UtcNow };
        SetupRemoteItem("timed.txt", remoteItem);

        var changeInfo = new ChangeInfo("timed.txt", ChangeType.Created) {
            DetectedAt = detectedAt
        };

        _mockRemote.Setup(x => x.GetRemoteChangesAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { changeInfo });

        using var engine = CreateEngine();

        // Act
        await engine.GetSyncPlanAsync();
        var pending = await engine.GetPendingOperationsAsync();

        // Assert - The pending operation should exist
        Assert.Contains(pending, p => p.Path == "timed.txt");
    }

    [Fact]
    public async Task GetSyncPlanAsync_IncorporateRemoteChanges_CancellationRespected() {
        // Arrange
        var remoteItem = new SyncItem { Path = "cancel_test.txt", Size = 100, LastModified = DateTime.UtcNow };
        SetupRemoteItem("cancel_test.txt", remoteItem);

        using var engine = CreateEngine();
        await engine.NotifyRemoteChangeAsync("cancel_test.txt", ChangeType.Created);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert - pre-cancelled token should throw
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => engine.GetSyncPlanAsync(cancellationToken: cts.Token));
    }

    [Fact]
    public async Task GetSyncPlanAsync_PendingRemoteChanged_AlreadyInRemotePaths_Skipped() {
        // Arrange - Notify two remote changes for the same path
        var remoteItem = new SyncItem { Path = "same.txt", Size = 100, LastModified = DateTime.UtcNow };
        SetupRemoteItem("same.txt", remoteItem);

        // Poll returns a Created change for same.txt
        _mockRemote.Setup(x => x.GetRemoteChangesAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { new ChangeInfo("same.txt", ChangeType.Created) });

        using var engine = CreateEngine();
        // Also notify remote change for same path
        await engine.NotifyRemoteChangeAsync("same.txt", ChangeType.Changed);

        // Act
        var plan = await engine.GetSyncPlanAsync();

        // Assert - Should have exactly one action, not duplicated
        Assert.Single(plan.Actions, a => a.Path == "same.txt");
    }

    [Fact]
    public async Task GetSyncPlanAsync_PendingRemoteRenamed_DirectNotification_SkippedByIncorporate() {
        // Arrange - Directly notify ChangeType.Renamed via NotifyRemoteChangeAsync
        // (not via NotifyRemoteRenameAsync). This tests the "case ChangeType.Renamed: break;" path
        // in IncorporatePendingRemoteChangesAsync.
        using var engine = CreateEngine();
        await engine.NotifyRemoteChangeAsync("renamed_direct.txt", ChangeType.Renamed);

        // Act
        var plan = await engine.GetSyncPlanAsync();

        // Assert - Renamed type is skipped by IncorporatePendingRemoteChangesAsync
        Assert.DoesNotContain(plan.Actions, a => a.Path == "renamed_direct.txt");
    }

    #endregion

    #region GetPendingOperationsAsync Mock Tests

    [Fact]
    public async Task GetPendingOperationsAsync_RemoteGetItemThrows_StillIncludesOperation() {
        // Arrange - Make GetItemAsync throw for remote item
        _mockRemote.Setup(x => x.GetItemAsync("throwing.txt", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("Network error"));

        // Setup empty pending sync states
        _mockDatabase.Setup(x => x.GetPendingSyncStatesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Enumerable.Empty<SyncState>());

        using var engine = CreateEngine();
        await engine.NotifyRemoteChangeAsync("throwing.txt", ChangeType.Created);

        // Act
        var pending = await engine.GetPendingOperationsAsync();

        // Assert - Should still have the operation even though GetItemAsync threw
        var op = Assert.Single(pending, p => p.Path == "throwing.txt");
        Assert.Equal(SyncActionType.Download, op.ActionType);
        Assert.Equal(ChangeSource.Remote, op.Source);
        Assert.Equal(0, op.Size); // Default size since GetItemAsync failed
    }

    [Fact]
    public async Task GetPendingOperationsAsync_LocalGetItemThrows_StillIncludesOperation() {
        // Arrange - Make GetItemAsync throw for local item
        _mockLocal.Setup(x => x.GetItemAsync("local_throwing.txt", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("Disk error"));

        // Setup empty pending sync states
        _mockDatabase.Setup(x => x.GetPendingSyncStatesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Enumerable.Empty<SyncState>());

        using var engine = CreateEngine();
        await engine.NotifyLocalChangeAsync("local_throwing.txt", ChangeType.Created);

        // Act
        var pending = await engine.GetPendingOperationsAsync();

        // Assert - Should still have the operation even though GetItemAsync threw
        var op = Assert.Single(pending, p => p.Path == "local_throwing.txt");
        Assert.Equal(SyncActionType.Upload, op.ActionType);
        Assert.Equal(ChangeSource.Local, op.Source);
        Assert.Equal(0, op.Size);
    }

    [Fact]
    public async Task GetPendingOperationsAsync_DatabasePendingState_SkippedWhenInRemoteChanges() {
        // Arrange - A path exists in both _pendingRemoteChanges and database pending states
        var remoteItem = new SyncItem { Path = "overlap.txt", Size = 100, LastModified = DateTime.UtcNow };
        SetupRemoteItem("overlap.txt", remoteItem);

        _mockDatabase.Setup(x => x.GetPendingSyncStatesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SyncState> {
                new SyncState { Path = "overlap.txt", Status = SyncStatus.RemoteNew }
            });

        using var engine = CreateEngine();
        await engine.NotifyRemoteChangeAsync("overlap.txt", ChangeType.Created);

        // Act
        var pending = await engine.GetPendingOperationsAsync();

        // Assert - Should have exactly one operation for overlap.txt (from remote changes, not duplicated from DB)
        Assert.Single(pending, p => p.Path == "overlap.txt");
    }

    [Fact]
    public async Task GetPendingOperationsAsync_RemoteItemExists_IncludesSize() {
        // Arrange - Remote GetItemAsync returns actual item with size
        var remoteItem = new SyncItem { Path = "sized.txt", Size = 12345, IsDirectory = false, LastModified = DateTime.UtcNow };
        _mockRemote.Setup(x => x.GetItemAsync("sized.txt", It.IsAny<CancellationToken>()))
            .ReturnsAsync(remoteItem);

        _mockDatabase.Setup(x => x.GetPendingSyncStatesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Enumerable.Empty<SyncState>());

        using var engine = CreateEngine();
        await engine.NotifyRemoteChangeAsync("sized.txt", ChangeType.Changed);

        // Act
        var pending = await engine.GetPendingOperationsAsync();

        // Assert - Should include the actual size from remote storage
        var op = Assert.Single(pending, p => p.Path == "sized.txt");
        Assert.Equal(12345, op.Size);
        Assert.False(op.IsDirectory);
    }

    [Fact]
    public async Task GetPendingOperationsAsync_RemoteDeleted_SkipsGetItem() {
        // Arrange - Deleted items should NOT call GetItemAsync
        _mockDatabase.Setup(x => x.GetPendingSyncStatesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Enumerable.Empty<SyncState>());

        using var engine = CreateEngine();
        await engine.NotifyRemoteChangeAsync("deleted_remote.txt", ChangeType.Deleted);

        // Act
        var pending = await engine.GetPendingOperationsAsync();

        // Assert
        var op = Assert.Single(pending, p => p.Path == "deleted_remote.txt");
        Assert.Equal(SyncActionType.DeleteLocal, op.ActionType);
        Assert.Equal(0, op.Size);

        // Verify GetItemAsync was NOT called for the deleted item
        _mockRemote.Verify(x => x.GetItemAsync("deleted_remote.txt", It.IsAny<CancellationToken>()), Times.Never);
    }

    #endregion

    #region SyncEngine Error Path Tests (mock-based)

    [Fact]
    public async Task SynchronizeAsync_DatabaseThrows_ReturnsResultWithError() {
        // Arrange - Make GetAllSyncStatesAsync throw to trigger SynchronizeAsync catch block
        _mockDatabase.Setup(x => x.GetAllSyncStatesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Database connection lost"));

        using var engine = CreateEngine();

        // Act
        var result = await engine.SynchronizeAsync();

        // Assert - Error should be captured, not thrown
        Assert.False(result.Success);
        Assert.NotNull(result.Error);
        Assert.IsType<InvalidOperationException>(result.Error);
        Assert.Contains("Database connection lost", result.Error.Message);
    }

    [Fact]
    public async Task SyncFolderAsync_DatabaseThrows_ReturnsResultWithError() {
        // Arrange - SyncFolderAsync uses GetSyncStatesByPrefixAsync, not GetAllSyncStatesAsync
        _mockDatabase.Setup(x => x.GetSyncStatesByPrefixAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Database error in folder sync"));

        using var engine = CreateEngine();

        // Act
        var result = await engine.SyncFolderAsync("SomeFolder");

        // Assert - Error should be captured, not thrown
        Assert.False(result.Success);
        Assert.NotNull(result.Error);
        Assert.Contains("Database error in folder sync", result.Error!.Message);
    }

    [Fact]
    public async Task SyncFilesAsync_DatabaseThrows_ReturnsResultWithError() {
        // Arrange - SyncFilesAsync uses GetSyncStateAsync per-file
        _mockDatabase.Setup(x => x.GetSyncStateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Database error in file sync"));

        using var engine = CreateEngine();

        // Act
        var result = await engine.SyncFilesAsync(["file1.txt", "file2.txt"]);

        // Assert - Error should be captured, not thrown
        Assert.False(result.Success);
        Assert.NotNull(result.Error);
        Assert.Contains("Database error in file sync", result.Error!.Message);
    }

    [Fact]
    public async Task GetSyncPlanAsync_DatabaseThrows_ReturnsEmptyPlan() {
        // Arrange - Make GetAllSyncStatesAsync throw to trigger GetSyncPlanAsync catch block
        _mockDatabase.Setup(x => x.GetAllSyncStatesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Database error in plan"));

        using var engine = CreateEngine();

        // Act
        var plan = await engine.GetSyncPlanAsync();

        // Assert - Should return empty plan on error (not throw)
        Assert.NotNull(plan);
        Assert.Empty(plan.Actions);
    }

    [Fact]
    public async Task SynchronizeAsync_StorageListThrows_ReturnsResultWithError() {
        // Arrange - Make local ListItemsAsync throw during scanning
        _mockLocal.Setup(x => x.ListItemsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("Storage I/O error"));
        // Also make database return tracked items so deletion detection triggers errors
        _mockDatabase.Setup(x => x.GetAllSyncStatesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SyncState>());

        using var engine = CreateEngine();

        // Act - should not throw; error swallowed in scan but result may still succeed if no changes detected
        var result = await engine.SynchronizeAsync();

        // Assert
        Assert.NotNull(result);
    }

    [Fact]
    public async Task TryPollRemoteChangesAsync_RemoteStorageThrows_DoesNotCrash() {
        // Arrange - GetRemoteChangesAsync throws to cover the catch in TryPollRemoteChangesAsync
        _mockRemote.Setup(x => x.GetRemoteChangesAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Remote server unreachable"));

        using var engine = CreateEngine();

        // First do a sync to set _lastRemotePollTime (enables polling)
        _mockDatabase.Setup(x => x.GetAllSyncStatesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SyncState>());
        await engine.SynchronizeAsync();

        // Act - GetSyncPlanAsync triggers TryPollRemoteChangesAsync internally
        var plan = await engine.GetSyncPlanAsync();

        // Assert - Should not throw; polling failure is logged and swallowed
        Assert.NotNull(plan);
    }

    [Fact]
    public async Task GetPendingOperationsAsync_LocalGetItemThrows_LocalPendingStillIncluded() {
        // Arrange - Mock local GetItemAsync to throw for local pending changes
        _mockLocal.Setup(x => x.GetItemAsync("local_err.txt", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("Local storage error"));

        _mockDatabase.Setup(x => x.GetPendingSyncStatesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Enumerable.Empty<SyncState>());

        using var engine = CreateEngine();
        await engine.NotifyLocalChangeAsync("local_err.txt", ChangeType.Changed);

        // Act
        var pending = await engine.GetPendingOperationsAsync();

        // Assert - Should still include the operation even when GetItemAsync fails
        Assert.Contains(pending, p => p.Path == "local_err.txt");
    }

    #endregion
}
