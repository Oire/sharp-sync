namespace Oire.SharpSync.Tests.Core;

public class SyncOptionsTests {
    [Fact]
    public void Constructor_ShouldSetDefaultValues() {
        // Arrange & Act
        var options = new SyncOptions();

        // Assert
        Assert.True(options.PreservePermissions);
        Assert.True(options.PreserveTimestamps);
        Assert.False(options.FollowSymlinks);
        Assert.False(options.Verbose);
        Assert.False(options.ChecksumOnly);
        Assert.False(options.SizeOnly);
        Assert.False(options.DeleteExtraneous);
        Assert.True(options.UpdateExisting);
        Assert.Equal(ConflictResolution.Ask, options.ConflictResolution);
        Assert.Equal(0, options.TimeoutSeconds);
        Assert.NotNull(options.ExcludePatterns);
        Assert.Empty(options.ExcludePatterns);
        Assert.Null(options.MaxBytesPerSecond);
    }

    [Fact]
    public void Properties_CanBeSetAndRetrieved() {
        // Arrange
        var options = new SyncOptions();
        var excludePatterns = new List<string> { "*.tmp", "*.log" };

        // Act
        options.PreservePermissions = false;
        options.PreserveTimestamps = false;
        options.FollowSymlinks = true;
        options.Verbose = true;
        options.ChecksumOnly = true;
        options.SizeOnly = false;
        options.DeleteExtraneous = true;
        options.UpdateExisting = false;
        options.ConflictResolution = ConflictResolution.UseLocal;
        options.TimeoutSeconds = 300;
        foreach (var pattern in excludePatterns) {
            options.ExcludePatterns.Add(pattern);
        }

        // Assert
        Assert.False(options.PreservePermissions);
        Assert.False(options.PreserveTimestamps);
        Assert.True(options.FollowSymlinks);
        Assert.True(options.Verbose);
        Assert.True(options.ChecksumOnly);
        Assert.False(options.SizeOnly);
        Assert.True(options.DeleteExtraneous);
        Assert.False(options.UpdateExisting);
        Assert.Equal(ConflictResolution.UseLocal, options.ConflictResolution);
        Assert.Equal(300, options.TimeoutSeconds);
        Assert.Equal(2, options.ExcludePatterns.Count);
        Assert.Contains("*.tmp", options.ExcludePatterns);
        Assert.Contains("*.log", options.ExcludePatterns);
    }

    [Theory]
    [InlineData(ConflictResolution.Ask)]
    [InlineData(ConflictResolution.UseLocal)]
    [InlineData(ConflictResolution.UseRemote)]
    [InlineData(ConflictResolution.Skip)]
    [InlineData(ConflictResolution.RenameLocal)]
    [InlineData(ConflictResolution.RenameRemote)]
    public void ConflictResolution_AllValuesSupported(ConflictResolution resolution) {
        // Arrange & Act
        var options = new SyncOptions { ConflictResolution = resolution };

        // Assert
        Assert.Equal(resolution, options.ConflictResolution);
    }

    [Fact]
    public void Clone_CreatesExactCopy() {
        // Arrange
        var original = new SyncOptions {
            PreservePermissions = false,
            ConflictResolution = ConflictResolution.UseLocal,
            TimeoutSeconds = 120
        };
        original.ExcludePatterns.Add("*.tmp");

        // Act
        var clone = original.Clone();

        // Assert
        Assert.NotSame(original, clone);
        Assert.Equal(original.PreservePermissions, clone.PreservePermissions);
        Assert.Equal(original.ConflictResolution, clone.ConflictResolution);
        Assert.Equal(original.TimeoutSeconds, clone.TimeoutSeconds);
        Assert.Equal(original.ExcludePatterns.Count, clone.ExcludePatterns.Count);
        Assert.Contains("*.tmp", clone.ExcludePatterns);
    }

    [Fact]
    public void TimeoutSeconds_CanBeSet() {
        // Arrange
        var options = new SyncOptions();

        // Act
        options.TimeoutSeconds = 600;

        // Assert
        Assert.Equal(600, options.TimeoutSeconds);
    }

    [Fact]
    public void MaxBytesPerSecond_CanBeSet() {
        // Arrange
        var options = new SyncOptions();

        // Act
        options.MaxBytesPerSecond = 1_048_576; // 1 MB/s

        // Assert
        Assert.Equal(1_048_576, options.MaxBytesPerSecond);
    }

    [Fact]
    public void MaxBytesPerSecond_CanBeSetToNull() {
        // Arrange
        var options = new SyncOptions { MaxBytesPerSecond = 1000 };

        // Act
        options.MaxBytesPerSecond = null;

        // Assert
        Assert.Null(options.MaxBytesPerSecond);
    }

    [Theory]
    [InlineData(1_048_576)]      // 1 MB/s
    [InlineData(10_485_760)]     // 10 MB/s
    [InlineData(104_857_600)]    // 100 MB/s
    [InlineData(1)]              // 1 byte/s (minimum)
    [InlineData(long.MaxValue)]  // Maximum possible
    public void MaxBytesPerSecond_AcceptsVariousValues(long bytesPerSecond) {
        // Arrange & Act
        var options = new SyncOptions { MaxBytesPerSecond = bytesPerSecond };

        // Assert
        Assert.Equal(bytesPerSecond, options.MaxBytesPerSecond);
    }

    [Fact]
    public void Clone_CopiesMaxBytesPerSecond() {
        // Arrange
        var original = new SyncOptions {
            MaxBytesPerSecond = 5_242_880 // 5 MB/s
        };

        // Act
        var clone = original.Clone();

        // Assert
        Assert.NotSame(original, clone);
        Assert.Equal(original.MaxBytesPerSecond, clone.MaxBytesPerSecond);
    }

    [Fact]
    public void Clone_CopiesNullMaxBytesPerSecond() {
        // Arrange
        var original = new SyncOptions {
            MaxBytesPerSecond = null
        };

        // Act
        var clone = original.Clone();

        // Assert
        Assert.NotSame(original, clone);
        Assert.Null(clone.MaxBytesPerSecond);
    }
}
