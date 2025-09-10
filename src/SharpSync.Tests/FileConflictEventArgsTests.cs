using Xunit;

namespace Oire.SharpSync.Tests;

public class FileConflictEventArgsTests
{
    [Fact]
    public void Constructor_ShouldSetPropertiesCorrectly()
    {
        // Arrange
        var sourcePath = "/source/file.txt";
        var targetPath = "/target/file.txt";
        var conflictType = ConflictType.BothModified;

        // Act
        var args = new FileConflictEventArgs(sourcePath, targetPath, conflictType);

        // Assert
        Assert.Equal(sourcePath, args.SourcePath);
        Assert.Equal(targetPath, args.TargetPath);
        Assert.Equal(conflictType, args.ConflictType);
        Assert.Equal(ConflictResolution.Ask, args.Resolution);
    }

    [Fact]
    public void Resolution_ShouldBeSettable()
    {
        // Arrange
        var args = new FileConflictEventArgs("/source/file.txt", "/target/file.txt", ConflictType.BothModified);

        // Act
        args.Resolution = ConflictResolution.UseSource;

        // Assert
        Assert.Equal(ConflictResolution.UseSource, args.Resolution);
    }

    [Theory]
    [InlineData(ConflictType.BothModified)]
    [InlineData(ConflictType.DeletedInSourceModifiedInTarget)]
    [InlineData(ConflictType.ModifiedInSourceDeletedInTarget)]
    [InlineData(ConflictType.TypeConflict)]
    public void ConflictType_ShouldAcceptAllValidValues(ConflictType conflictType)
    {
        // Arrange & Act
        var args = new FileConflictEventArgs("/source/file.txt", "/target/file.txt", conflictType);

        // Assert
        Assert.Equal(conflictType, args.ConflictType);
    }

    [Theory]
    [InlineData(ConflictResolution.Ask)]
    [InlineData(ConflictResolution.UseSource)]
    [InlineData(ConflictResolution.UseTarget)]
    [InlineData(ConflictResolution.Skip)]
    [InlineData(ConflictResolution.Merge)]
    public void Resolution_ShouldAcceptAllValidValues(ConflictResolution resolution)
    {
        // Arrange
        var args = new FileConflictEventArgs("/source/file.txt", "/target/file.txt", ConflictType.BothModified);

        // Act
        args.Resolution = resolution;

        // Assert
        Assert.Equal(resolution, args.Resolution);
    }
}