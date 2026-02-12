namespace Oire.SharpSync.Tests.Core;

public class SmartConflictResolverTests {
    [Fact]
    public void Constructor_WithoutParameters_CreatesResolverWithSkipDefault() {
        // Arrange & Act
        var resolver = new SmartConflictResolver();

        // Assert - resolver should be created successfully
        Assert.NotNull(resolver);
    }

    [Theory]
    [InlineData(ConflictResolution.Ask)]
    [InlineData(ConflictResolution.UseLocal)]
    [InlineData(ConflictResolution.UseRemote)]
    [InlineData(ConflictResolution.Skip)]
    [InlineData(ConflictResolution.RenameLocal)]
    public void Constructor_WithDefaultResolution_SetsCorrectly(ConflictResolution defaultResolution) {
        // Arrange & Act
        var resolver = new SmartConflictResolver(null, defaultResolution);

        // Assert - resolver should be created successfully with the specified default
        Assert.NotNull(resolver);
    }

    [Fact]
    public async Task ResolveConflictAsync_WithConflictHandler_InvokesHandler() {
        // Arrange
        var handlerInvoked = false;
        var expectedResolution = ConflictResolution.UseLocal;

        SmartConflictResolver.ConflictHandlerDelegate handler = (analysis, ct) => {
            handlerInvoked = true;
            return Task.FromResult(expectedResolution);
        };

        var resolver = new SmartConflictResolver(handler);
        var localItem = new SyncItem {
            Path = "test.txt",
            Size = 1024,
            LastModified = DateTime.UtcNow
        };
        var remoteItem = new SyncItem {
            Path = "test.txt",
            Size = 2048,
            LastModified = DateTime.UtcNow.AddMinutes(5)
        };

        var conflict = new FileConflictEventArgs("test.txt", localItem, remoteItem, ConflictType.BothModified);

        // Act
        var result = await resolver.ResolveConflictAsync(conflict);

        // Assert
        Assert.True(handlerInvoked);
        Assert.Equal(expectedResolution, result);
    }

    [Fact]
    public async Task ResolveConflictAsync_DeletedLocallyModifiedRemotely_RecommiendsUseRemote() {
        // Arrange
        var resolver = new SmartConflictResolver();
        var remoteItem = new SyncItem {
            Path = "test.txt",
            Size = 1024,
            LastModified = DateTime.UtcNow
        };

        var conflict = new FileConflictEventArgs("test.txt", null, remoteItem, ConflictType.DeletedLocallyModifiedRemotely);

        // Act
        var result = await resolver.ResolveConflictAsync(conflict);

        // Assert
        Assert.Equal(ConflictResolution.UseRemote, result);
    }

    [Fact]
    public async Task ResolveConflictAsync_ModifiedLocallyDeletedRemotely_RecommendsUseLocal() {
        // Arrange
        var resolver = new SmartConflictResolver();
        var localItem = new SyncItem {
            Path = "test.txt",
            Size = 1024,
            LastModified = DateTime.UtcNow
        };

        var conflict = new FileConflictEventArgs("test.txt", localItem, null, ConflictType.ModifiedLocallyDeletedRemotely);

        // Act
        var result = await resolver.ResolveConflictAsync(conflict);

        // Assert
        Assert.Equal(ConflictResolution.UseLocal, result);
    }

    [Fact]
    public async Task ResolveConflictAsync_TypeConflict_WithNoHandler_Skips() {
        // Arrange
        var resolver = new SmartConflictResolver();
        var localItem = new SyncItem {
            Path = "test",
            IsDirectory = false,
            Size = 100
        };
        var remoteItem = new SyncItem {
            Path = "test",
            IsDirectory = true,
            Size = 0
        };

        var conflict = new FileConflictEventArgs("test", localItem, remoteItem, ConflictType.TypeConflict);

        // Act
        var result = await resolver.ResolveConflictAsync(conflict);

        // Assert
        Assert.Equal(ConflictResolution.Skip, result);
    }

