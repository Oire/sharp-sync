using Xunit;

namespace Oire.SharpSync.Tests;

public class SyncResultTests
{
    [Fact]
    public void Constructor_ShouldSetDefaultValues()
    {
        // Arrange & Act
        var result = new SyncResult();

        // Assert
        Assert.False(result.Success);
        Assert.Equal(0, result.FilesSynchronized);
        Assert.Equal(0, result.FilesSkipped);
        Assert.Equal(0, result.FilesConflicted);
        Assert.Equal(0, result.FilesDeleted);
        Assert.Equal(TimeSpan.Zero, result.ElapsedTime);
        Assert.Null(result.Error);
        Assert.Equal(string.Empty, result.Details);
        Assert.Equal(0, result.TotalFilesProcessed);
    }

    [Fact]
    public void TotalFilesProcessed_ShouldCalculateCorrectly()
    {
        // Arrange
        var result = new SyncResult
        {
            FilesSynchronized = 10,
            FilesSkipped = 5,
            FilesConflicted = 2
        };

        // Act & Assert
        Assert.Equal(17, result.TotalFilesProcessed);
    }

    [Fact]
    public void Properties_ShouldBeSettable()
    {
        // Arrange
        var result = new SyncResult();
        var error = new SyncException(SyncErrorCode.Generic, "Test error");
        var elapsed = TimeSpan.FromMinutes(5);

        // Act
        result.Success = true;
        result.FilesSynchronized = 100;
        result.FilesSkipped = 10;
        result.FilesConflicted = 2;
        result.FilesDeleted = 5;
        result.ElapsedTime = elapsed;
        result.Error = error;
        result.Details = "Test completed";

        // Assert
        Assert.True(result.Success);
        Assert.Equal(100, result.FilesSynchronized);
        Assert.Equal(10, result.FilesSkipped);
        Assert.Equal(2, result.FilesConflicted);
        Assert.Equal(5, result.FilesDeleted);
        Assert.Equal(elapsed, result.ElapsedTime);
        Assert.Same(error, result.Error);
        Assert.Equal("Test completed", result.Details);
        Assert.Equal(112, result.TotalFilesProcessed);
    }
}