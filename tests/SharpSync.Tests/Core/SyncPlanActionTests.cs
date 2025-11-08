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
    public void SyncPlanAction_Download_GeneratesCorrectDescription() {
        // Arrange
        var action = new SyncPlanAction {
            ActionType = SyncActionType.Download,
            Path = "documents/report.pdf",
            Size = 1024 * 1024 * 2, // 2 MB
            IsDirectory = false
        };

        // Act
        var description = action.Description;

        // Assert
        Assert.Contains("Download", description);
        Assert.Contains("documents/report.pdf", description);
        Assert.Contains("2.0 MB", description);
    }

    [Fact]
    public void SyncPlanAction_Upload_GeneratesCorrectDescription() {
        // Arrange
        var action = new SyncPlanAction {
            ActionType = SyncActionType.Upload,
            Path = "photos/vacation.jpg",
            Size = 1024 * 512, // 512 KB
            IsDirectory = false
        };

        // Act
        var description = action.Description;

        // Assert
        Assert.Contains("Upload", description);
        Assert.Contains("photos/vacation.jpg", description);
        Assert.Contains("512.0 KB", description);
    }

    [Fact]
    public void SyncPlanAction_DownloadDirectory_GeneratesCorrectDescription() {
        // Arrange
        var action = new SyncPlanAction {
            ActionType = SyncActionType.Download,
            Path = "MyFolder",
            Size = 0,
            IsDirectory = true
        };

        // Act
        var description = action.Description;

        // Assert
        Assert.Contains("Download", description);
        Assert.Contains("MyFolder/", description);
        Assert.DoesNotContain("KB", description);
        Assert.DoesNotContain("MB", description);
    }

    [Fact]
    public void SyncPlanAction_DeleteLocal_GeneratesCorrectDescription() {
        // Arrange
        var action = new SyncPlanAction {
            ActionType = SyncActionType.DeleteLocal,
            Path = "old-file.txt",
            IsDirectory = false
        };

        // Act
        var description = action.Description;

        // Assert
        Assert.Contains("Delete", description);
        Assert.Contains("old-file.txt", description);
        Assert.Contains("local storage", description);
    }

    [Fact]
    public void SyncPlanAction_DeleteRemote_GeneratesCorrectDescription() {
        // Arrange
        var action = new SyncPlanAction {
            ActionType = SyncActionType.DeleteRemote,
            Path = "archive/old-data.bin",
            IsDirectory = false
        };

        // Act
        var description = action.Description;

        // Assert
        Assert.Contains("Delete", description);
        Assert.Contains("archive/old-data.bin", description);
        Assert.Contains("remote storage", description);
    }

    [Fact]
    public void SyncPlanAction_Conflict_GeneratesCorrectDescription() {
        // Arrange
        var action = new SyncPlanAction {
            ActionType = SyncActionType.Conflict,
            Path = "document.docx",
            ConflictType = ConflictType.BothModified,
            IsDirectory = false
        };

        // Act
        var description = action.Description;

        // Assert
        Assert.Contains("Resolve conflict", description);
        Assert.Contains("document.docx", description);
        Assert.Contains("BothModified", description);
    }

    [Fact]
    public void SyncPlanAction_ConflictWithoutType_GeneratesCorrectDescription() {
        // Arrange
        var action = new SyncPlanAction {
            ActionType = SyncActionType.Conflict,
            Path = "file.txt",
            IsDirectory = false
        };

        // Act
        var description = action.Description;

        // Assert
        Assert.Contains("Resolve conflict", description);
        Assert.Contains("file.txt", description);
        Assert.DoesNotContain("BothModified", description);
    }

    [Theory]
    [InlineData(100, "100 B")]
    [InlineData(1024, "1.0 KB")]
    [InlineData(1536, "1.5 KB")]
    [InlineData(1024 * 1024, "1.0 MB")]
    [InlineData(1024 * 1024 * 1024, "1.0 GB")]
    [InlineData(1024L * 1024L * 1024L * 2, "2.0 GB")]
    public void SyncPlanAction_FormatSize_ReturnsCorrectFormat(long bytes, string expected) {
        // Arrange
        var action = new SyncPlanAction {
            ActionType = SyncActionType.Download,
            Path = "test.file",
            Size = bytes,
            IsDirectory = false
        };

        // Act
        var description = action.Description;

        // Assert
        Assert.Contains(expected, description);
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
