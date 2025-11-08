namespace Oire.SharpSync.Tests.Core;

public class SyncPlanTests {
    [Fact]
    public void SyncPlan_DefaultInitialization_HasEmptyActions() {
        // Arrange & Act
        var plan = new SyncPlan();

        // Assert
        Assert.Empty(plan.Actions);
        Assert.Equal(0, plan.TotalActions);
        Assert.False(plan.HasChanges);
        Assert.False(plan.HasConflicts);
    }

    [Fact]
    public void SyncPlan_WithNoActions_ReturnsNoChangesMessage() {
        // Arrange
        var plan = new SyncPlan { Actions = Array.Empty<SyncPlanAction>() };

        // Act
        var summary = plan.Summary;

        // Assert
        Assert.Equal("No changes to synchronize", summary);
        Assert.False(plan.HasChanges);
    }

    [Fact]
    public void SyncPlan_WithDownloads_GroupsCorrectly() {
        // Arrange
        var actions = new List<SyncPlanAction>
        {
            new() { ActionType = SyncActionType.Download, Path = "file1.txt", Size = 1024, IsDirectory = false },
            new() { ActionType = SyncActionType.Download, Path = "file2.txt", Size = 2048, IsDirectory = false },
            new() { ActionType = SyncActionType.Upload, Path = "file3.txt", Size = 512, IsDirectory = false }
        };
        var plan = new SyncPlan { Actions = actions };

        // Act
        var downloads = plan.Downloads;
        var uploads = plan.Uploads;

        // Assert
        Assert.Equal(2, downloads.Count);
        Assert.Single(uploads);
        Assert.Equal(2, plan.DownloadCount);
        Assert.Equal(1, plan.UploadCount);
    }

    [Fact]
    public void SyncPlan_WithUploads_GroupsCorrectly() {
        // Arrange
        var actions = new List<SyncPlanAction>
        {
            new() { ActionType = SyncActionType.Upload, Path = "upload1.txt", Size = 1024, IsDirectory = false },
            new() { ActionType = SyncActionType.Upload, Path = "upload2.txt", Size = 2048, IsDirectory = false }
        };
        var plan = new SyncPlan { Actions = actions };

        // Act
        var uploads = plan.Uploads;

        // Assert
        Assert.Equal(2, uploads.Count);
        Assert.Equal(2, plan.UploadCount);
        Assert.Equal(0, plan.DownloadCount);
    }

    [Fact]
    public void SyncPlan_WithDeletes_GroupsCorrectly() {
        // Arrange
        var actions = new List<SyncPlanAction>
        {
            new() { ActionType = SyncActionType.DeleteLocal, Path = "local1.txt", IsDirectory = false },
            new() { ActionType = SyncActionType.DeleteLocal, Path = "local2.txt", IsDirectory = false },
            new() { ActionType = SyncActionType.DeleteRemote, Path = "remote1.txt", IsDirectory = false }
        };
        var plan = new SyncPlan { Actions = actions };

        // Act
        var localDeletes = plan.LocalDeletes;
        var remoteDeletes = plan.RemoteDeletes;

        // Assert
        Assert.Equal(2, localDeletes.Count);
        Assert.Single(remoteDeletes);
        Assert.Equal(3, plan.DeleteCount);
    }

    [Fact]
    public void SyncPlan_WithConflicts_GroupsCorrectly() {
        // Arrange
        var actions = new List<SyncPlanAction>
        {
            new() { ActionType = SyncActionType.Conflict, Path = "conflict1.txt", ConflictType = ConflictType.BothModified, IsDirectory = false },
            new() { ActionType = SyncActionType.Conflict, Path = "conflict2.txt", ConflictType = ConflictType.DeletedLocallyModifiedRemotely, IsDirectory = false },
            new() { ActionType = SyncActionType.Download, Path = "normal.txt", Size = 1024, IsDirectory = false }
        };
        var plan = new SyncPlan { Actions = actions };

        // Act
        var conflicts = plan.Conflicts;

        // Assert
        Assert.Equal(2, conflicts.Count);
        Assert.Equal(2, plan.ConflictCount);
        Assert.True(plan.HasConflicts);
    }

