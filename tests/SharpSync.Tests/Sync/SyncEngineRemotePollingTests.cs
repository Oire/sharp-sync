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

    #endregion
}