    [Fact]
    public async Task ResolveConflictAsync_BothModified_RemoteNewer_RecommendsUseRemote() {
        // Arrange
        var resolver = new SmartConflictResolver();
        var localTime = DateTime.UtcNow;
        var remoteTime = localTime.AddMinutes(10);

        var localItem = new SyncItem {
            Path = "test.txt",
            Size = 1024,
            LastModified = localTime
        };
        var remoteItem = new SyncItem {
            Path = "test.txt",
            Size = 1024,
            LastModified = remoteTime
        };

        var conflict = new FileConflictEventArgs("test.txt", localItem, remoteItem, ConflictType.BothModified);

        // Act
        var result = await resolver.ResolveConflictAsync(conflict);

        // Assert
        Assert.Equal(ConflictResolution.UseRemote, result);
    }

    [Fact]
    public async Task ResolveConflictAsync_BothModified_LocalNewer_RecommendsUseLocal() {
        // Arrange
        var resolver = new SmartConflictResolver();
        var remoteTime = DateTime.UtcNow;
        var localTime = remoteTime.AddMinutes(10);

        var localItem = new SyncItem {
            Path = "test.txt",
            Size = 1024,
            LastModified = localTime
        };
        var remoteItem = new SyncItem {
            Path = "test.txt",
            Size = 1024,
            LastModified = remoteTime
        };

        var conflict = new FileConflictEventArgs("test.txt", localItem, remoteItem, ConflictType.BothModified);

        // Act
        var result = await resolver.ResolveConflictAsync(conflict);

        // Assert
        Assert.Equal(ConflictResolution.UseLocal, result);
    }

    [Fact]
    public async Task ResolveConflictAsync_BothModified_SameTime_UsesFallbackResolution() {
        // Arrange
        var defaultResolution = ConflictResolution.Skip;
        var resolver = new SmartConflictResolver(null, defaultResolution);
        var time = DateTime.UtcNow;

        var localItem = new SyncItem {
            Path = "test.txt",
            Size = 1024,
            LastModified = time
        };
        var remoteItem = new SyncItem {
            Path = "test.txt",
            Size = 1024,
            LastModified = time
        };

        var conflict = new FileConflictEventArgs("test.txt", localItem, remoteItem, ConflictType.BothModified);

        // Act
        var result = await resolver.ResolveConflictAsync(conflict);

        // Assert
        Assert.Equal(defaultResolution, result);
    }

    [Theory]
    [InlineData("test.exe")]
    [InlineData("test.dll")]
    [InlineData("test.bin")]
    [InlineData("test.zip")]
    [InlineData("test.jpg")]
    [InlineData("test.png")]
    [InlineData("test.mp4")]
    [InlineData("test.pdf")]
    [InlineData("test.docx")]
    public async Task ResolveConflictAsync_BinaryFiles_AnalyzesCorrectly(string fileName) {
        // Arrange
        ConflictAnalysis? capturedAnalysis = null;
        SmartConflictResolver.ConflictHandlerDelegate handler = (analysis, ct) => {
            capturedAnalysis = analysis;
            return Task.FromResult(ConflictResolution.UseLocal);
        };

        var resolver = new SmartConflictResolver(handler);
        var localItem = new SyncItem {
            Path = fileName,
            Size = 1024,
            LastModified = DateTime.UtcNow
        };
        var remoteItem = new SyncItem {
            Path = fileName,
            Size = 2048,
            LastModified = DateTime.UtcNow.AddMinutes(5)
        };

        var conflict = new FileConflictEventArgs(fileName, localItem, remoteItem, ConflictType.BothModified);

        // Act
        await resolver.ResolveConflictAsync(conflict);

        // Assert
        Assert.NotNull(capturedAnalysis);
        Assert.True(capturedAnalysis.IsLikelyBinary);
        Assert.False(capturedAnalysis.IsLikelyTextFile);
    }

