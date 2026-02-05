namespace Oire.SharpSync.Tests.Core;

public class SyncPlanActionTests {
    [Fact]
    public void SyncPlanAction_DefaultInitialization_HasEmptyPath() {
        // Arrange & Act
        var action = new SyncPlanAction();

        // Assert
        Assert.Equal(string.Empty, action.Path);
        Assert.Equal(SyncActionType.Download, action.ActionType);
        Assert.False(action.IsDirectory);
        Assert.Equal(0, action.Size);
        Assert.Null(action.LastModified);
        Assert.Null(action.ConflictType);
        Assert.Equal(0, action.Priority);
    }

    [Fact]
    public void SyncPlanAction_WithLastModified_StoresCorrectly() {
        // Arrange
        var lastModified = new DateTime(2024, 1, 15, 10, 30, 0, DateTimeKind.Utc);
        var action = new SyncPlanAction {
            ActionType = SyncActionType.Upload,
            Path = "data.json",
            LastModified = lastModified,
            IsDirectory = false
        };

        // Assert
        Assert.Equal(lastModified, action.LastModified);
    }

    [Fact]
    public void SyncPlanAction_WithPriority_StoresCorrectly() {
        // Arrange
        var action = new SyncPlanAction {
            ActionType = SyncActionType.Download,
            Path = "important.doc",
            Priority = 1000,
            IsDirectory = false
        };

        // Assert
        Assert.Equal(1000, action.Priority);
    }

    [Fact]
    public void SyncPlanAction_AllPropertiesSet_PreservesValues() {
        // Arrange
        var lastModified = DateTime.UtcNow;
        var action = new SyncPlanAction {
            ActionType = SyncActionType.Upload,
            Path = "test/file.txt",
            IsDirectory = false,
            Size = 2048,
            LastModified = lastModified,
            ConflictType = ConflictType.ModifiedLocallyDeletedRemotely,
            Priority = 500
        };

        // Assert
        Assert.Equal(SyncActionType.Upload, action.ActionType);
        Assert.Equal("test/file.txt", action.Path);
        Assert.False(action.IsDirectory);
        Assert.Equal(2048, action.Size);
        Assert.Equal(lastModified, action.LastModified);
        Assert.Equal(ConflictType.ModifiedLocallyDeletedRemotely, action.ConflictType);
        Assert.Equal(500, action.Priority);
    }
}
