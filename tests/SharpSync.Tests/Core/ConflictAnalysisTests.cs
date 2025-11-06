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
        var reasoning = "Remote file is newer";
        var localModified = DateTime.UtcNow.AddHours(-1);
        var remoteModified = DateTime.UtcNow;

        // Act
        var analysis = new ConflictAnalysis {
            FilePath = filePath,
            ConflictType = conflictType,
            LocalItem = localItem,
            RemoteItem = remoteItem,
            RecommendedResolution = ConflictResolution.UseRemote,
            Reasoning = reasoning,
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
        Assert.Equal(reasoning, analysis.Reasoning);
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

    [Fact]
    public void FileExtension_WithExtension_ReturnsCorrectValue() {
        // Arrange
        var analysis = new ConflictAnalysis {
            FilePath = "documents/test.txt",
            ConflictType = ConflictType.BothModified
        };

        // Act
        var extension = analysis.FileExtension;

        // Assert
        Assert.Equal(".txt", extension);
    }

    [Fact]
    public void FileExtension_WithoutExtension_ReturnsEmpty() {
        // Arrange
        var analysis = new ConflictAnalysis {
            FilePath = "documents/testfile",
            ConflictType = ConflictType.BothModified
        };

        // Act
        var extension = analysis.FileExtension;

        // Assert
        Assert.Equal("", extension);
    }

    [Fact]
    public void FileName_WithPath_ReturnsFileNameOnly() {
        // Arrange
        var analysis = new ConflictAnalysis {
            FilePath = "documents/subfolder/test.txt",
            ConflictType = ConflictType.BothModified
        };

        // Act
        var fileName = analysis.FileName;

        // Assert
        Assert.Equal("test.txt", fileName);
    }

    [Fact]
    public void FileName_WithoutPath_ReturnsFileName() {
        // Arrange
        var analysis = new ConflictAnalysis {
            FilePath = "test.txt",
            ConflictType = ConflictType.BothModified
        };

        // Act
        var fileName = analysis.FileName;

        // Assert
        Assert.Equal("test.txt", fileName);
    }

    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(512, "512.0 B")]
    [InlineData(1024, "1.0 KB")]
    [InlineData(1536, "1.5 KB")]
    [InlineData(1048576, "1.0 MB")]
    [InlineData(1572864, "1.5 MB")]
    [InlineData(1073741824, "1.0 GB")]
    [InlineData(1610612736, "1.5 GB")]
    [InlineData(1099511627776, "1.0 TB")]
    public void FormattedSizeDifference_VariousSizes_FormatsCorrectly(long bytes, string expected) {
        // Arrange
        var analysis = new ConflictAnalysis {
            FilePath = "test.txt",
            ConflictType = ConflictType.BothModified,
            SizeDifference = bytes
        };

        // Act
        var formatted = analysis.FormattedSizeDifference;

        // Assert
        Assert.Equal(expected, formatted);
    }

    [Fact]
    public void FormattedSizeDifference_VeryLargeSize_FormatsAsTerabytes() {
        // Arrange
        var analysis = new ConflictAnalysis {
            FilePath = "huge.bin",
            ConflictType = ConflictType.BothModified,
            SizeDifference = 1024L * 1024L * 1024L * 1024L * 5L // 5 TB
        };

        // Act
        var formatted = analysis.FormattedSizeDifference;

        // Assert
        Assert.Equal("5.0 TB", formatted);
    }

    [Fact]
    public void Reasoning_DefaultValue_IsEmpty() {
        // Arrange & Act
        var analysis = new ConflictAnalysis {
            FilePath = "test.txt",
            ConflictType = ConflictType.BothModified
        };

        // Assert
        Assert.Equal(string.Empty, analysis.Reasoning);
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
            RecommendedResolution = ConflictResolution.UseLocal,
            Reasoning = "Test"
        };

        var analysis2 = new ConflictAnalysis {
            FilePath = "test.txt",
            ConflictType = ConflictType.BothModified,
            RecommendedResolution = ConflictResolution.UseLocal,
            Reasoning = "Test"
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
    public void SizeDifference_ZeroWhenSizesEqual_FormatsAsZero() {
        // Arrange
        var analysis = new ConflictAnalysis {
            FilePath = "test.txt",
            ConflictType = ConflictType.BothModified,
            LocalSize = 1024,
            RemoteSize = 1024,
            SizeDifference = 0
        };

        // Act
        var formatted = analysis.FormattedSizeDifference;

        // Assert
        Assert.Equal("0 B", formatted);
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