    [Theory]
    [InlineData("test.txt")]
    [InlineData("test.md")]
    [InlineData("test.json")]
    [InlineData("test.cs")]
    [InlineData("test.js")]
    [InlineData("test.py")]
    [InlineData("test.html")]
    [InlineData("test.xml")]
    [InlineData("test.yml")]
    public async Task ResolveConflictAsync_TextFiles_AnalyzesCorrectly(string fileName) {
        // Arrange
        ConflictAnalysis? capturedAnalysis = null;
        SmartConflictResolver.ConflictHandlerDelegate handler = (analysis, ct) => {
            capturedAnalysis = analysis;
            return Task.FromResult(ConflictResolution.UseLocal);
        };

        var resolver = new SmartConflictResolver(handler);
        var localItem = new SyncItem {
            Path = fileName,
            Size = 1024,
            LastModified = DateTime.UtcNow
        };
        var remoteItem = new SyncItem {
            Path = fileName,
            Size = 2048,
            LastModified = DateTime.UtcNow.AddMinutes(5)
        };

        var conflict = new FileConflictEventArgs(fileName, localItem, remoteItem, ConflictType.BothModified);

        // Act
        await resolver.ResolveConflictAsync(conflict);

        // Assert
        Assert.NotNull(capturedAnalysis);
        Assert.True(capturedAnalysis.IsLikelyTextFile);
        Assert.False(capturedAnalysis.IsLikelyBinary);
    }

    [Fact]
    public async Task ResolveConflictAsync_UnknownExtension_NotMarkedAsBinaryOrText() {
        // Arrange
        ConflictAnalysis? capturedAnalysis = null;
        SmartConflictResolver.ConflictHandlerDelegate handler = (analysis, ct) => {
            capturedAnalysis = analysis;
            return Task.FromResult(ConflictResolution.UseLocal);
        };

        var resolver = new SmartConflictResolver(handler);
        var localItem = new SyncItem {
            Path = "test.unknownext",
            Size = 1024,
            LastModified = DateTime.UtcNow
        };
        var remoteItem = new SyncItem {
            Path = "test.unknownext",
            Size = 2048,
            LastModified = DateTime.UtcNow.AddMinutes(5)
        };

        var conflict = new FileConflictEventArgs("test.unknownext", localItem, remoteItem, ConflictType.BothModified);

        // Act
        await resolver.ResolveConflictAsync(conflict);

        // Assert
        Assert.NotNull(capturedAnalysis);
        Assert.False(capturedAnalysis.IsLikelyBinary);
        Assert.False(capturedAnalysis.IsLikelyTextFile);
    }

    [Fact]
    public async Task ResolveConflictAsync_SizeDifference_CalculatesCorrectly() {
        // Arrange
        ConflictAnalysis? capturedAnalysis = null;
        SmartConflictResolver.ConflictHandlerDelegate handler = (analysis, ct) => {
            capturedAnalysis = analysis;
            return Task.FromResult(ConflictResolution.UseLocal);
        };

        var resolver = new SmartConflictResolver(handler);
        var localItem = new SyncItem {
            Path = "test.txt",
            Size = 1000,
            LastModified = DateTime.UtcNow
        };
        var remoteItem = new SyncItem {
            Path = "test.txt",
            Size = 3500,
            LastModified = DateTime.UtcNow.AddMinutes(5)
        };

        var conflict = new FileConflictEventArgs("test.txt", localItem, remoteItem, ConflictType.BothModified);

        // Act
        await resolver.ResolveConflictAsync(conflict);

        // Assert
        Assert.NotNull(capturedAnalysis);
        Assert.Equal(1000, capturedAnalysis.LocalSize);
        Assert.Equal(3500, capturedAnalysis.RemoteSize);
        Assert.Equal(2500, capturedAnalysis.SizeDifference);
    }

