using Moq;
using Oire.SharpSync.Core;
using Oire.SharpSync.Storage;

namespace Oire.SharpSync.Tests.Fixtures;

public static class MockStorageFactory
{
    public static Mock<ISyncStorage> CreateMockStorage(
        string? rootPath = null,
        bool supportsETag = true,
        bool supportsChunking = false)
    {
        var mock = new Mock<ISyncStorage>();
        
        mock.Setup(x => x.StorageType)
            .Returns(StorageType.Local);
        
        mock.Setup(x => x.RootPath)
            .Returns(rootPath ?? TestConstants.TestLocalPath);
        
        // Default setup for common operations
        mock.Setup(x => x.ExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        
        mock.Setup(x => x.GetItemAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SyncItem?)null);
        
        mock.Setup(x => x.ListItemsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SyncItem>());
        
        mock.Setup(x => x.CreateDirectoryAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        
        mock.Setup(x => x.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        
        return mock;
    }

    public static Mock<ISyncDatabase> CreateMockDatabase()
    {
        var mock = new Mock<ISyncDatabase>();
        
        mock.Setup(x => x.GetSyncStateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SyncState?)null);
        
        mock.Setup(x => x.GetAllSyncStatesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SyncState>());
        
        mock.Setup(x => x.UpdateSyncStateAsync(It.IsAny<SyncState>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        
        mock.Setup(x => x.DeleteSyncStateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        
        mock.Setup(x => x.BeginTransactionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Mock.Of<ISyncTransaction>());
        
        return mock;
    }

    public static Mock<IConflictResolver> CreateMockConflictResolver(
        ConflictResolution defaultResolution = ConflictResolution.UseLocal)
    {
        var mock = new Mock<IConflictResolver>();
        
        mock.Setup(x => x.ResolveConflictAsync(It.IsAny<FileConflictEventArgs>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(defaultResolution);
        
        return mock;
    }

    public static Mock<ISyncFilter> CreateMockSyncFilter(
        bool defaultInclude = true)
    {
        var mock = new Mock<ISyncFilter>();
        
        mock.Setup(x => x.ShouldSync(It.IsAny<string>()))
            .Returns(defaultInclude);
        
        mock.Setup(x => x.AddExclusionPattern(It.IsAny<string>()));
        mock.Setup(x => x.AddInclusionPattern(It.IsAny<string>()));
        mock.Setup(x => x.Clear());
        
        return mock;
    }
}