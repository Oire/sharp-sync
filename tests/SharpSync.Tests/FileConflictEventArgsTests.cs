namespace Oire.SharpSync.Tests.Core;

public class FileConflictEventArgsTests {
    [Fact]
    public void Constructor_ShouldSetPropertiesCorrectly() {
        // Arrange
        var localItem = new SyncItem { Path = "file.txt", Size = 1024 };
        var remoteItem = new SyncItem { Path = "file.txt", Size = 2048 };
        var conflictType = ConflictType.BothModified;

        // Act
        var args = new FileConflictEventArgs("file.txt", localItem, remoteItem, conflictType);

        // Assert
        Assert.Equal("file.txt", args.Path);
        Assert.Equal(localItem, args.LocalItem);
        Assert.Equal(remoteItem, args.RemoteItem);
        Assert.Equal(conflictType, args.ConflictType);
        Assert.Equal(ConflictResolution.Ask, args.Resolution);
    }

    [Fact]
    public void Resolution_ShouldBeSettable() {
        // Arrange
        var localItem = new SyncItem { Path = "file.txt" };
        var remoteItem = new SyncItem { Path = "file.txt" };
        var args = new FileConflictEventArgs("file.txt", localItem, remoteItem, ConflictType.BothModified);

        // Act
        args.Resolution = ConflictResolution.UseLocal;

        // Assert
        Assert.Equal(ConflictResolution.UseLocal, args.Resolution);
    }

    [Theory]
    [InlineData(ConflictType.BothModified)]
    [InlineData(ConflictType.DeletedLocallyModifiedRemotely)]
    [InlineData(ConflictType.ModifiedLocallyDeletedRemotely)]
    [InlineData(ConflictType.TypeConflict)]
    public void ConflictType_ShouldAcceptAllValidValues(ConflictType conflictType) {
        // Arrange
        var localItem = new SyncItem { Path = "file.txt" };
        var remoteItem = new SyncItem { Path = "file.txt" };

        // Act
        var args = new FileConflictEventArgs("file.txt", localItem, remoteItem, conflictType);

        // Assert
        Assert.Equal(conflictType, args.ConflictType);
    }

    [Theory]
    [InlineData(ConflictResolution.Ask)]
    [InlineData(ConflictResolution.UseLocal)]
    [InlineData(ConflictResolution.UseRemote)]
    [InlineData(ConflictResolution.Skip)]
    [InlineData(ConflictResolution.RenameLocal)]
    public void Resolution_ShouldAcceptAllValidValues(ConflictResolution resolution) {
        // Arrange
        var localItem = new SyncItem { Path = "file.txt" };
        var remoteItem = new SyncItem { Path = "file.txt" };
        var args = new FileConflictEventArgs("file.txt", localItem, remoteItem, ConflictType.BothModified);

        // Act
        args.Resolution = resolution;

        // Assert
        Assert.Equal(resolution, args.Resolution);
    }
}