    [Fact]
    public async Task ResolveConflictAsync_TimeDifference_CalculatesCorrectly() {
        // Arrange
        ConflictAnalysis? capturedAnalysis = null;
        SmartConflictResolver.ConflictHandlerDelegate handler = (analysis, ct) => {
            capturedAnalysis = analysis;
            return Task.FromResult(ConflictResolution.UseLocal);
        };

        var resolver = new SmartConflictResolver(handler);
        var localTime = DateTime.UtcNow;
        var remoteTime = localTime.AddMinutes(10); // 600 seconds difference

        var localItem = new SyncItem {
            Path = "test.txt",
            Size = 1024,
            LastModified = localTime
        };
        var remoteItem = new SyncItem {
            Path = "test.txt",
            Size = 1024,
            LastModified = remoteTime
        };

        var conflict = new FileConflictEventArgs("test.txt", localItem, remoteItem, ConflictType.BothModified);

        // Act
        await resolver.ResolveConflictAsync(conflict);

        // Assert
        Assert.NotNull(capturedAnalysis);
        Assert.Equal(600, capturedAnalysis.TimeDifference, 1.0); // Allow 1 second tolerance
        Assert.Equal("Remote", capturedAnalysis.NewerVersion);
    }

    [Fact]
    public async Task ResolveConflictAsync_CancellationRequested_ThrowsOperationCanceledException() {
        // Arrange
        var resolver = new SmartConflictResolver();
        var localItem = new SyncItem { Path = "test.txt", LastModified = DateTime.UtcNow };
        var remoteItem = new SyncItem { Path = "test.txt", LastModified = DateTime.UtcNow };

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var conflict = new FileConflictEventArgs("test.txt", localItem, remoteItem, ConflictType.BothModified);

        // Act & Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            resolver.ResolveConflictAsync(conflict, cts.Token));
    }

    [Fact]
    public async Task ResolveConflictAsync_HandlerCancellation_ThrowsOperationCanceledException() {
        // Arrange
        SmartConflictResolver.ConflictHandlerDelegate handler = async (analysis, ct) => {
            await Task.Delay(100, ct); // This should throw when cancelled
            return ConflictResolution.UseLocal;
        };

        var resolver = new SmartConflictResolver(handler);
        var localItem = new SyncItem { Path = "test.txt", LastModified = DateTime.UtcNow };
        var remoteItem = new SyncItem { Path = "test.txt", LastModified = DateTime.UtcNow };

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var conflict = new FileConflictEventArgs("test.txt", localItem, remoteItem, ConflictType.BothModified);

        // Act & Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            resolver.ResolveConflictAsync(conflict, cts.Token));
    }

    [Fact]
    public async Task ResolveConflictAsync_NoHandler_FallsBackToDefault() {
        // Arrange
        var defaultResolution = ConflictResolution.RenameLocal;
        var resolver = new SmartConflictResolver(null, defaultResolution);

        // Use same timestamp for both to ensure they're equal
        var timestamp = DateTime.UtcNow;

        var localItem = new SyncItem {
            Path = "test.txt",
            Size = 1024,
            LastModified = timestamp
        };
        var remoteItem = new SyncItem {
            Path = "test.txt",
            Size = 1024,
            LastModified = timestamp
        };

        var conflict = new FileConflictEventArgs("test.txt", localItem, remoteItem, ConflictType.BothModified);

        // Act
        var result = await resolver.ResolveConflictAsync(conflict);

        // Assert
        Assert.Equal(defaultResolution, result);
    }

    [Fact]
    public async Task ResolveConflictAsync_LargeFiles_HandlesCorrectly() {
        // Arrange
        ConflictAnalysis? capturedAnalysis = null;
        SmartConflictResolver.ConflictHandlerDelegate handler = (analysis, ct) => {
            capturedAnalysis = analysis;
            return Task.FromResult(ConflictResolution.UseRemote);
        };

        var resolver = new SmartConflictResolver(handler);
        var localItem = new SyncItem {
            Path = "largefile.bin",
            Size = 1024L * 1024L * 1024L * 5L, // 5GB
            LastModified = DateTime.UtcNow.AddHours(-1)
        };
        var remoteItem = new SyncItem {
            Path = "largefile.bin",
            Size = 1024L * 1024L * 1024L * 6L, // 6GB
            LastModified = DateTime.UtcNow
        };

        var conflict = new FileConflictEventArgs("largefile.bin", localItem, remoteItem, ConflictType.BothModified);

        // Act
        await resolver.ResolveConflictAsync(conflict);

        // Assert
        Assert.NotNull(capturedAnalysis);
        Assert.Equal(1024L * 1024L * 1024L * 5L, capturedAnalysis.LocalSize);
        Assert.Equal(1024L * 1024L * 1024L * 6L, capturedAnalysis.RemoteSize);
        Assert.True(capturedAnalysis.IsLikelyBinary);
    }