    [Fact]
    public void SyncPlan_TotalActions_IncludesAllTypes() {
        // Arrange
        var actions = new List<SyncPlanAction>
        {
            new() { ActionType = SyncActionType.Download, Path = "d1.txt", Size = 100, IsDirectory = false },
            new() { ActionType = SyncActionType.Upload, Path = "u1.txt", Size = 200, IsDirectory = false },
            new() { ActionType = SyncActionType.DeleteLocal, Path = "dl1.txt", IsDirectory = false },
            new() { ActionType = SyncActionType.DeleteRemote, Path = "dr1.txt", IsDirectory = false },
            new() { ActionType = SyncActionType.Conflict, Path = "c1.txt", IsDirectory = false }
        };
        var plan = new SyncPlan { Actions = actions };

        // Assert
        Assert.Equal(5, plan.TotalActions);
        Assert.True(plan.HasChanges);
    }

    [Fact]
    public void SyncPlan_TotalDownloadSize_CalculatesCorrectly() {
        // Arrange
        var actions = new List<SyncPlanAction>
        {
            new() { ActionType = SyncActionType.Download, Path = "file1.txt", Size = 1024, IsDirectory = false },
            new() { ActionType = SyncActionType.Download, Path = "file2.txt", Size = 2048, IsDirectory = false },
            new() { ActionType = SyncActionType.Download, Path = "folder/", Size = 0, IsDirectory = true }, // Should not count
            new() { ActionType = SyncActionType.Upload, Path = "file3.txt", Size = 512, IsDirectory = false } // Should not count
        };
        var plan = new SyncPlan { Actions = actions };

        // Assert
        Assert.Equal(3072, plan.TotalDownloadSize); // 1024 + 2048
    }

    [Fact]
    public void SyncPlan_TotalUploadSize_CalculatesCorrectly() {
        // Arrange
        var actions = new List<SyncPlanAction>
        {
            new() { ActionType = SyncActionType.Upload, Path = "file1.txt", Size = 500, IsDirectory = false },
            new() { ActionType = SyncActionType.Upload, Path = "file2.txt", Size = 1500, IsDirectory = false },
            new() { ActionType = SyncActionType.Upload, Path = "folder/", Size = 0, IsDirectory = true }, // Should not count
            new() { ActionType = SyncActionType.Download, Path = "file3.txt", Size = 1024, IsDirectory = false } // Should not count
        };
        var plan = new SyncPlan { Actions = actions };

        // Assert
        Assert.Equal(2000, plan.TotalUploadSize); // 500 + 1500
    }

    [Fact]
    public void SyncPlan_Summary_OnlyDownloads_FormatsCorrectly() {
        // Arrange
        var actions = new List<SyncPlanAction>
        {
            new() { ActionType = SyncActionType.Download, Path = "file1.txt", Size = 1024 * 1024, IsDirectory = false },
            new() { ActionType = SyncActionType.Download, Path = "file2.txt", Size = 512 * 1024, IsDirectory = false }
        };
        var plan = new SyncPlan { Actions = actions };

        // Act
        var summary = plan.Summary;

        // Assert
        Assert.Contains("2 downloads", summary);
        Assert.Contains("1.5 MB", summary);
    }

    [Fact]
    public void SyncPlan_Summary_OnlyUploads_FormatsCorrectly() {
        // Arrange
        var actions = new List<SyncPlanAction>
        {
            new() { ActionType = SyncActionType.Upload, Path = "file1.txt", Size = 2 * 1024 * 1024, IsDirectory = false }
        };
        var plan = new SyncPlan { Actions = actions };

        // Act
        var summary = plan.Summary;

        // Assert
        Assert.Contains("1 upload", summary); // Singular
        Assert.Contains("2.0 MB", summary);
    }

    [Fact]
    public void SyncPlan_Summary_MixedActions_FormatsCorrectly() {
        // Arrange
        var actions = new List<SyncPlanAction>
        {
            new() { ActionType = SyncActionType.Download, Path = "d1.txt", Size = 1024 * 1024, IsDirectory = false },
            new() { ActionType = SyncActionType.Download, Path = "d2.txt", Size = 1024 * 1024, IsDirectory = false },
            new() { ActionType = SyncActionType.Upload, Path = "u1.txt", Size = 512 * 1024, IsDirectory = false },
            new() { ActionType = SyncActionType.DeleteLocal, Path = "del1.txt", IsDirectory = false }
        };
        var plan = new SyncPlan { Actions = actions };

        // Act
        var summary = plan.Summary;

        // Assert
        Assert.Contains("2 downloads", summary);
        Assert.Contains("2.0 MB", summary);
        Assert.Contains("1 upload", summary);
        Assert.Contains("512.0 KB", summary);
        Assert.Contains("1 delete", summary);
    }

