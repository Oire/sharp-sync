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
        Assert.Equal(0, progress.ProcessedBytes);
        Assert.Equal(0, progress.TotalBytes);
        Assert.Equal(0.0, progress.Percentage);
        Assert.Null(progress.CurrentItem);
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
    public void Properties_CanBeSetAndRetrieved()
    {
        // Arrange
        var progress = new SyncProgress();
        
        // Act
        progress.ProcessedItems = 42;
        progress.TotalItems = 100;
        progress.ProcessedBytes = 1024;
        progress.TotalBytes = 2048;
        progress.CurrentItem = "test.txt";

        // Assert
        Assert.Equal(42, progress.ProcessedItems);
        Assert.Equal(100, progress.TotalItems);
        Assert.Equal(1024, progress.ProcessedBytes);
        Assert.Equal(2048, progress.TotalBytes);
        Assert.Equal("test.txt", progress.CurrentItem);
        Assert.Equal(42.0, progress.Percentage, 1);
    }

    [Fact]
    public void BytesPercentage_ShouldCalculateCorrectly()
    {
        // Arrange
        var progress = new SyncProgress
        {
            ProcessedBytes = 512,
            TotalBytes = 1024
        };

        // Act
        var percentage = (double)progress.ProcessedBytes / progress.TotalBytes * 100;

        // Assert
        Assert.Equal(50.0, percentage, 1);
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