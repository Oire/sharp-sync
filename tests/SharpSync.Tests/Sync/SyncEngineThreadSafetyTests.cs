using Oire.SharpSync.Core;
using Oire.SharpSync.Database;
using Oire.SharpSync.Storage;
using Oire.SharpSync.Sync;

namespace Oire.SharpSync.Tests.Sync;

/// <summary>
/// Tests for thread-safety guarantees of SyncEngine.
/// These tests verify that:
/// - State properties can be read safely from any thread
/// - NotifyLocalChangeAsync can be called from multiple threads concurrently
/// - PauseAsync/ResumeAsync can be called from different threads than the sync thread
/// - GetPendingOperationsAsync can be called while sync is running
/// - Only one sync operation can run at a time
/// </summary>
public class SyncEngineThreadSafetyTests: IDisposable {
    private readonly string _localRootPath;
    private readonly string _remoteRootPath;
    private readonly string _dbPath;
    private readonly LocalFileStorage _localStorage;
    private readonly LocalFileStorage _remoteStorage;
    private readonly SqliteSyncDatabase _database;
    private readonly SyncEngine _syncEngine;

    public SyncEngineThreadSafetyTests() {
        _localRootPath = Path.Combine(Path.GetTempPath(), "SharpSyncThreadTests", "Local", Guid.NewGuid().ToString());
        _remoteRootPath = Path.Combine(Path.GetTempPath(), "SharpSyncThreadTests", "Remote", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_localRootPath);
        Directory.CreateDirectory(_remoteRootPath);

        _dbPath = Path.Combine(Path.GetTempPath(), "SharpSyncThreadTests", $"sync_{Guid.NewGuid()}.db");
        _localStorage = new LocalFileStorage(_localRootPath);
        _remoteStorage = new LocalFileStorage(_remoteRootPath);
        _database = new SqliteSyncDatabase(_dbPath);
        _database.InitializeAsync().GetAwaiter().GetResult();

        var filter = new SyncFilter();
        var conflictResolver = new DefaultConflictResolver(ConflictResolution.UseLocal);
        _syncEngine = new SyncEngine(_localStorage, _remoteStorage, _database, filter, conflictResolver);
    }

    public void Dispose() {
        _syncEngine?.Dispose();
        _database?.Dispose();

        if (Directory.Exists(_localRootPath)) {
            Directory.Delete(_localRootPath, recursive: true);
        }

        if (Directory.Exists(_remoteRootPath)) {
            Directory.Delete(_remoteRootPath, recursive: true);
        }

        if (File.Exists(_dbPath)) {
            File.Delete(_dbPath);
        }
    }

    #region Concurrent Sync Prevention Tests

    [Fact]
    public async Task ConcurrentSyncAttempt_ThrowsInvalidOperationException() {
        // Arrange - Create files to make sync take some time
        for (int i = 0; i < 10; i++) {
            await File.WriteAllTextAsync(Path.Combine(_localRootPath, $"file{i}.txt"), new string('x', 10000));
        }

        var syncStarted = new TaskCompletionSource();
        _syncEngine.ProgressChanged += (s, e) => {
            if (e.Operation != SyncOperation.Scanning) {
                syncStarted.TrySetResult();
            }
        };

        // Act - Start first sync
        var firstSyncTask = Task.Run(() => _syncEngine.SynchronizeAsync());

        // Wait for sync to start
        await Task.WhenAny(syncStarted.Task, Task.Delay(TimeSpan.FromSeconds(5)));

        // Try to start a second sync
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _syncEngine.SynchronizeAsync()
        );

        // Allow first sync to complete
        await firstSyncTask;