    [Fact]
    public void SyncPlan_Summary_WithConflicts_IncludesConflicts() {
        // Arrange
        var actions = new List<SyncPlanAction>
        {
            new() { ActionType = SyncActionType.Download, Path = "d1.txt", Size = 1024, IsDirectory = false },
            new() { ActionType = SyncActionType.Conflict, Path = "c1.txt", IsDirectory = false },
            new() { ActionType = SyncActionType.Conflict, Path = "c2.txt", IsDirectory = false }
        };
        var plan = new SyncPlan { Actions = actions };

        // Act
        var summary = plan.Summary;

        // Assert
        Assert.Contains("1 download", summary);
        Assert.Contains("2 conflicts", summary);
    }

    [Fact]
    public void SyncPlan_Summary_MultipleDeletes_UsesPluralForm() {
        // Arrange
        var actions = new List<SyncPlanAction>
        {
            new() { ActionType = SyncActionType.DeleteLocal, Path = "del1.txt", IsDirectory = false },
            new() { ActionType = SyncActionType.DeleteRemote, Path = "del2.txt", IsDirectory = false },
            new() { ActionType = SyncActionType.DeleteLocal, Path = "del3.txt", IsDirectory = false }
        };
        var plan = new SyncPlan { Actions = actions };

        // Act
        var summary = plan.Summary;

        // Assert
        Assert.Contains("3 deletes", summary); // Plural
    }

    [Fact]
    public void SyncPlan_Summary_OnlyDirectories_ShowsCountWithoutSize() {
        // Arrange
        var actions = new List<SyncPlanAction>
        {
            new() { ActionType = SyncActionType.Download, Path = "folder1/", Size = 0, IsDirectory = true },
            new() { ActionType = SyncActionType.Download, Path = "folder2/", Size = 0, IsDirectory = true }
        };
        var plan = new SyncPlan { Actions = actions };

        // Act
        var summary = plan.Summary;

        // Assert
        Assert.Contains("2 downloads", summary);
        // Should not show size or show 0 B
    }

    [Fact]
    public void SyncPlan_HasChanges_WithActions_ReturnsTrue() {
        // Arrange
        var actions = new List<SyncPlanAction>
        {
            new() { ActionType = SyncActionType.Download, Path = "file.txt", Size = 100, IsDirectory = false }
        };
        var plan = new SyncPlan { Actions = actions };

        // Assert
        Assert.True(plan.HasChanges);
    }

    [Fact]
    public void SyncPlan_HasConflicts_WithConflicts_ReturnsTrue() {
        // Arrange
        var actions = new List<SyncPlanAction>
        {
            new() { ActionType = SyncActionType.Conflict, Path = "conflict.txt", IsDirectory = false }
        };
        var plan = new SyncPlan { Actions = actions };

        // Assert
        Assert.True(plan.HasConflicts);
    }

    [Fact]
    public void SyncPlan_HasConflicts_WithoutConflicts_ReturnsFalse() {
        // Arrange
        var actions = new List<SyncPlanAction>
        {
            new() { ActionType = SyncActionType.Download, Path = "file.txt", Size = 100, IsDirectory = false }
        };
        var plan = new SyncPlan { Actions = actions };

        // Assert
        Assert.False(plan.HasConflicts);
    }

    [Fact]
    public void SyncPlan_LargeDataSizes_FormatsCorrectly() {
        // Arrange
        var actions = new List<SyncPlanAction>
        {
            new() { ActionType = SyncActionType.Download, Path = "big.iso", Size = 2L * 1024 * 1024 * 1024, IsDirectory = false }, // 2 GB
            new() { ActionType = SyncActionType.Upload, Path = "video.mp4", Size = 500L * 1024 * 1024, IsDirectory = false } // 500 MB
        };
        var plan = new SyncPlan { Actions = actions };

        // Act
        var summary = plan.Summary;

        // Assert
        Assert.Contains("2.0 GB", summary);
        Assert.Contains("500.0 MB", summary);
    }
}
