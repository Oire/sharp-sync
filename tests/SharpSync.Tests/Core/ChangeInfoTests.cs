namespace Oire.SharpSync.Tests.Core;

/// <summary>
/// Tests for the ChangeInfo record type.
/// </summary>
public class ChangeInfoTests {
    [Fact]
    public void Constructor_RequiredParameters_SetsProperties() {
        // Act
        var change = new ChangeInfo("docs/readme.md", ChangeType.Created);

        // Assert
        Assert.Equal("docs/readme.md", change.Path);
        Assert.Equal(ChangeType.Created, change.ChangeType);
        Assert.Equal(0, change.Size);
        Assert.False(change.IsDirectory);
        Assert.Null(change.RenamedFrom);
        Assert.Null(change.RenamedTo);
    }

    [Fact]
    public void Constructor_AllParameters_SetsProperties() {
        // Act
        var change = new ChangeInfo(
            Path: "folder/file.txt",
            ChangeType: ChangeType.Changed,
            Size: 1024,
            IsDirectory: false,
            RenamedFrom: "folder/old.txt",
            RenamedTo: "folder/file.txt");

        // Assert
        Assert.Equal("folder/file.txt", change.Path);
        Assert.Equal(ChangeType.Changed, change.ChangeType);
        Assert.Equal(1024, change.Size);
        Assert.False(change.IsDirectory);
        Assert.Equal("folder/old.txt", change.RenamedFrom);
        Assert.Equal("folder/file.txt", change.RenamedTo);
    }

    [Fact]
    public void DetectedAt_DefaultsToUtcNow() {
        // Arrange
        var before = DateTime.UtcNow;

        // Act
        var change = new ChangeInfo("file.txt", ChangeType.Created);

        // Assert
        var after = DateTime.UtcNow;
        Assert.InRange(change.DetectedAt, before, after);
    }

    [Fact]
    public void DetectedAt_CanBeSetViaInit() {
        // Arrange
        var timestamp = new DateTime(2025, 6, 15, 12, 0, 0, DateTimeKind.Utc);

        // Act
        var change = new ChangeInfo("file.txt", ChangeType.Changed) { DetectedAt = timestamp };

        // Assert
        Assert.Equal(timestamp, change.DetectedAt);
    }

    [Fact]
    public void IsDirectory_CanBeSetToTrue() {
        // Act
        var change = new ChangeInfo("photos/", ChangeType.Created, IsDirectory: true);

        // Assert
        Assert.True(change.IsDirectory);
    }

    [Theory]
    [InlineData(ChangeType.Created)]
    [InlineData(ChangeType.Changed)]
    [InlineData(ChangeType.Deleted)]
    [InlineData(ChangeType.Renamed)]
    public void Constructor_AllChangeTypes_AreAccepted(ChangeType changeType) {
        // Act
        var change = new ChangeInfo("file.txt", changeType);

        // Assert
        Assert.Equal(changeType, change.ChangeType);
    }

    [Fact]
    public void RecordEquality_SameValues_AreEqual() {
        // Arrange
        var timestamp = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var a = new ChangeInfo("file.txt", ChangeType.Created, Size: 100) { DetectedAt = timestamp };
        var b = new ChangeInfo("file.txt", ChangeType.Created, Size: 100) { DetectedAt = timestamp };

        // Assert
        Assert.Equal(a, b);
    }

    [Fact]
    public void RecordEquality_DifferentPath_AreNotEqual() {
        // Arrange
        var timestamp = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var a = new ChangeInfo("file1.txt", ChangeType.Created) { DetectedAt = timestamp };
        var b = new ChangeInfo("file2.txt", ChangeType.Created) { DetectedAt = timestamp };

        // Assert
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void With_CreatesModifiedCopy() {
        // Arrange
        var original = new ChangeInfo("file.txt", ChangeType.Created, Size: 100);

        // Act
        var modified = original with { ChangeType = ChangeType.Deleted };

        // Assert
        Assert.Equal("file.txt", modified.Path);
        Assert.Equal(ChangeType.Deleted, modified.ChangeType);
        Assert.Equal(100, modified.Size);
    }
}
