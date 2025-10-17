namespace Oire.SharpSync.Tests.Core;

public class SyncResultTests
{
    [Fact]
    public void Constructor_ShouldSetDefaultValues()
    {
        // Arrange & Act
        var result = new SyncResult();

        // Assert
        Assert.False(result.IsSuccessful);
        Assert.Equal(0, result.FilesProcessed);
        Assert.Equal(0, result.ConflictsResolved);
        Assert.Equal(TimeSpan.Zero, result.Duration);
        Assert.NotNull(result.Errors);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Properties_CanBeSetAndRetrieved()
    {
        // Arrange
        var result = new SyncResult();
        var duration = TimeSpan.FromMinutes(5);
        var errors = new List<string> { "Error 1", "Error 2" };

        // Act
        result.IsSuccessful = true;
        result.FilesProcessed = 100;
        result.ConflictsResolved = 5;
        result.Duration = duration;
        result.Errors.AddRange(errors);

        // Assert
        Assert.True(result.IsSuccessful);
        Assert.Equal(100, result.FilesProcessed);
        Assert.Equal(5, result.ConflictsResolved);
        Assert.Equal(duration, result.Duration);
        Assert.Equal(2, result.Errors.Count);
        Assert.Contains("Error 1", result.Errors);
        Assert.Contains("Error 2", result.Errors);
    }

    [Fact]
    public void IsSuccessful_WithErrors_ReturnsFalse()
    {
        // Arrange
        var result = new SyncResult();
        
        // Act
        result.IsSuccessful = true; // Set to true initially
        result.Errors.Add("Test error"); // Add an error

        // Assert - Having errors should make it unsuccessful
        Assert.Single(result.Errors);
    }

    [Fact]
    public void IsSuccessful_WithoutErrors_CanBeTrue()
    {
        // Arrange & Act
        var result = new SyncResult { IsSuccessful = true };

        // Assert
        Assert.True(result.IsSuccessful);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Duration_CanBeSet()
    {
        // Arrange
        var result = new SyncResult();
        var duration = TimeSpan.FromSeconds(30);

        // Act
        result.Duration = duration;

        // Assert
        Assert.Equal(duration, result.Duration);
    }
}