namespace Oire.SharpSync.Tests.Core;

public class SyncResultTests {
    [Fact]
    public void Constructor_ShouldSetDefaultValues() {
        // Arrange & Act
        var result = new SyncResult();

        // Assert
        Assert.False(result.Success);
        Assert.Equal(0, result.FilesSynchronized);
        Assert.Equal(0, result.FilesSkipped);
        Assert.Equal(0, result.FilesConflicted);
        Assert.Equal(0, result.FilesDeleted);
        Assert.Equal(0, result.TotalFilesProcessed);
        Assert.Equal(TimeSpan.Zero, result.ElapsedTime);
        Assert.Null(result.Error);
        Assert.Equal(string.Empty, result.Details);
    }

    [Fact]
    public void Properties_CanBeSetAndRetrieved() {
        // Arrange
        var result = new SyncResult();
        var elapsedTime = TimeSpan.FromMinutes(5);
        var error = new InvalidOperationException("Test error");

        // Act
        result.Success = true;
        result.FilesSynchronized = 50;
        result.FilesSkipped = 10;
        result.FilesConflicted = 5;
        result.FilesDeleted = 3;
        result.ElapsedTime = elapsedTime;
        result.Error = error;
        result.Details = "Test details";

        // Assert
        Assert.True(result.Success);
        Assert.Equal(50, result.FilesSynchronized);
        Assert.Equal(10, result.FilesSkipped);
        Assert.Equal(5, result.FilesConflicted);
        Assert.Equal(3, result.FilesDeleted);
        Assert.Equal(65, result.TotalFilesProcessed); // 50 + 10 + 5
        Assert.Equal(elapsedTime, result.ElapsedTime);
        Assert.Same(error, result.Error);
        Assert.Equal("Test details", result.Details);
    }

    [Fact]
    public void Success_WithError_CanStillBeTrue() {
        // Arrange
        var result = new SyncResult {
            Success = true,
            Error = new Exception("Non-fatal warning")
        };

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void TotalFilesProcessed_CalculatesCorrectly() {
        // Arrange
        var result = new SyncResult {
            FilesSynchronized = 100,
            FilesSkipped = 20,
            FilesConflicted = 5
        };

        // Act & Assert
        Assert.Equal(125, result.TotalFilesProcessed);
    }

    [Fact]
    public void ElapsedTime_CanBeSet() {
        // Arrange
        var result = new SyncResult();
        var duration = TimeSpan.FromSeconds(30);

        // Act
        result.ElapsedTime = duration;

        // Assert
        Assert.Equal(duration, result.ElapsedTime);
    }

    [Fact]
    public void Details_DefaultsToEmptyString() {
        // Arrange & Act
        var result = new SyncResult();

        // Assert
        Assert.NotNull(result.Details);
        Assert.Equal(string.Empty, result.Details);
    }
}
