using Xunit;

namespace SharpSync.Tests;

public class SyncProgressTests
{
    [Fact]
    public void Constructor_ShouldSetDefaultValues()
    {
        // Arrange & Act
        var progress = new SyncProgress();

        // Assert
        Assert.Equal(0, progress.CurrentFile);
        Assert.Equal(0, progress.TotalFiles);
        Assert.Equal(string.Empty, progress.CurrentFileName);
        Assert.False(progress.IsCancelled);
        Assert.Equal(0.0, progress.Percentage);
    }

    [Theory]
    [InlineData(0, 100, 0.0)]
    [InlineData(25, 100, 25.0)]
    [InlineData(50, 100, 50.0)]
    [InlineData(75, 100, 75.0)]
    [InlineData(100, 100, 100.0)]
    [InlineData(10, 0, 0.0)] // Division by zero case
    public void Percentage_ShouldCalculateCorrectly(long current, long total, double expected)
    {
        // Arrange
        var progress = new SyncProgress
        {
            CurrentFile = current,
            TotalFiles = total
        };

        // Act & Assert
        Assert.Equal(expected, progress.Percentage, 1);
    }

    [Fact]
    public void Clone_ShouldCreateExactCopy()
    {
        // Arrange
        var original = new SyncProgress
        {
            CurrentFile = 42,
            TotalFiles = 100,
            CurrentFileName = "test.txt",
            IsCancelled = true
        };

        // Act
        var clone = original.Clone();

        // Assert
        Assert.NotSame(original, clone);
        Assert.Equal(original.CurrentFile, clone.CurrentFile);
        Assert.Equal(original.TotalFiles, clone.TotalFiles);
        Assert.Equal(original.CurrentFileName, clone.CurrentFileName);
        Assert.Equal(original.IsCancelled, clone.IsCancelled);
        Assert.Equal(original.Percentage, clone.Percentage);
    }

    [Fact]
    public void ModifyingClone_ShouldNotAffectOriginal()
    {
        // Arrange
        var original = new SyncProgress
        {
            CurrentFile = 10,
            TotalFiles = 50,
            CurrentFileName = "original.txt",
            IsCancelled = false
        };
        var clone = original.Clone();

        // Act
        clone.CurrentFile = 20;
        clone.TotalFiles = 100;
        clone.CurrentFileName = "modified.txt";
        clone.IsCancelled = true;

        // Assert
        Assert.Equal(10, original.CurrentFile);
        Assert.Equal(50, original.TotalFiles);
        Assert.Equal("original.txt", original.CurrentFileName);
        Assert.False(original.IsCancelled);
    }
}