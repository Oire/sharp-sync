using Oire.SharpSync.Core;
using Oire.SharpSync.Database;
using Oire.SharpSync.Storage;
using Oire.SharpSync.Sync;

namespace Oire.SharpSync.Tests.Sync;

public class VirtualFileCallbackTests: IDisposable {
    private readonly string _localRootPath;
    private readonly string _remoteRootPath;
    private readonly string _dbPath;
    private readonly LocalFileStorage _localStorage;
    private readonly LocalFileStorage _remoteStorage;
    private readonly SqliteSyncDatabase _database;

    public VirtualFileCallbackTests() {
        _localRootPath = Path.Combine(Path.GetTempPath(), "SharpSyncTests", "VirtualFile", "Local", Guid.NewGuid().ToString());
        _remoteRootPath = Path.Combine(Path.GetTempPath(), "SharpSyncTests", "VirtualFile", "Remote", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_localRootPath);
        Directory.CreateDirectory(_remoteRootPath);

        _dbPath = Path.Combine(Path.GetTempPath(), "SharpSyncTests", $"sync_vf_{Guid.NewGuid()}.db");
        _localStorage = new LocalFileStorage(_localRootPath);
        _remoteStorage = new LocalFileStorage(_remoteRootPath);
        _database = new SqliteSyncDatabase(_dbPath);
        _database.InitializeAsync().GetAwaiter().GetResult();
    }

