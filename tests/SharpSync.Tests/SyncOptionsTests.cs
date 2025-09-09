using Xunit;

namespace SharpSync.Tests;

public class SyncOptionsTests
{
    [Fact]
    public void Constructor_ShouldSetDefaultValues()
    {
        // Arrange & Act
        var options = new SyncOptions();

        // Assert
        Assert.True(options.PreservePermissions);
        Assert.True(options.PreserveTimestamps);
        Assert.False(options.FollowSymlinks);
        Assert.False(options.DryRun);
        Assert.False(options.Verbose);
        Assert.False(options.ChecksumOnly);
        Assert.False(options.SizeOnly);
        Assert.False(options.DeleteExtraneous);
        Assert.True(options.UpdateExisting);
        Assert.Equal(ConflictResolution.Ask, options.ConflictResolution);
    }

    [Fact]
    public void Clone_ShouldCreateExactCopy()
    {
        // Arrange
        var original = new SyncOptions
        {
            PreservePermissions = false,
            PreserveTimestamps = false,
            FollowSymlinks = true,
            DryRun = true,
            Verbose = true,
            ChecksumOnly = true,
            SizeOnly = false,
            DeleteExtraneous = true,
            UpdateExisting = false,
            ConflictResolution = ConflictResolution.UseSource
        };

        // Act
        var clone = original.Clone();

        // Assert
        Assert.NotSame(original, clone);
        Assert.Equal(original.PreservePermissions, clone.PreservePermissions);
        Assert.Equal(original.PreserveTimestamps, clone.PreserveTimestamps);
        Assert.Equal(original.FollowSymlinks, clone.FollowSymlinks);
        Assert.Equal(original.DryRun, clone.DryRun);
        Assert.Equal(original.Verbose, clone.Verbose);
        Assert.Equal(original.ChecksumOnly, clone.ChecksumOnly);
        Assert.Equal(original.SizeOnly, clone.SizeOnly);
        Assert.Equal(original.DeleteExtraneous, clone.DeleteExtraneous);
        Assert.Equal(original.UpdateExisting, clone.UpdateExisting);
        Assert.Equal(original.ConflictResolution, clone.ConflictResolution);
    }

    [Fact]
    public void ModifyingClone_ShouldNotAffectOriginal()
    {
        // Arrange
        var original = new SyncOptions();
        var clone = original.Clone();

        // Act
        clone.DryRun = true;
        clone.Verbose = true;
        clone.ConflictResolution = ConflictResolution.UseSource;

        // Assert
        Assert.False(original.DryRun);
        Assert.False(original.Verbose);
        Assert.Equal(ConflictResolution.Ask, original.ConflictResolution);
    }
}