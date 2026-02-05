namespace Oire.SharpSync.Tests.Core;

public class ConflictAnalysisTests {
    [Fact]
    public void Constructor_RequiredProperties_InitializesCorrectly() {
        // Arrange
        var filePath = "test.txt";
        var conflictType = ConflictType.BothModified;

        // Act
        var analysis = new ConflictAnalysis {
            FilePath = filePath,
            ConflictType = conflictType
        };

        // Assert
        Assert.Equal(filePath, analysis.FilePath);
        Assert.Equal(conflictType, analysis.ConflictType);
    }

    [Fact]
    public void Constructor_AllProperties_InitializesCorrectly() {
        // Arrange
        var filePath = "documents/test.txt";
        var conflictType = ConflictType.BothModified;
        var localItem = new SyncItem { Path = filePath, Size = 1024, LastModified = DateTime.UtcNow };
        var remoteItem = new SyncItem { Path = filePath, Size = 2048, LastModified = DateTime.UtcNow.AddMinutes(5) };
        var localModified = DateTime.UtcNow.AddHours(-1);
        var remoteModified = DateTime.UtcNow;

        // Act
        var analysis = new ConflictAnalysis {
            FilePath = filePath,
            ConflictType = conflictType,
            LocalItem = localItem,
            RemoteItem = remoteItem,
            RecommendedResolution = ConflictResolution.UseRemote,
            LocalSize = 1024,
            RemoteSize = 2048,
            SizeDifference = 1024,
            LocalModified = localModified,
            RemoteModified = remoteModified,
            TimeDifference = 3600,
            NewerVersion = "Remote",
            IsLikelyBinary = false,
            IsLikelyTextFile = true
        };

        // Assert
        Assert.Equal(filePath, analysis.FilePath);
        Assert.Equal(conflictType, analysis.ConflictType);
        Assert.Equal(localItem, analysis.LocalItem);
        Assert.Equal(remoteItem, analysis.RemoteItem);
        Assert.Equal(ConflictResolution.UseRemote, analysis.RecommendedResolution);
        Assert.Equal(1024, analysis.LocalSize);
        Assert.Equal(2048, analysis.RemoteSize);
        Assert.Equal(1024, analysis.SizeDifference);
        Assert.Equal(localModified, analysis.LocalModified);
        Assert.Equal(remoteModified, analysis.RemoteModified);
        Assert.Equal(3600, analysis.TimeDifference);
        Assert.Equal("Remote", analysis.NewerVersion);
        Assert.False(analysis.IsLikelyBinary);
        Assert.True(analysis.IsLikelyTextFile);
    }

    [Theory]
    [InlineData(ConflictType.BothModified)]
    [InlineData(ConflictType.DeletedLocallyModifiedRemotely)]
    [InlineData(ConflictType.ModifiedLocallyDeletedRemotely)]
    [InlineData(ConflictType.TypeConflict)]
    [InlineData(ConflictType.BothCreated)]
    public void ConflictType_AllTypes_SetsCorrectly(ConflictType type) {
        // Arrange & Act
        var analysis = new ConflictAnalysis {
            FilePath = "test.txt",
            ConflictType = type
        };

        // Assert
        Assert.Equal(type, analysis.ConflictType);
    }

    [Theory]
    [InlineData(ConflictResolution.Ask)]
    [InlineData(ConflictResolution.UseLocal)]
    [InlineData(ConflictResolution.UseRemote)]
    [InlineData(ConflictResolution.Skip)]
    [InlineData(ConflictResolution.RenameLocal)]
    [InlineData(ConflictResolution.RenameRemote)]
    public void RecommendedResolution_AllResolutions_SetsCorrectly(ConflictResolution resolution) {
        // Arrange & Act
        var analysis = new ConflictAnalysis {
            FilePath = "test.txt",
            ConflictType = ConflictType.BothModified,
            RecommendedResolution = resolution
        };

        // Assert
        Assert.Equal(resolution, analysis.RecommendedResolution);
    }

