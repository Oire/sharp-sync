namespace Oire.SharpSync.Tests.Core;

public class SyncStateTests
{
    [Fact]
    public void SyncState_DefaultConstructor_SetsDefaults()
    {
        // Arrange & Act
        var state = new SyncState();

        // Assert
        Assert.Equal(0, state.Id);
        Assert.Equal(string.Empty, state.Path);
        Assert.False(state.IsDirectory);
        Assert.Null(state.LocalHash);
        Assert.Null(state.RemoteHash);
        Assert.Null(state.LocalModified);
        Assert.Null(state.RemoteModified);
        Assert.Equal(0, state.LocalSize);
        Assert.Equal(0, state.RemoteSize);
        Assert.Equal(SyncStatus.Synced, state.Status);
        Assert.Null(state.LastSyncTime);
        Assert.Null(state.ETag);
        Assert.Null(state.ErrorMessage);
        Assert.Equal(0, state.SyncAttempts);
    }

    [Fact]
    public void SyncState_Properties_CanBeSetAndRetrieved()
    {
        // Arrange
        var state = new SyncState();
        var now = DateTime.UtcNow;

        // Act
        state.Id = 123;
        state.Path = "test/file.txt";
        state.IsDirectory = false;
        state.LocalHash = "local123";
        state.RemoteHash = "remote456";
        state.LocalModified = now;
        state.RemoteModified = now.AddMinutes(-5);
        state.LocalSize = 1024;
        state.RemoteSize = 2048;
        state.Status = SyncStatus.Conflict;
        state.LastSyncTime = now.AddHours(-1);
        state.ETag = "etag789";
        state.ErrorMessage = "Test error";
        state.SyncAttempts = 3;

        // Assert
        Assert.Equal(123, state.Id);
        Assert.Equal("test/file.txt", state.Path);
        Assert.False(state.IsDirectory);
        Assert.Equal("local123", state.LocalHash);
        Assert.Equal("remote456", state.RemoteHash);
        Assert.Equal(now, state.LocalModified);
        Assert.Equal(now.AddMinutes(-5), state.RemoteModified);
        Assert.Equal(1024, state.LocalSize);
        Assert.Equal(2048, state.RemoteSize);
        Assert.Equal(SyncStatus.Conflict, state.Status);
        Assert.Equal(now.AddHours(-1), state.LastSyncTime);
        Assert.Equal("etag789", state.ETag);
        Assert.Equal("Test error", state.ErrorMessage);
        Assert.Equal(3, state.SyncAttempts);
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
    public void SyncState_Status_SupportsAllValues(SyncStatus status)
    {
        // Arrange & Act
        var state = new SyncState { Status = status };

        // Assert
        Assert.Equal(status, state.Status);
    }
}