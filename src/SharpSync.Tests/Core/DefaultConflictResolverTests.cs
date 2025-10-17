namespace Oire.SharpSync.Tests.Core;

public class DefaultConflictResolverTests
{
    [Fact]
    public void Constructor_DefaultResolution_SetsCorrectly()
    {
        // Arrange & Act
        var resolver = new DefaultConflictResolver(ConflictResolution.UseLocal);

        // Assert
        Assert.Equal(ConflictResolution.UseLocal, resolver.DefaultResolution);
    }

    [Theory]
    [InlineData(ConflictResolution.Ask)]
    [InlineData(ConflictResolution.UseLocal)]
    [InlineData(ConflictResolution.UseRemote)]
    [InlineData(ConflictResolution.Skip)]
    [InlineData(ConflictResolution.RenameLocal)]
    public void Constructor_AllResolutions_Supported(ConflictResolution resolution)
    {
        // Arrange & Act
        var resolver = new DefaultConflictResolver(resolution);

        // Assert
        Assert.Equal(resolution, resolver.DefaultResolution);
    }

    [Fact]
    public async Task ResolveConflictAsync_BothModified_ReturnsDefaultResolution()
    {
        // Arrange
        var resolver = new DefaultConflictResolver(ConflictResolution.UseLocal);
        var localItem = new SyncItem 
        { 
            Path = "test.txt", 
            Size = 1024,
            LastModified = DateTime.UtcNow.AddMinutes(-1)
        };
        var remoteItem = new SyncItem 
        { 
            Path = "test.txt", 
            Size = 2048,
            LastModified = DateTime.UtcNow
        };

        var conflict = new FileConflictEventArgs("test.txt", localItem, remoteItem, ConflictType.BothModified);

        // Act
        var result = await resolver.ResolveConflictAsync(conflict);

        // Assert
        Assert.Equal(ConflictResolution.UseLocal, result);
    }

    [Theory]
    [InlineData(ConflictType.BothModified)]
    [InlineData(ConflictType.DeletedLocallyModifiedRemotely)]
    [InlineData(ConflictType.ModifiedLocallyDeletedRemotely)]
    [InlineData(ConflictType.TypeConflict)]
    public async Task ResolveConflictAsync_AllConflictTypes_ReturnsDefaultResolution(ConflictType conflictType)
    {
        // Arrange
        var resolution = ConflictResolution.UseRemote;
        var resolver = new DefaultConflictResolver(resolution);
        var localItem = new SyncItem { Path = "test.txt" };
        var remoteItem = new SyncItem { Path = "test.txt" };

        var conflict = new FileConflictEventArgs("test.txt", localItem, remoteItem, conflictType);

        // Act
        var result = await resolver.ResolveConflictAsync(conflict);

        // Assert
        Assert.Equal(resolution, result);
    }

    [Fact]
    public async Task ResolveConflictAsync_NullLocalItem_ReturnsDefaultResolution()
    {
        // Arrange
        var resolver = new DefaultConflictResolver(ConflictResolution.Skip);
        var remoteItem = new SyncItem { Path = "test.txt" };

        var conflict = new FileConflictEventArgs("test.txt", null, remoteItem, ConflictType.DeletedLocallyModifiedRemotely);

        // Act
        var result = await resolver.ResolveConflictAsync(conflict);

        // Assert
        Assert.Equal(ConflictResolution.Skip, result);
    }

    [Fact]
    public async Task ResolveConflictAsync_NullRemoteItem_ReturnsDefaultResolution()
    {
        // Arrange
        var resolver = new DefaultConflictResolver(ConflictResolution.RenameLocal);
        var localItem = new SyncItem { Path = "test.txt" };

        var conflict = new FileConflictEventArgs("test.txt", localItem, null, ConflictType.ModifiedLocallyDeletedRemotely);

        // Act
        var result = await resolver.ResolveConflictAsync(conflict);

        // Assert
        Assert.Equal(ConflictResolution.RenameLocal, result);
    }

    [Fact]
    public async Task ResolveConflictAsync_EmptyFilePath_ReturnsDefaultResolution()
    {
        // Arrange
        var resolver = new DefaultConflictResolver(ConflictResolution.UseLocal);
        var localItem = new SyncItem { Path = "" };
        var remoteItem = new SyncItem { Path = "" };

        var conflict = new FileConflictEventArgs("", localItem, remoteItem, ConflictType.BothModified);

        // Act
        var result = await resolver.ResolveConflictAsync(conflict);

        // Assert
        Assert.Equal(ConflictResolution.UseLocal, result);
    }

    [Fact]
    public async Task ResolveConflictAsync_TypeMismatch_ReturnsDefaultResolution()
    {
        // Arrange
        var resolver = new DefaultConflictResolver(ConflictResolution.UseRemote);
        var localItem = new SyncItem 
        { 
            Path = "test", 
            IsDirectory = false, 
            Size = 100 
        };
        var remoteItem = new SyncItem 
        { 
            Path = "test", 
            IsDirectory = true, 
            Size = 0 
        };

        var conflict = new FileConflictEventArgs("test", localItem, remoteItem, ConflictType.TypeConflict);

        // Act
        var result = await resolver.ResolveConflictAsync(conflict);

        // Assert
        Assert.Equal(ConflictResolution.UseRemote, result);
    }

    [Fact]
    public async Task ResolveConflictAsync_LargeFile_ReturnsDefaultResolution()
    {
        // Arrange
        var resolver = new DefaultConflictResolver(ConflictResolution.Skip);
        var localItem = new SyncItem 
        { 
            Path = "largefile.bin", 
            Size = 1024 * 1024 * 100, // 100MB
            LastModified = DateTime.UtcNow.AddHours(-1)
        };
        var remoteItem = new SyncItem 
        { 
            Path = "largefile.bin", 
            Size = 1024 * 1024 * 200, // 200MB
            LastModified = DateTime.UtcNow
        };

        var conflict = new FileConflictEventArgs("largefile.bin", localItem, remoteItem, ConflictType.BothModified);

        // Act
        var result = await resolver.ResolveConflictAsync(conflict);

        // Assert
        Assert.Equal(ConflictResolution.Skip, result);
    }

    [Fact]
    public async Task ResolveConflictAsync_CancellationRequested_ThrowsOperationCanceledException()
    {
        // Arrange
        var resolver = new DefaultConflictResolver(ConflictResolution.UseLocal);
        var localItem = new SyncItem { Path = "test.txt" };
        var remoteItem = new SyncItem { Path = "test.txt" };
        
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var conflict = new FileConflictEventArgs("test.txt", localItem, remoteItem, ConflictType.BothModified);

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            resolver.ResolveConflictAsync(conflict, cts.Token));
    }
}