    public void Dispose() {
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

        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task SynchronizeAsync_WithVirtualFileCallback_InvokesCallbackAfterDownload() {
        // Arrange
        var callbackInvocations = new List<(string RelativePath, string LocalFullPath, SyncItem Metadata)>();

        var options = new SyncOptions {
            CreateVirtualFilePlaceholders = true,
            VirtualFileCallback = (relativePath, localFullPath, metadata, ct) => {
                callbackInvocations.Add((relativePath, localFullPath, metadata));
                return Task.CompletedTask;
            }
        };

        // Create a file on the remote side to be downloaded
        var remoteFilePath = Path.Combine(_remoteRootPath, "document.txt");
        await File.WriteAllTextAsync(remoteFilePath, "Hello, Virtual World!");

        var filter = new SyncFilter();
        var conflictResolver = new DefaultConflictResolver(ConflictResolution.UseLocal);
        using var syncEngine = new SyncEngine(_localStorage, _remoteStorage, _database, filter, conflictResolver);

        // Act
        var result = await syncEngine.SynchronizeAsync(options);

        // Assert
        Assert.True(result.Success);
        Assert.Single(callbackInvocations);
        Assert.Equal("document.txt", callbackInvocations[0].RelativePath);
        Assert.Equal(Path.Combine(_localRootPath, "document.txt"), callbackInvocations[0].LocalFullPath);
        Assert.Equal(21, callbackInvocations[0].Metadata.Size); // "Hello, Virtual World!".Length
    }

    [Fact]
    public async Task SynchronizeAsync_WithVirtualFileCallbackDisabled_DoesNotInvokeCallback() {
        // Arrange
        var callbackInvoked = false;

        var options = new SyncOptions {
            CreateVirtualFilePlaceholders = false,
            VirtualFileCallback = (_, _, _, _) => {
                callbackInvoked = true;
                return Task.CompletedTask;
            }
        };

        // Create a file on the remote side
        var remoteFilePath = Path.Combine(_remoteRootPath, "document.txt");
        await File.WriteAllTextAsync(remoteFilePath, "Test content");

        var filter = new SyncFilter();
        var conflictResolver = new DefaultConflictResolver(ConflictResolution.UseLocal);
        using var syncEngine = new SyncEngine(_localStorage, _remoteStorage, _database, filter, conflictResolver);

        // Act
        await syncEngine.SynchronizeAsync(options);

        // Assert
        Assert.False(callbackInvoked);
    }

    [Fact]
    public async Task SynchronizeAsync_WithNullCallback_DoesNotThrow() {
        // Arrange
        var options = new SyncOptions {
            CreateVirtualFilePlaceholders = true,
            VirtualFileCallback = null
        };

        var remoteFilePath = Path.Combine(_remoteRootPath, "document.txt");
        await File.WriteAllTextAsync(remoteFilePath, "Test content");

        var filter = new SyncFilter();
        var conflictResolver = new DefaultConflictResolver(ConflictResolution.UseLocal);
        using var syncEngine = new SyncEngine(_localStorage, _remoteStorage, _database, filter, conflictResolver);

        // Act
        var result = await syncEngine.SynchronizeAsync(options);

        // Assert
        Assert.True(result.Success);
        Assert.True(File.Exists(Path.Combine(_localRootPath, "document.txt")));
    }

    [Fact]
    public async Task SynchronizeAsync_CallbackThrowsException_ContinuesSyncWithoutFailing() {
        // Arrange
        var callbackInvocationCount = 0;

        var options = new SyncOptions {
            CreateVirtualFilePlaceholders = true,
            VirtualFileCallback = (_, _, _, _) => {
                callbackInvocationCount++;
                throw new InvalidOperationException("Simulated callback failure");
            }
        };

        // Create files on the remote side
        await File.WriteAllTextAsync(Path.Combine(_remoteRootPath, "file1.txt"), "Content 1");
        await File.WriteAllTextAsync(Path.Combine(_remoteRootPath, "file2.txt"), "Content 2");

        var filter = new SyncFilter();
        var conflictResolver = new DefaultConflictResolver(ConflictResolution.UseLocal);
        using var syncEngine = new SyncEngine(_localStorage, _remoteStorage, _database, filter, conflictResolver);

        // Act
        var result = await syncEngine.SynchronizeAsync(options);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(2, callbackInvocationCount);
        Assert.True(File.Exists(Path.Combine(_localRootPath, "file1.txt")));
        Assert.True(File.Exists(Path.Combine(_localRootPath, "file2.txt")));
    }

    [Fact]
    public async Task SynchronizeAsync_DirectoryDownload_DoesNotInvokeCallback() {
        // Arrange
        var callbackInvoked = false;

        var options = new SyncOptions {
            CreateVirtualFilePlaceholders = true,
            VirtualFileCallback = (_, _, _, _) => {
                callbackInvoked = true;
                return Task.CompletedTask;
            }
        };

        // Create only a directory on the remote side (no files)
        Directory.CreateDirectory(Path.Combine(_remoteRootPath, "subdir"));

        var filter = new SyncFilter();
        var conflictResolver = new DefaultConflictResolver(ConflictResolution.UseLocal);
        using var syncEngine = new SyncEngine(_localStorage, _remoteStorage, _database, filter, conflictResolver);

        // Act
        await syncEngine.SynchronizeAsync(options);

        // Assert - callback should not be invoked for directories
        Assert.False(callbackInvoked);
        Assert.True(Directory.Exists(Path.Combine(_localRootPath, "subdir")));
    }

    [Fact]
    public async Task SynchronizeAsync_UploadOperation_DoesNotInvokeCallback() {
        // Arrange
        var callbackInvoked = false;

        var options = new SyncOptions {
            CreateVirtualFilePlaceholders = true,
            VirtualFileCallback = (_, _, _, _) => {
                callbackInvoked = true;
                return Task.CompletedTask;
            }
        };

        // Create a file on the local side (will be uploaded, not downloaded)
        await File.WriteAllTextAsync(Path.Combine(_localRootPath, "local-file.txt"), "Local content");

        var filter = new SyncFilter();
        var conflictResolver = new DefaultConflictResolver(ConflictResolution.UseLocal);
        using var syncEngine = new SyncEngine(_localStorage, _remoteStorage, _database, filter, conflictResolver);

        // Act
        await syncEngine.SynchronizeAsync(options);

        // Assert - callback should not be invoked for uploads
        Assert.False(callbackInvoked);
    }

    [Fact]
    public async Task GetSyncPlanAsync_WithVirtualFilePlaceholders_SetsWillCreateVirtualPlaceholder() {
        // Arrange
        var options = new SyncOptions {
            CreateVirtualFilePlaceholders = true
        };

        // Create a file on the remote side (will be planned for download)
        await File.WriteAllTextAsync(Path.Combine(_remoteRootPath, "remote-file.txt"), "Remote content");

        var filter = new SyncFilter();
        var conflictResolver = new DefaultConflictResolver(ConflictResolution.UseLocal);
        using var syncEngine = new SyncEngine(_localStorage, _remoteStorage, _database, filter, conflictResolver);

        // Act
        var plan = await syncEngine.GetSyncPlanAsync(options);

        // Assert
        Assert.Single(plan.Actions);
        var action = plan.Actions[0];
        Assert.Equal(SyncActionType.Download, action.ActionType);
        Assert.True(action.WillCreateVirtualPlaceholder);
    }

    [Fact]
    public async Task GetSyncPlanAsync_WithoutVirtualFilePlaceholders_DoesNotSetWillCreateVirtualPlaceholder() {
        // Arrange
        var options = new SyncOptions {
            CreateVirtualFilePlaceholders = false
        };

        // Create a file on the remote side
        await File.WriteAllTextAsync(Path.Combine(_remoteRootPath, "remote-file.txt"), "Remote content");

        var filter = new SyncFilter();
        var conflictResolver = new DefaultConflictResolver(ConflictResolution.UseLocal);
        using var syncEngine = new SyncEngine(_localStorage, _remoteStorage, _database, filter, conflictResolver);

        // Act
        var plan = await syncEngine.GetSyncPlanAsync(options);

        // Assert
        Assert.Single(plan.Actions);
        var action = plan.Actions[0];
        Assert.Equal(SyncActionType.Download, action.ActionType);
        Assert.False(action.WillCreateVirtualPlaceholder);
    }

    [Fact]
    public async Task GetSyncPlanAsync_DirectoryDownload_DoesNotSetWillCreateVirtualPlaceholder() {
        // Arrange
        var options = new SyncOptions {
            CreateVirtualFilePlaceholders = true
        };

        // Create only a directory on the remote side
        Directory.CreateDirectory(Path.Combine(_remoteRootPath, "subdir"));

        var filter = new SyncFilter();
        var conflictResolver = new DefaultConflictResolver(ConflictResolution.UseLocal);
        using var syncEngine = new SyncEngine(_localStorage, _remoteStorage, _database, filter, conflictResolver);

        // Act
        var plan = await syncEngine.GetSyncPlanAsync(options);

        // Assert
        Assert.Single(plan.Actions);
        var action = plan.Actions[0];
        Assert.True(action.IsDirectory);
        Assert.False(action.WillCreateVirtualPlaceholder);
    }

    [Fact]
    public void SyncPlanAction_WillCreateVirtualPlaceholder_StoresCorrectly() {
        // Arrange
        var withPlaceholder = new SyncPlanAction {
            ActionType = SyncActionType.Download,
            Path = "document.pdf",
            IsDirectory = false,
            Size = 1024 * 1024,
            WillCreateVirtualPlaceholder = true
        };

        var withoutPlaceholder = new SyncPlanAction {
            ActionType = SyncActionType.Download,
            Path = "document.pdf",
            IsDirectory = false,
            Size = 1024 * 1024,
            WillCreateVirtualPlaceholder = false
        };

        // Assert
        Assert.True(withPlaceholder.WillCreateVirtualPlaceholder);
        Assert.False(withoutPlaceholder.WillCreateVirtualPlaceholder);
    }

    [Fact]
    public void SyncOptions_Clone_PreservesVirtualFileSettings() {
        // Arrange
        VirtualFileCallbackDelegate callback = (_, _, _, _) => Task.CompletedTask;
        var original = new SyncOptions {
            CreateVirtualFilePlaceholders = true,
            VirtualFileCallback = callback
        };

        // Act
        var cloned = original.Clone();

        // Assert
        Assert.True(cloned.CreateVirtualFilePlaceholders);
        Assert.Same(callback, cloned.VirtualFileCallback);
    }
}