    [Theory]
    [InlineData("photo.JPG")]
    [InlineData("photo.Jpg")]
    [InlineData("archive.ZIP")]
    [InlineData("doc.PDF")]
    public async Task ResolveConflictAsync_BinaryExtensions_CaseInsensitive(string fileName) {
        // Arrange
        ConflictAnalysis? capturedAnalysis = null;
        SmartConflictResolver.ConflictHandlerDelegate handler = (analysis, ct) => {
            capturedAnalysis = analysis;
            return Task.FromResult(ConflictResolution.UseLocal);
        };

        var resolver = new SmartConflictResolver(handler);
        var localItem = new SyncItem { Path = fileName, Size = 1024, LastModified = DateTime.UtcNow };
        var remoteItem = new SyncItem { Path = fileName, Size = 2048, LastModified = DateTime.UtcNow.AddMinutes(5) };
        var conflict = new FileConflictEventArgs(fileName, localItem, remoteItem, ConflictType.BothModified);

        // Act
        await resolver.ResolveConflictAsync(conflict);

        // Assert
        Assert.NotNull(capturedAnalysis);
        Assert.True(capturedAnalysis.IsLikelyBinary);
    }

    [Theory]
    [InlineData("readme.MD")]
    [InlineData("config.JSON")]
    [InlineData("styles.CSS")]
    [InlineData("Program.CS")]
    public async Task ResolveConflictAsync_TextExtensions_CaseInsensitive(string fileName) {
        // Arrange
        ConflictAnalysis? capturedAnalysis = null;
        SmartConflictResolver.ConflictHandlerDelegate handler = (analysis, ct) => {
            capturedAnalysis = analysis;
            return Task.FromResult(ConflictResolution.UseLocal);
        };

        var resolver = new SmartConflictResolver(handler);
        var localItem = new SyncItem { Path = fileName, Size = 1024, LastModified = DateTime.UtcNow };
        var remoteItem = new SyncItem { Path = fileName, Size = 2048, LastModified = DateTime.UtcNow.AddMinutes(5) };
        var conflict = new FileConflictEventArgs(fileName, localItem, remoteItem, ConflictType.BothModified);

        // Act
        await resolver.ResolveConflictAsync(conflict);

        // Assert
        Assert.NotNull(capturedAnalysis);
        Assert.True(capturedAnalysis.IsLikelyTextFile);
    }

    [Fact]
    public async Task ResolveConflictAsync_NoExtension_NotBinaryOrText() {
        // Arrange
        ConflictAnalysis? capturedAnalysis = null;
        SmartConflictResolver.ConflictHandlerDelegate handler = (analysis, ct) => {
            capturedAnalysis = analysis;
            return Task.FromResult(ConflictResolution.UseLocal);
        };

        var resolver = new SmartConflictResolver(handler);
        var localItem = new SyncItem { Path = "Makefile", Size = 100, LastModified = DateTime.UtcNow };
        var remoteItem = new SyncItem { Path = "Makefile", Size = 200, LastModified = DateTime.UtcNow.AddMinutes(5) };
        var conflict = new FileConflictEventArgs("Makefile", localItem, remoteItem, ConflictType.BothModified);

        // Act
        await resolver.ResolveConflictAsync(conflict);

        // Assert
        Assert.NotNull(capturedAnalysis);
        Assert.False(capturedAnalysis.IsLikelyBinary);
        Assert.False(capturedAnalysis.IsLikelyTextFile);
    }
}