        // Assert
        Assert.Contains("already in progress", exception.Message.ToLower());
    }

    [Fact]
    public async Task SequentialSyncs_SucceedAfterFirstCompletes() {
        // Arrange
        await File.WriteAllTextAsync(Path.Combine(_localRootPath, "file1.txt"), "content1");

        // Act - Run two syncs sequentially
        var result1 = await _syncEngine.SynchronizeAsync();

        await File.WriteAllTextAsync(Path.Combine(_localRootPath, "file2.txt"), "content2");
        var result2 = await _syncEngine.SynchronizeAsync();

        // Assert
        Assert.True(result1.Success);
        Assert.True(result2.Success);
    }

    #endregion

    #region State Property Thread-Safety Tests

    [Fact]
    public async Task StateProperties_CanBeReadFromMultipleThreadsConcurrently() {
        // Arrange
        for (int i = 0; i < 5; i++) {
            await File.WriteAllTextAsync(Path.Combine(_localRootPath, $"state_test_{i}.txt"), "content");
        }

        var syncStarted = new TaskCompletionSource();
        var stateReadCount = 0;
        var stateReadErrors = new List<Exception>();

        _syncEngine.ProgressChanged += (s, e) => {
            if (e.Operation != SyncOperation.Scanning) {
                syncStarted.TrySetResult();
            }
        };

        // Act - Start sync and read state from multiple threads
        var syncTask = Task.Run(() => _syncEngine.SynchronizeAsync());

        await Task.WhenAny(syncStarted.Task, Task.Delay(TimeSpan.FromSeconds(3)));

        var readerTasks = Enumerable.Range(0, 10).Select(_ => Task.Run(() => {
            try {
                for (int i = 0; i < 100; i++) {
                    // These reads should all be safe
                    var isSyncing = _syncEngine.IsSynchronizing;
                    var isPaused = _syncEngine.IsPaused;
                    var state = _syncEngine.State;
                    Interlocked.Increment(ref stateReadCount);
                }
            } catch (Exception ex) {
                lock (stateReadErrors) {
                    stateReadErrors.Add(ex);
                }
            }
        })).ToArray();

        await Task.WhenAll(readerTasks);
        await syncTask;

        // Assert
        Assert.Empty(stateReadErrors);
        Assert.Equal(1000, stateReadCount); // 10 tasks * 100 reads each
    }

    [Fact]
    public async Task IsSynchronizing_ReturnsCorrectValueDuringSync() {
        // Arrange
        await File.WriteAllTextAsync(Path.Combine(_localRootPath, "sync_check.txt"), "content");

        var observedSynchronizing = false;

        _syncEngine.ProgressChanged += (s, e) => {
            if (_syncEngine.IsSynchronizing) {
                observedSynchronizing = true;
            }
        };

        // Act
        var result = await _syncEngine.SynchronizeAsync();

        // Assert
        Assert.True(result.Success);
        Assert.True(observedSynchronizing);
        Assert.False(_syncEngine.IsSynchronizing); // Should be false after sync completes
    }

    #endregion

    #region NotifyLocalChangeAsync Thread-Safety Tests

    [Fact]
    public async Task NotifyLocalChangeAsync_CanBeCalledFromMultipleThreadsConcurrently() {
        // Arrange
        var tasks = new List<Task>();
        var errors = new List<Exception>();

        // Act - Simulate FileSystemWatcher events from multiple threads
        for (int i = 0; i < 100; i++) {
            var index = i;
            tasks.Add(Task.Run(async () => {
                try {
                    await _syncEngine.NotifyLocalChangeAsync($"file{index}.txt", ChangeType.Created);
                } catch (Exception ex) {
                    lock (errors) {
                        errors.Add(ex);
                    }
                }
            }));
        }

        await Task.WhenAll(tasks);
        var pending = await _syncEngine.GetPendingOperationsAsync();

        // Assert
        Assert.Empty(errors);
        Assert.Equal(100, pending.Count);
    }

    [Fact]
    public async Task NotifyLocalChangeAsync_CanBeCalledWhileSyncIsRunning() {
        // Arrange - Create files to make sync take some time
        for (int i = 0; i < 20; i++) {
            await File.WriteAllTextAsync(Path.Combine(_localRootPath, $"existing_{i}.txt"), new string('x', 5000));
        }

        var syncStarted = new TaskCompletionSource();
        var notificationsMade = 0;
        var notificationErrors = new List<Exception>();

        _syncEngine.ProgressChanged += (s, e) => {
            if (e.Operation != SyncOperation.Scanning) {
                syncStarted.TrySetResult();
            }
        };

        // Act - Start sync
        var syncTask = Task.Run(() => _syncEngine.SynchronizeAsync());

        // Wait for sync to start
        await Task.WhenAny(syncStarted.Task, Task.Delay(TimeSpan.FromSeconds(3)));

        // Make notifications while sync is running
        var notificationTasks = Enumerable.Range(0, 10).Select(i => Task.Run(async () => {
            try {
                await _syncEngine.NotifyLocalChangeAsync($"new_file_{i}.txt", ChangeType.Created);
                Interlocked.Increment(ref notificationsMade);
            } catch (Exception ex) {
                lock (notificationErrors) {
                    notificationErrors.Add(ex);
                }
            }
        })).ToArray();

        await Task.WhenAll(notificationTasks);
        var result = await syncTask;

        // Assert
        Assert.True(result.Success);
        Assert.Empty(notificationErrors);
        Assert.Equal(10, notificationsMade);
    }

    [Fact]
    public async Task NotifyLocalChangesAsync_BatchNotification_ThreadSafe() {
        // Arrange
        var batchTasks = new List<Task>();
        var errors = new List<Exception>();

        // Act - Multiple threads sending batch notifications
        for (int batch = 0; batch < 10; batch++) {
            var batchNum = batch;
            batchTasks.Add(Task.Run(async () => {
                try {
                    var changes = Enumerable.Range(0, 10)
                        .Select(i => ($"batch{batchNum}_file{i}.txt", ChangeType.Created))
                        .ToList();
                    await _syncEngine.NotifyLocalChangesAsync(changes);
                } catch (Exception ex) {
                    lock (errors) {
                        errors.Add(ex);
                    }
                }
            }));
        }

        await Task.WhenAll(batchTasks);
        var pending = await _syncEngine.GetPendingOperationsAsync();

        // Assert
        Assert.Empty(errors);
        Assert.Equal(100, pending.Count); // 10 batches * 10 files
    }

    #endregion

    #region PauseAsync/ResumeAsync Thread-Safety Tests

    [Fact]
    public async Task PauseAsync_CanBeCalledFromDifferentThreadThanSync() {
        // Arrange - Create files to make sync take some time
        for (int i = 0; i < 30; i++) {
            await File.WriteAllTextAsync(Path.Combine(_localRootPath, $"pause_thread_test_{i}.txt"), new string('x', 5000));
        }

        var syncStarted = new TaskCompletionSource();
        var pauseCalled = false;
        var pauseError = (Exception?)null;

        _syncEngine.ProgressChanged += (s, e) => {
            if (e.Operation != SyncOperation.Scanning && !pauseCalled) {
                syncStarted.TrySetResult();
            }
        };

        // Act - Start sync on one thread
        var syncTask = Task.Run(() => _syncEngine.SynchronizeAsync());

        // Wait for sync to start
        await Task.WhenAny(syncStarted.Task, Task.Delay(TimeSpan.FromSeconds(3)));

        // Pause from a different thread
        var pauseTask = Task.Run(async () => {
            try {
                pauseCalled = true;
                await _syncEngine.PauseAsync();
            } catch (Exception ex) {
                pauseError = ex;
            }
        });

        await pauseTask;

        // Resume to complete sync
        await _syncEngine.ResumeAsync();
        var result = await syncTask;

        // Assert
        Assert.Null(pauseError);
        Assert.True(result.Success);
    }

    [Fact]
    public async Task MultiplePauseResumeCalls_FromDifferentThreads_AreIdempotent() {
        // Arrange
        for (int i = 0; i < 20; i++) {
            await File.WriteAllTextAsync(Path.Combine(_localRootPath, $"multi_pause_{i}.txt"), new string('x', 3000));
        }

        var syncStarted = new TaskCompletionSource();
        var errors = new List<Exception>();

        _syncEngine.ProgressChanged += (s, e) => {
            if (e.Operation != SyncOperation.Scanning) {
                syncStarted.TrySetResult();
            }
        };

        // Act - Start sync
        var syncTask = Task.Run(() => _syncEngine.SynchronizeAsync());

        await Task.WhenAny(syncStarted.Task, Task.Delay(TimeSpan.FromSeconds(3)));

        // Multiple threads calling pause/resume
        var controlTasks = Enumerable.Range(0, 5).Select(_ => Task.Run(async () => {
            try {
                await _syncEngine.PauseAsync();
                await Task.Delay(10);
                await _syncEngine.ResumeAsync();
            } catch (Exception ex) {
                lock (errors) {
                    errors.Add(ex);
                }
            }
        })).ToArray();

        await Task.WhenAll(controlTasks);

        // Make sure we're resumed for sync to complete
        await _syncEngine.ResumeAsync();
        var result = await syncTask;

        // Assert
        Assert.Empty(errors);
        Assert.True(result.Success);
    }

    #endregion

    #region GetPendingOperationsAsync Thread-Safety Tests

    [Fact]
    public async Task GetPendingOperationsAsync_CanBeCalledWhileSyncIsRunning() {
        // Arrange
        for (int i = 0; i < 20; i++) {
            await File.WriteAllTextAsync(Path.Combine(_localRootPath, $"pending_query_{i}.txt"), new string('x', 3000));
        }

        var syncStarted = new TaskCompletionSource();
        var queryCount = 0;
        var queryErrors = new List<Exception>();

        _syncEngine.ProgressChanged += (s, e) => {
            if (e.Operation != SyncOperation.Scanning) {
                syncStarted.TrySetResult();
            }
        };

        // Add some pending changes
        for (int i = 0; i < 5; i++) {
            await _syncEngine.NotifyLocalChangeAsync($"pending_new_{i}.txt", ChangeType.Created);
        }

        // Act - Start sync
        var syncTask = Task.Run(() => _syncEngine.SynchronizeAsync());

        await Task.WhenAny(syncStarted.Task, Task.Delay(TimeSpan.FromSeconds(3)));

        // Query pending operations while sync runs
        var queryTasks = Enumerable.Range(0, 10).Select(_ => Task.Run(async () => {
            try {
                var pending = await _syncEngine.GetPendingOperationsAsync();
                Interlocked.Increment(ref queryCount);
            } catch (Exception ex) {
                lock (queryErrors) {
                    queryErrors.Add(ex);
                }
            }
        })).ToArray();

        await Task.WhenAll(queryTasks);
        await syncTask;

        // Assert
        Assert.Empty(queryErrors);
        Assert.Equal(10, queryCount);
    }

    [Fact]
    public async Task GetPendingOperationsAsync_ReturnsConsistentSnapshot() {
        // Arrange - Add changes from multiple threads simultaneously
        var addTasks = Enumerable.Range(0, 50).Select(i => Task.Run(async () => {
            await _syncEngine.NotifyLocalChangeAsync($"snapshot_test_{i}.txt", ChangeType.Created);
        })).ToArray();

        await Task.WhenAll(addTasks);

        // Act - Query from multiple threads
        var snapshots = new List<int>();
        var queryTasks = Enumerable.Range(0, 10).Select(_ => Task.Run(async () => {
            var pending = await _syncEngine.GetPendingOperationsAsync();
            lock (snapshots) {
                snapshots.Add(pending.Count);
            }
        })).ToArray();

        await Task.WhenAll(queryTasks);

        // Assert - All queries should see the same count since no modifications happened during queries
        Assert.All(snapshots, count => Assert.Equal(50, count));
    }

    #endregion

    #region ClearPendingChanges Thread-Safety Tests

    [Fact]
    public async Task ClearPendingChanges_IsThreadSafe() {
        // Arrange - Add some changes
        for (int i = 0; i < 20; i++) {
            await _syncEngine.NotifyLocalChangeAsync($"clear_test_{i}.txt", ChangeType.Created);
        }

        var errors = new List<Exception>();

        // Act - Clear from multiple threads (they should all succeed without error)
        var clearTasks = Enumerable.Range(0, 5).Select(_ => Task.Run(() => {
            try {
                _syncEngine.ClearPendingChanges();
            } catch (Exception ex) {
                lock (errors) {
                    errors.Add(ex);
                }
            }
        })).ToArray();

        await Task.WhenAll(clearTasks);
        var pending = await _syncEngine.GetPendingOperationsAsync();

        // Assert
        Assert.Empty(errors);
        Assert.Empty(pending);
    }

    #endregion

    #region Combined Concurrent Operations Tests

    [Fact]
    public async Task CombinedOperations_AllThreadSafeConcurrently() {
        // Arrange - Create files for sync
        for (int i = 0; i < 30; i++) {
            await File.WriteAllTextAsync(Path.Combine(_localRootPath, $"combined_{i}.txt"), new string('x', 2000));
        }

        var syncStarted = new TaskCompletionSource();
        var errors = new List<Exception>();

        _syncEngine.ProgressChanged += (s, e) => {
            if (e.Operation != SyncOperation.Scanning) {
                syncStarted.TrySetResult();
            }
        };

        // Act - Start sync
        var syncTask = Task.Run(() => _syncEngine.SynchronizeAsync());

        await Task.WhenAny(syncStarted.Task, Task.Delay(TimeSpan.FromSeconds(3)));

        // Run all operations concurrently
        var allTasks = new List<Task>();

        // State readers
        for (int i = 0; i < 5; i++) {
            allTasks.Add(Task.Run(() => {
                try {
                    for (int j = 0; j < 20; j++) {
                        _ = _syncEngine.IsSynchronizing;
                        _ = _syncEngine.IsPaused;
                        _ = _syncEngine.State;
                    }
                } catch (Exception ex) {
                    lock (errors) { errors.Add(ex); }
                }
            }));
        }

        // Change notifiers
        for (int i = 0; i < 5; i++) {
            var idx = i;
            allTasks.Add(Task.Run(async () => {
                try {
                    await _syncEngine.NotifyLocalChangeAsync($"concurrent_notify_{idx}.txt", ChangeType.Created);
                } catch (Exception ex) {
                    lock (errors) { errors.Add(ex); }
                }
            }));
        }

        // Pending operation queries
        for (int i = 0; i < 3; i++) {
            allTasks.Add(Task.Run(async () => {
                try {
                    await _syncEngine.GetPendingOperationsAsync();
                } catch (Exception ex) {
                    lock (errors) { errors.Add(ex); }
                }
            }));
        }

        // Pause/Resume (will be idempotent)
        allTasks.Add(Task.Run(async () => {
            try {
                await _syncEngine.PauseAsync();
                await Task.Delay(50);
                await _syncEngine.ResumeAsync();
            } catch (Exception ex) {
                lock (errors) { errors.Add(ex); }
            }
        }));

        await Task.WhenAll(allTasks);

        // Ensure resumed
        await _syncEngine.ResumeAsync();
        var result = await syncTask;

        // Assert
        Assert.Empty(errors);
        Assert.True(result.Success);
    }

    #endregion

    #region Stress Tests

    [Fact]
    public async Task StressTest_HighVolumeNotifications_NoDataLoss() {
        // Arrange
        const int notificationCount = 1000;
        var errors = new List<Exception>();

        // Act - Send many notifications from multiple threads
        var tasks = Enumerable.Range(0, notificationCount).Select(i => Task.Run(async () => {
            try {
                await _syncEngine.NotifyLocalChangeAsync($"stress_{i}.txt", ChangeType.Created);
            } catch (Exception ex) {
                lock (errors) { errors.Add(ex); }
            }
        })).ToArray();

        await Task.WhenAll(tasks);
        var pending = await _syncEngine.GetPendingOperationsAsync();

        // Assert
        Assert.Empty(errors);
        Assert.Equal(notificationCount, pending.Count);
    }

    [Fact]
    public async Task StressTest_RapidStateReads_NoErrors() {
        // Arrange
        await File.WriteAllTextAsync(Path.Combine(_localRootPath, "stress_state.txt"), "content");

        var readCount = 0;
        var errors = new List<Exception>();

        // Start sync
        var syncTask = _syncEngine.SynchronizeAsync();

        // Act - Rapid state reads
        var readerTasks = Enumerable.Range(0, 20).Select(n => Task.Run(() => {
            try {
                for (int i = 0; i < 500; i++) {
                    var isSyncing = _syncEngine.IsSynchronizing;
                    var isPaused = _syncEngine.IsPaused;
                    var state = _syncEngine.State;
                    Interlocked.Increment(ref readCount);
                }
            } catch (Exception ex) {
                lock (errors) { errors.Add(ex); }
            }
        })).ToArray();

        await Task.WhenAll(readerTasks);
        await syncTask;

        // Assert
        Assert.Empty(errors);
        Assert.Equal(20 * 500, readCount);
    }

    #endregion
}