    [Fact]
    public void LocalItem_Null_IsAllowed() {
        // Arrange & Act
        var analysis = new ConflictAnalysis {
            FilePath = "test.txt",
            ConflictType = ConflictType.DeletedLocallyModifiedRemotely,
            LocalItem = null,
            RemoteItem = new SyncItem { Path = "test.txt" }
        };

        // Assert
        Assert.Null(analysis.LocalItem);
    }

    [Fact]
    public void RemoteItem_Null_IsAllowed() {
        // Arrange & Act
        var analysis = new ConflictAnalysis {
            FilePath = "test.txt",
            ConflictType = ConflictType.ModifiedLocallyDeletedRemotely,
            LocalItem = new SyncItem { Path = "test.txt" },
            RemoteItem = null
        };

        // Assert
        Assert.Null(analysis.RemoteItem);
    }

    [Fact]
    public void Record_WithWith_CreatesNewInstance() {
        // Arrange
        var original = new ConflictAnalysis {
            FilePath = "test.txt",
            ConflictType = ConflictType.BothModified,
            RecommendedResolution = ConflictResolution.UseLocal
        };

        // Act
        var modified = original with { RecommendedResolution = ConflictResolution.UseRemote };

        // Assert
        Assert.Equal(ConflictResolution.UseLocal, original.RecommendedResolution);
        Assert.Equal(ConflictResolution.UseRemote, modified.RecommendedResolution);
        Assert.Equal(original.FilePath, modified.FilePath);
        Assert.Equal(original.ConflictType, modified.ConflictType);
    }

    [Fact]
    public void Record_Equality_WorksCorrectly() {
        // Arrange
        var analysis1 = new ConflictAnalysis {
            FilePath = "test.txt",
            ConflictType = ConflictType.BothModified,
            RecommendedResolution = ConflictResolution.UseLocal
        };

        var analysis2 = new ConflictAnalysis {
            FilePath = "test.txt",
            ConflictType = ConflictType.BothModified,
            RecommendedResolution = ConflictResolution.UseLocal
        };

        // Act & Assert
        Assert.Equal(analysis1, analysis2);
    }

    [Fact]
    public void NewerVersion_Null_IsAllowed() {
        // Arrange & Act
        var analysis = new ConflictAnalysis {
            FilePath = "test.txt",
            ConflictType = ConflictType.BothModified,
            NewerVersion = null
        };

        // Assert
        Assert.Null(analysis.NewerVersion);
    }

    [Fact]
    public void NewerVersion_LocalOrRemote_SetsCorrectly() {
        // Arrange & Act
        var localNewer = new ConflictAnalysis {
            FilePath = "test.txt",
            ConflictType = ConflictType.BothModified,
            NewerVersion = "Local"
        };

        var remoteNewer = new ConflictAnalysis {
            FilePath = "test.txt",
            ConflictType = ConflictType.BothModified,
            NewerVersion = "Remote"
        };

        // Assert
        Assert.Equal("Local", localNewer.NewerVersion);
        Assert.Equal("Remote", remoteNewer.NewerVersion);
    }

    [Fact]
    public void IsLikelyBinary_AndIsLikelyTextFile_CanBothBeFalse() {
        // Arrange & Act
        var analysis = new ConflictAnalysis {
            FilePath = "test.unknown",
            ConflictType = ConflictType.BothModified,
            IsLikelyBinary = false,
            IsLikelyTextFile = false
        };

        // Assert
        Assert.False(analysis.IsLikelyBinary);
        Assert.False(analysis.IsLikelyTextFile);
    }

    [Fact]
    public void TimeDifference_Zero_IsAllowed() {
        // Arrange & Act
        var analysis = new ConflictAnalysis {
            FilePath = "test.txt",
            ConflictType = ConflictType.BothModified,
            TimeDifference = 0
        };

        // Assert
        Assert.Equal(0, analysis.TimeDifference);
    }

    [Fact]
    public void LocalModified_AndRemoteModified_Null_IsAllowed() {
        // Arrange & Act
        var analysis = new ConflictAnalysis {
            FilePath = "test.txt",
            ConflictType = ConflictType.BothCreated,
            LocalModified = null,
            RemoteModified = null
        };

        // Assert
        Assert.Null(analysis.LocalModified);
        Assert.Null(analysis.RemoteModified);
    }
}
