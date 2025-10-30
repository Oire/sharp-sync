namespace Oire.SharpSync.Tests.Core;

public class SyncProgressTests
{
    [Fact]
    public void Constructor_ShouldSetDefaultValues()
    {
        // Arrange & Act
        var progress = new SyncProgress();

        // Assert
        Assert.Equal(0, progress.ProcessedItems);
        Assert.Equal(0, progress.TotalItems);
        Assert.Null(progress.CurrentItem);
        Assert.Equal(0.0, progress.Percentage);
        Assert.False(progress.IsCancelled);
    }

    [Theory]
    [InlineData(0, 100, 0.0)]
    [InlineData(25, 100, 25.0)]
    [InlineData(50, 100, 50.0)]
    [InlineData(75, 100, 75.0)]
    [InlineData(100, 100, 100.0)]
    [InlineData(10, 0, 0.0)] // Division by zero case
    public void Percentage_ShouldCalculateCorrectly(int current, int total, double expected)
    {
        // Arrange
        var progress = new SyncProgress
        {
            ProcessedItems = current,
            TotalItems = total
        };

        // Act & Assert
        Assert.Equal(expected, progress.Percentage, 1);
    }

    [Fact]
    public void Properties_ShouldWorkWithInitSyntax()
    {
        // Arrange & Act
        var progress = new SyncProgress
        {
            ProcessedItems = 42,
            TotalItems = 100,
            CurrentItem = "test.txt",
            IsCancelled = false
        };

        // Assert
        Assert.Equal(42, progress.ProcessedItems);
        Assert.Equal(100, progress.TotalItems);
        Assert.Equal("test.txt", progress.CurrentItem);
        Assert.Equal(42.0, progress.Percentage, 1);
        // Test backward compatibility properties
        Assert.Equal(42, progress.CurrentFile);
        Assert.Equal(100, progress.TotalFiles);
        Assert.Equal("test.txt", progress.CurrentFileName);
    }

    [Fact]
    public void BackwardCompatibilityProperties_ShouldWork()
    {
        // Arrange
        var progress = new SyncProgress
        {
            ProcessedItems = 512,
            TotalItems = 1024,
            CurrentItem = "myfile.txt"
        };

        // Act & Assert
        Assert.Equal(512, progress.CurrentFile);
        Assert.Equal(1024, progress.TotalFiles);
        Assert.Equal("myfile.txt", progress.CurrentFileName);
    }

    [Fact]
    public void Percentage_WithZeroTotal_ReturnsZero()
    {
        // Arrange
        var progress = new SyncProgress
        {
            ProcessedItems = 10,
            TotalItems = 0
        };

        // Act & Assert
        Assert.Equal(0.0, progress.Percentage);
    }
}