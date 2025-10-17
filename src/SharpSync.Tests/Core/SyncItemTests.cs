namespace Oire.SharpSync.Tests.Core;

public class SyncItemTests
{
    [Fact]
    public void SyncItem_DefaultConstructor_SetsDefaults()
    {
        // Arrange & Act
        var item = new SyncItem();

        // Assert
        Assert.Equal(string.Empty, item.Path);
        Assert.False(item.IsDirectory);
        Assert.Equal(0, item.Size);
        Assert.Equal(default(DateTime), item.LastModified);
        Assert.Null(item.Hash);
        Assert.Null(item.ETag);
        Assert.NotNull(item.Metadata);
        Assert.Empty(item.Metadata);
    }

    [Fact]
    public void SyncItem_Properties_CanBeSetAndRetrieved()
    {
        // Arrange
        var item = new SyncItem();
        var lastModified = DateTime.UtcNow;

        // Act
        item.Path = "test/file.txt";
        item.IsDirectory = false;
        item.Size = 1024;
        item.LastModified = lastModified;
        item.Hash = "abc123";
        item.ETag = "etag123";
        item.MimeType = "text/plain";
        item.Permissions = "644";
        item.Metadata["custom"] = "value";

        // Assert
        Assert.Equal("test/file.txt", item.Path);
        Assert.False(item.IsDirectory);
        Assert.Equal(1024, item.Size);
        Assert.Equal(lastModified, item.LastModified);
        Assert.Equal("abc123", item.Hash);
        Assert.Equal("etag123", item.ETag);
        Assert.Equal("text/plain", item.MimeType);
        Assert.Equal("644", item.Permissions);
        Assert.Equal("value", item.Metadata["custom"]);
    }

    [Fact]
    public void SyncItem_Directory_HasZeroSize()
    {
        // Arrange & Act
        var item = new SyncItem
        {
            Path = "test/directory",
            IsDirectory = true,
            Size = 0
        };

        // Assert
        Assert.True(item.IsDirectory);
        Assert.Equal(0, item.Size);
    }
}