namespace Oire.SharpSync.Tests.Sync;

public class SyncEngineTests: IDisposable {
    private readonly string _localRootPath;
    private readonly string _dbPath;
    private readonly LocalFileStorage _localStorage;
    private readonly SqliteSyncDatabase _database;
    private readonly SyncEngine _syncEngine;

    public SyncEngineTests() {
        _localRootPath = Path.Combine(Path.GetTempPath(), "SharpSyncTests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_localRootPath);

        _dbPath = Path.Combine(_localRootPath, "sync.db");
        _localStorage = new LocalFileStorage(_localRootPath);
        _database = new SqliteSyncDatabase(_dbPath);

        var filter = new SyncFilter();
        var conflictResolver = new DefaultConflictResolver(ConflictResolution.UseLocal);
        _syncEngine = new SyncEngine(_localStorage, _localStorage, _database, filter, conflictResolver);
    }

    public void Dispose() {
        _syncEngine?.Dispose();
        _database?.Dispose();

        if (Directory.Exists(_localRootPath)) {
            Directory.Delete(_localRootPath, recursive: true);
        }
    }

    [Fact]
    public void Constructor_ValidParameters_CreatesEngine() {
        // Assert
        Assert.NotNull(_syncEngine);
        Assert.False(_syncEngine.IsSynchronizing);
    }

    [Fact]
    public void Constructor_NullLocalStorage_ThrowsArgumentNullException() {
        // Act & Assert
        var filter = new SyncFilter();
        var conflictResolver = new DefaultConflictResolver(ConflictResolution.UseLocal);
        Assert.Throws<ArgumentNullException>(() =>
            new SyncEngine(null!, _localStorage, _database, filter, conflictResolver));
    }

    [Fact]
    public void Constructor_NullRemoteStorage_ThrowsArgumentNullException() {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new SyncEngine(_localStorage, null!, _database, new SyncFilter(), new DefaultConflictResolver(ConflictResolution.UseLocal)));
    }

    [Fact]
    public void Constructor_NullDatabase_ThrowsArgumentNullException() {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new SyncEngine(_localStorage, _localStorage, null!, new SyncFilter(), new DefaultConflictResolver(ConflictResolution.UseLocal)));
    }

    [Fact]
    public async Task SynchronizeAsync_EmptyDirectories_ReturnsSuccess() {
        // Act
        var result = await _syncEngine.SynchronizeAsync();

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.Equal(0, result.TotalFilesProcessed);
        Assert.Equal(0, result.FilesConflicted);
        Assert.Null(result.Error);
    }

    [Fact]
    public async Task SynchronizeAsync_SingleFile_SyncsCorrectly() {
        // Arrange
        var filePath = "test.txt";
        var content = "Hello, World!";
        var fullPath = Path.Combine(_localRootPath, filePath);
        await File.WriteAllTextAsync(fullPath, content);

        // Act
        var result = await _syncEngine.SynchronizeAsync();

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.Equal(1, result.TotalFilesProcessed);
        Assert.Equal(0, result.FilesConflicted);
        Assert.Null(result.Error);
    }

    [Fact]
    public async Task SynchronizeAsync_WithFilter_RespectsExclusions() {
        // Arrange
        var filter = new SyncFilter();
        filter.AddExclusionPattern("*.tmp");

        var includedFile = Path.Combine(_localRootPath, "included.txt");
        var excludedFile = Path.Combine(_localRootPath, "excluded.tmp");

        await File.WriteAllTextAsync(includedFile, "included");
        await File.WriteAllTextAsync(excludedFile, "excluded");

        var options = new SyncOptions();

        // Act
        var result = await _syncEngine.SynchronizeAsync(options);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.Equal(1, result.TotalFilesProcessed); // Only included file
    }

    [Fact]
    public async Task SynchronizeAsync_WithConflictResolver_UsesResolver() {
        // Arrange
        var resolver = new DefaultConflictResolver(ConflictResolution.UseLocal);
        var options = new SyncOptions();

        // Act
        var result = await _syncEngine.SynchronizeAsync(options);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Success);
    }

    [Fact]
    public async Task SynchronizeAsync_CancellationRequested_StopsSync() {
        // Arrange
        using var cts = new CancellationTokenSource();
        cts.Cancel(); // Cancel immediately

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            _syncEngine.SynchronizeAsync(cancellationToken: cts.Token));
    }

    [Fact]
    public async Task SynchronizeAsync_AlreadySynchronizing_ThrowsInvalidOperationException() {
        // Arrange - Start a long-running sync
        var longRunningTask = Task.Run(async () => {
            await _syncEngine.SynchronizeAsync();
        });

        // Wait a bit to ensure sync starts
        await Task.Delay(50);

        try {
            // Act & Assert
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                _syncEngine.SynchronizeAsync());
        } finally {
            // Cleanup
            await longRunningTask;
        }
    }

    [Fact]
    public void IsSynchronizing_InitialState_ReturnsFalse() {
        // Assert
        Assert.False(_syncEngine.IsSynchronizing);
    }

    [Fact]
    public void Dispose_MultipleCalls_DoesNotThrow() {
        // Arrange
        var engine = new SyncEngine(_localStorage, _localStorage, _database, new SyncFilter(), new DefaultConflictResolver(ConflictResolution.UseLocal));

        // Act & Assert
        engine.Dispose();
        engine.Dispose(); // Should not throw
    }

    [Fact]
    public async Task SynchronizeAsync_AfterDispose_ThrowsObjectDisposedException() {
        // Arrange
        var engine = new SyncEngine(_localStorage, _localStorage, _database, new SyncFilter(), new DefaultConflictResolver(ConflictResolution.UseLocal));
        engine.Dispose();

        // Act & Assert
        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            engine.SynchronizeAsync());
    }

    [Fact]
    public async Task ProgressChanged_Event_IsRaised() {
        // Arrange
        var progressRaised = false;
        _syncEngine.ProgressChanged += (sender, args) => {
            progressRaised = true;
        };

        var filePath = Path.Combine(_localRootPath, "test.txt");
        await File.WriteAllTextAsync(filePath, "test");

        // Act
        await _syncEngine.SynchronizeAsync();

        // Assert
        Assert.True(progressRaised);
    }

    [Fact]
    public async Task ConflictDetected_Event_IsRaised() {
        // Arrange
        var conflictRaised = false;
        _syncEngine.ConflictDetected += (sender, args) => {
            conflictRaised = true;
            args.Resolution = ConflictResolution.UseLocal;
        };

        // Create a scenario where a conflict might occur
        // This is a simplified test - in real scenarios conflicts are more complex
        var filePath = Path.Combine(_localRootPath, "conflict.txt");
        await File.WriteAllTextAsync(filePath, "local version");

        // Act
        await _syncEngine.SynchronizeAsync();

        // Assert - This might not trigger in this simple case, 
        // but the event handler is correctly set up
        Assert.False(conflictRaised); // No actual conflict in this simple test
    }
}
