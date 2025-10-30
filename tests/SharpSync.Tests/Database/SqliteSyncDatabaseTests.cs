namespace Oire.SharpSync.Tests.Database;

public class SqliteSyncDatabaseTests: IDisposable {
    private readonly string _dbPath;
    private readonly SqliteSyncDatabase _database;

    public SqliteSyncDatabaseTests() {
        _dbPath = Path.Combine(Path.GetTempPath(), $"test_sync_{Guid.NewGuid()}.db");
        _database = new SqliteSyncDatabase(_dbPath);
        _database.InitializeAsync().GetAwaiter().GetResult();
    }

    public void Dispose() {
        _database?.Dispose();
        if (File.Exists(_dbPath)) {
            File.Delete(_dbPath);
        }
    }

    [Fact]
    public void Constructor_CreatesDatabase() {
        // Assert
        Assert.True(File.Exists(_dbPath));
    }

    [Fact]
    public async Task InitializeAsync_CreatesDirectoryIfNotExists() {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), $"test_dir_{Guid.NewGuid()}");
        var dbPath = Path.Combine(tempDir, "subdir", "test.db");
        var database = new SqliteSyncDatabase(dbPath);

        try {
            // Act
            await database.InitializeAsync();

            // Assert
            Assert.True(Directory.Exists(Path.GetDirectoryName(dbPath)));
            Assert.True(File.Exists(dbPath));
        } finally {
            // Cleanup
            database.Dispose();
            if (Directory.Exists(tempDir)) {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Fact]
    public async Task GetSyncStateAsync_NonExistentPath_ReturnsNull() {
        // Act
        var state = await _database.GetSyncStateAsync("nonexistent.txt");

        // Assert
        Assert.Null(state);
    }

    [Fact]
    public async Task UpdateSyncStateAsync_NewState_InsertsRecord() {
        // Arrange
        var state = new SyncState {
            Path = "test.txt",
            IsDirectory = false,
            LocalHash = "hash123",
            LocalSize = 1024,
            LocalModified = DateTime.UtcNow,
            Status = SyncStatus.LocalNew
        };

        // Act
        await _database.UpdateSyncStateAsync(state);

        // Assert
        var retrievedState = await _database.GetSyncStateAsync("test.txt");
        Assert.NotNull(retrievedState);
        Assert.Equal("test.txt", retrievedState.Path);
        Assert.Equal("hash123", retrievedState.LocalHash);
        Assert.Equal(1024, retrievedState.LocalSize);
        Assert.Equal(SyncStatus.LocalNew, retrievedState.Status);
    }

    [Fact]
    public async Task UpdateSyncStateAsync_ExistingState_UpdatesRecord() {
        // Arrange
        var originalState = new SyncState {
            Path = "test.txt",
            LocalHash = "original",
            Status = SyncStatus.LocalNew
        };
        await _database.UpdateSyncStateAsync(originalState);

        var updatedState = await _database.GetSyncStateAsync("test.txt");
        updatedState!.LocalHash = "updated";
        updatedState.Status = SyncStatus.Synced;

        // Act
        await _database.UpdateSyncStateAsync(updatedState);

        // Assert
        var retrievedState = await _database.GetSyncStateAsync("test.txt");
        Assert.NotNull(retrievedState);
        Assert.Equal("updated", retrievedState.LocalHash);
        Assert.Equal(SyncStatus.Synced, retrievedState.Status);
    }

    [Fact]
    public async Task GetAllSyncStatesAsync_EmptyDatabase_ReturnsEmpty() {
        // Act
        var states = await _database.GetAllSyncStatesAsync();

        // Assert
        Assert.NotNull(states);
        Assert.Empty(states);
    }

    [Fact]
    public async Task GetAllSyncStatesAsync_WithStates_ReturnsAllStates() {
        // Arrange
        var states = new[]
        {
            new SyncState { Path = "file1.txt", Status = SyncStatus.LocalNew },
            new SyncState { Path = "file2.txt", Status = SyncStatus.RemoteNew },
            new SyncState { Path = "dir1", IsDirectory = true, Status = SyncStatus.Synced }
        };

        foreach (var state in states) {
            await _database.UpdateSyncStateAsync(state);
        }

        // Act
        var allStates = await _database.GetAllSyncStatesAsync();

        // Assert
        Assert.Equal(3, allStates.Count());
        Assert.Contains(allStates, s => s.Path == "file1.txt");
        Assert.Contains(allStates, s => s.Path == "file2.txt");
        Assert.Contains(allStates, s => s.Path == "dir1");
    }

    [Fact]
    public async Task GetSyncStatesByStatusAsync_FiltersByStatus() {
        // Arrange
        var states = new[]
        {
            new SyncState { Path = "synced1.txt", Status = SyncStatus.Synced },
            new SyncState { Path = "synced2.txt", Status = SyncStatus.Synced },
            new SyncState { Path = "conflict.txt", Status = SyncStatus.Conflict },
            new SyncState { Path = "error.txt", Status = SyncStatus.Error }
        };

        foreach (var state in states) {
            await _database.UpdateSyncStateAsync(state);
        }

        // Act
        var allStates = await _database.GetAllSyncStatesAsync();
        var syncedStates = allStates.Where(s => s.Status == SyncStatus.Synced).ToList();
        var conflictStates = allStates.Where(s => s.Status == SyncStatus.Conflict).ToList();

        // Assert
        Assert.Equal(2, syncedStates.Count);
        Assert.All(syncedStates, s => Assert.Equal(SyncStatus.Synced, s.Status));

        Assert.Single(conflictStates);
        Assert.Equal("conflict.txt", conflictStates.First().Path);
    }

    [Fact]
    public async Task DeleteSyncStateAsync_ExistingState_RemovesRecord() {
        // Arrange
        var state = new SyncState { Path = "delete_me.txt", Status = SyncStatus.LocalNew };
        await _database.UpdateSyncStateAsync(state);

        // Verify it exists
        var existingState = await _database.GetSyncStateAsync("delete_me.txt");
        Assert.NotNull(existingState);

        // Act
        await _database.DeleteSyncStateAsync("delete_me.txt");

        // Assert
        var deletedState = await _database.GetSyncStateAsync("delete_me.txt");
        Assert.Null(deletedState);
    }

    [Fact]
    public async Task DeleteSyncStateAsync_NonExistentState_DoesNotThrow() {
        // Act & Assert - should not throw
        await _database.DeleteSyncStateAsync("nonexistent.txt");
    }

    [Fact]
    public async Task ClearAllSyncStatesAsync_RemovesAllRecords() {
        // Arrange
        var states = new[]
        {
            new SyncState { Path = "file1.txt", Status = SyncStatus.LocalNew },
            new SyncState { Path = "file2.txt", Status = SyncStatus.RemoteNew },
            new SyncState { Path = "file3.txt", Status = SyncStatus.Synced }
        };

        foreach (var state in states) {
            await _database.UpdateSyncStateAsync(state);
        }

        // Verify states exist
        var allStates = await _database.GetAllSyncStatesAsync();
        Assert.Equal(3, allStates.Count());

        // Act
        await _database.ClearAsync();

        // Assert
        var clearedStates = await _database.GetAllSyncStatesAsync();
        Assert.Empty(clearedStates);
    }

    [Fact]
    public async Task GetDatabaseStatsAsync_ReturnsStats() {
        // Arrange
        var states = new[]
        {
            new SyncState { Path = "synced1.txt", Status = SyncStatus.Synced },
            new SyncState { Path = "synced2.txt", Status = SyncStatus.Synced },
            new SyncState { Path = "conflict.txt", Status = SyncStatus.Conflict },
            new SyncState { Path = "error.txt", Status = SyncStatus.Error },
            new SyncState { Path = "pending.txt", Status = SyncStatus.LocalNew }
        };

        foreach (var state in states) {
            await _database.UpdateSyncStateAsync(state);
        }

        // Act
        var stats = await _database.GetStatsAsync();

        // Assert
        Assert.Equal(5, stats.TotalItems);
        Assert.Equal(2, stats.SyncedItems);
        Assert.Equal(1, stats.PendingItems);
        Assert.Equal(1, stats.ConflictedItems);
        Assert.Equal(1, stats.ErrorItems);
        Assert.True(stats.DatabaseSize > 0);
    }

    [Theory]
    [InlineData(SyncStatus.Synced)]
    [InlineData(SyncStatus.LocalNew)]
    [InlineData(SyncStatus.RemoteNew)]
    [InlineData(SyncStatus.LocalModified)]
    [InlineData(SyncStatus.RemoteModified)]
    [InlineData(SyncStatus.LocalDeleted)]
    [InlineData(SyncStatus.RemoteDeleted)]
    [InlineData(SyncStatus.Conflict)]
    [InlineData(SyncStatus.Error)]
    [InlineData(SyncStatus.Ignored)]
    public async Task SyncStatus_AllValues_SupportedCorrectly(SyncStatus status) {
        // Arrange
        var state = new SyncState {
            Path = $"test_{status}.txt",
            Status = status
        };

        // Act
        await _database.UpdateSyncStateAsync(state);

        // Assert
        var retrievedState = await _database.GetSyncStateAsync($"test_{status}.txt");
        Assert.NotNull(retrievedState);
        Assert.Equal(status, retrievedState.Status);
    }

    [Fact]
    public async Task UpdateSyncStateAsync_WithAllProperties_PersistsCorrectly() {
        // Arrange
        var now = DateTime.UtcNow;
        var state = new SyncState {
            Path = "complete_test.txt",
            IsDirectory = false,
            LocalHash = "local_hash_123",
            RemoteHash = "remote_hash_456",
            LocalModified = now.AddMinutes(-10),
            RemoteModified = now.AddMinutes(-5),
            LocalSize = 2048,
            RemoteSize = 4096,
            Status = SyncStatus.Conflict,
            LastSyncTime = now.AddMinutes(-15),
            ETag = "etag_789",
            ErrorMessage = "Test error message",
            SyncAttempts = 2
        };

        // Act
        await _database.UpdateSyncStateAsync(state);

        // Assert
        var retrieved = await _database.GetSyncStateAsync("complete_test.txt");
        Assert.NotNull(retrieved);
        Assert.Equal("complete_test.txt", retrieved.Path);
        Assert.False(retrieved.IsDirectory);
        Assert.Equal("local_hash_123", retrieved.LocalHash);
        Assert.Equal("remote_hash_456", retrieved.RemoteHash);
        Assert.Equal(2048, retrieved.LocalSize);
        Assert.Equal(4096, retrieved.RemoteSize);
        Assert.Equal(SyncStatus.Conflict, retrieved.Status);
        Assert.Equal("etag_789", retrieved.ETag);
        Assert.Equal("Test error message", retrieved.ErrorMessage);
        Assert.Equal(2, retrieved.SyncAttempts);

        // DateTime comparison with some tolerance for precision
        Assert.True(Math.Abs((retrieved.LocalModified!.Value - state.LocalModified!.Value).TotalSeconds) < 1);
        Assert.True(Math.Abs((retrieved.RemoteModified!.Value - state.RemoteModified!.Value).TotalSeconds) < 1);
        Assert.True(Math.Abs((retrieved.LastSyncTime!.Value - state.LastSyncTime!.Value).TotalSeconds) < 1);
    }

    [Fact]
    public void Dispose_MultipleCalls_DoesNotThrow() {
        // Arrange
        var database = new SqliteSyncDatabase(_dbPath + "_dispose_test");

        // Act & Assert
        database.Dispose();
        database.Dispose(); // Should not throw
    }
}
