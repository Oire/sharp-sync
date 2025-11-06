namespace Oire.SharpSync.Tests.Storage;

public class LocalFileStorageTests: IDisposable {
    private readonly string _testDirectory;
    private readonly LocalFileStorage _storage;

    public LocalFileStorageTests() {
        _testDirectory = Path.Combine(Path.GetTempPath(), "SharpSyncTests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_testDirectory);
        _storage = new LocalFileStorage(_testDirectory);
    }

    public void Dispose() {
        if (Directory.Exists(_testDirectory)) {
            Directory.Delete(_testDirectory, recursive: true);
        }
    }

    [Fact]
    public void Constructor_ValidPath_CreatesStorage() {
        // Assert
        Assert.Equal(StorageType.Local, _storage.StorageType);
        Assert.Equal(_testDirectory, _storage.RootPath);
    }

    [Fact]
    public void Constructor_InvalidPath_ThrowsException() {
        // Arrange
        var invalidPath = Path.Combine(_testDirectory, "nonexistent");

        // Act & Assert
        Assert.Throws<DirectoryNotFoundException>(() => new LocalFileStorage(invalidPath));
    }

    [Fact]
    public void Constructor_EmptyPath_ThrowsException() {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => new LocalFileStorage(""));
    }

    [Fact]
    public async Task TestConnectionAsync_ReturnsTrue() {
        // Act
        var result = await _storage.TestConnectionAsync();

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task CreateDirectoryAsync_CreatesDirectory() {
        // Arrange
        var dirPath = "test/subdir";
        var fullPath = Path.Combine(_testDirectory, "test", "subdir");

        // Act
        await _storage.CreateDirectoryAsync(dirPath);

        // Assert
        Assert.True(Directory.Exists(fullPath));
    }

    [Fact]
    public async Task WriteFileAsync_CreatesFile() {
        // Arrange
        var filePath = "test.txt";
        var content = "Hello, World!";
        var fullPath = Path.Combine(_testDirectory, filePath);

        // Act
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content));
        await _storage.WriteFileAsync(filePath, stream);

        // Assert
        Assert.True(File.Exists(fullPath));
        var fileContent = await File.ReadAllTextAsync(fullPath);
        Assert.Equal(content, fileContent);
    }

    [Fact]
    public async Task ReadFileAsync_ReturnsFileContent() {
        // Arrange
        var filePath = "test.txt";
        var content = "Hello, World!";
        var fullPath = Path.Combine(_testDirectory, filePath);
        await File.WriteAllTextAsync(fullPath, content);

        // Act
        using var stream = await _storage.ReadFileAsync(filePath);
        using var reader = new StreamReader(stream);
        var result = await reader.ReadToEndAsync();

        // Assert
        Assert.Equal(content, result);
    }

    [Fact]
    public async Task ReadFileAsync_NonexistentFile_ThrowsException() {
        // Act & Assert
        await Assert.ThrowsAsync<FileNotFoundException>(() => _storage.ReadFileAsync("nonexistent.txt"));
    }

    [Fact]
    public async Task ExistsAsync_ExistingFile_ReturnsTrue() {
        // Arrange
        var filePath = "test.txt";
        var fullPath = Path.Combine(_testDirectory, filePath);
        await File.WriteAllTextAsync(fullPath, "test");

        // Act
        var result = await _storage.ExistsAsync(filePath);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task ExistsAsync_NonexistentFile_ReturnsFalse() {
        // Act
        var result = await _storage.ExistsAsync("nonexistent.txt");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task DeleteAsync_ExistingFile_DeletesFile() {
        // Arrange
        var filePath = "test.txt";
        var fullPath = Path.Combine(_testDirectory, filePath);
        await File.WriteAllTextAsync(fullPath, "test");

        // Act
        await _storage.DeleteAsync(filePath);

        // Assert
        Assert.False(File.Exists(fullPath));
    }

    [Fact]
    public async Task MoveAsync_MovesFile() {
        // Arrange
        var sourcePath = "source.txt";
        var targetPath = "target.txt";
        var sourceFullPath = Path.Combine(_testDirectory, sourcePath);
        var targetFullPath = Path.Combine(_testDirectory, targetPath);
        var content = "test content";
        await File.WriteAllTextAsync(sourceFullPath, content);

        // Act
        await _storage.MoveAsync(sourcePath, targetPath);

        // Assert
        Assert.False(File.Exists(sourceFullPath));
        Assert.True(File.Exists(targetFullPath));
        var targetContent = await File.ReadAllTextAsync(targetFullPath);
        Assert.Equal(content, targetContent);
    }

    [Fact]
    public async Task ListItemsAsync_ReturnsItems() {
        // Arrange
        var testFile = Path.Combine(_testDirectory, "file1.txt");
        var testDir = Path.Combine(_testDirectory, "subdir");
        var testFileInDir = Path.Combine(testDir, "file2.txt");

        await File.WriteAllTextAsync(testFile, "content1");
        Directory.CreateDirectory(testDir);
        await File.WriteAllTextAsync(testFileInDir, "content2");

        // Act
        var items = await _storage.ListItemsAsync("");

        // Assert
        var itemsList = items.ToList();
        Assert.Equal(2, itemsList.Count);

        var file = itemsList.First(i => !i.IsDirectory);
        var directory = itemsList.First(i => i.IsDirectory);

        Assert.Equal("file1.txt", file.Path);
        Assert.Equal("subdir", directory.Path);
        Assert.True(directory.IsDirectory);
        Assert.False(file.IsDirectory);
    }

    [Fact]
    public async Task GetItemAsync_ExistingFile_ReturnsItem() {
        // Arrange
        var filePath = "test.txt";
        var content = "Hello, World!";
        var fullPath = Path.Combine(_testDirectory, filePath);
        await File.WriteAllTextAsync(fullPath, content);

        // Act
        var item = await _storage.GetItemAsync(filePath);

        // Assert
        Assert.NotNull(item);
        Assert.Equal(filePath, item.Path);
        Assert.False(item.IsDirectory);
        Assert.Equal(content.Length, item.Size);
        Assert.Equal("text/plain", item.MimeType);
    }

    [Fact]
    public async Task ComputeHashAsync_ReturnsConsistentHash() {
        // Arrange
        var filePath = "test.txt";
        var content = "Hello, World!";
        var fullPath = Path.Combine(_testDirectory, filePath);
        await File.WriteAllTextAsync(fullPath, content);

        // Act
        var hash1 = await _storage.ComputeHashAsync(filePath);
        var hash2 = await _storage.ComputeHashAsync(filePath);

        // Assert
        Assert.NotNull(hash1);
        Assert.NotEmpty(hash1);
        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public async Task GetStorageInfoAsync_ReturnsInfo() {
        // Act
        var info = await _storage.GetStorageInfoAsync();

        // Assert
        Assert.NotNull(info);
        Assert.True(info.TotalSpace > 0);
        Assert.True(info.UsedSpace >= 0);
        Assert.True(info.AvailableSpace >= 0);
    }

    [Fact]
    public async Task WriteFileAsync_LargeFile_HandlesCorrectly() {
        // Arrange
        var filePath = "large.bin";
        var largeContent = new byte[5 * 1024 * 1024]; // 5 MB
        new Random().NextBytes(largeContent);

        // Act
        using var stream = new MemoryStream(largeContent);
        await _storage.WriteFileAsync(filePath, stream);

        // Assert
        var fullPath = Path.Combine(_testDirectory, filePath);
        Assert.True(File.Exists(fullPath));
        var fileInfo = new FileInfo(fullPath);
        Assert.Equal(largeContent.Length, fileInfo.Length);
    }

    [Fact]
    public async Task WriteFileAsync_EmptyFile_CreatesEmptyFile() {
        // Arrange
        var filePath = "empty.txt";
        using var stream = new MemoryStream();

        // Act
        await _storage.WriteFileAsync(filePath, stream);

        // Assert
        var fullPath = Path.Combine(_testDirectory, filePath);
        Assert.True(File.Exists(fullPath));
        var fileInfo = new FileInfo(fullPath);
        Assert.Equal(0, fileInfo.Length);
    }

    [Fact]
    public async Task WriteFileAsync_OverwritesExistingFile() {
        // Arrange
        var filePath = "overwrite.txt";
        var fullPath = Path.Combine(_testDirectory, filePath);
        await File.WriteAllTextAsync(fullPath, "original content");

        // Act
        var newContent = "new content";
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(newContent));
        await _storage.WriteFileAsync(filePath, stream);

        // Assert
        var fileContent = await File.ReadAllTextAsync(fullPath);
        Assert.Equal(newContent, fileContent);
    }

    [Fact]
    public async Task WriteFileAsync_CreatesIntermediateDirectories() {
        // Arrange
        var filePath = "deeply/nested/directory/file.txt";
        var content = "test content";

        // Act
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content));
        await _storage.WriteFileAsync(filePath, stream);

        // Assert
        var fullPath = Path.Combine(_testDirectory, filePath);
        Assert.True(File.Exists(fullPath));
        var fileContent = await File.ReadAllTextAsync(fullPath);
        Assert.Equal(content, fileContent);
    }

    [Fact]
    public async Task ReadFileAsync_LargeFile_ReadsCorrectly() {
        // Arrange
        var filePath = "large_read.bin";
        var largeContent = new byte[5 * 1024 * 1024]; // 5 MB
        new Random().NextBytes(largeContent);
        var fullPath = Path.Combine(_testDirectory, filePath);
        await File.WriteAllBytesAsync(fullPath, largeContent);

        // Act
        using var stream = await _storage.ReadFileAsync(filePath);
        using var memoryStream = new MemoryStream();
        await stream.CopyToAsync(memoryStream);
        var readContent = memoryStream.ToArray();

        // Assert
        Assert.Equal(largeContent.Length, readContent.Length);
        Assert.Equal(largeContent, readContent);
    }

    [Fact]
    public async Task ListItemsAsync_EmptyDirectory_ReturnsEmpty() {
        // Arrange
        var subdir = "empty_subdir";
        Directory.CreateDirectory(Path.Combine(_testDirectory, subdir));

        // Act
        var items = await _storage.ListItemsAsync(subdir);

        // Assert
        Assert.Empty(items);
    }

    [Fact]
    public async Task ListItemsAsync_WithSubdirectories_ListsImmediateItems() {
        // Arrange
        var dir1 = Path.Combine(_testDirectory, "dir1");
        var dir2 = Path.Combine(dir1, "dir2");
        Directory.CreateDirectory(dir1);
        Directory.CreateDirectory(dir2);

        await File.WriteAllTextAsync(Path.Combine(_testDirectory, "root.txt"), "root");
        await File.WriteAllTextAsync(Path.Combine(dir1, "file1.txt"), "file1");
        await File.WriteAllTextAsync(Path.Combine(dir2, "file2.txt"), "file2");

        // Act
        var allItems = await _storage.ListItemsAsync("");

        // Assert
        var itemsList = allItems.ToList();
        Assert.True(itemsList.Count >= 2); // At least root.txt and dir1 (not recursive)
    }

    [Fact]
    public async Task DeleteAsync_Directory_DeletesDirectory() {
        // Arrange
        var dirPath = "test_delete_dir";
        var fullPath = Path.Combine(_testDirectory, dirPath);
        Directory.CreateDirectory(fullPath);

        // Act
        await _storage.DeleteAsync(dirPath);

        // Assert
        Assert.False(Directory.Exists(fullPath));
    }

    [Fact]
    public async Task DeleteAsync_NonexistentItem_DoesNotThrow() {
        // Act & Assert - should not throw
        await _storage.DeleteAsync("nonexistent.txt");
    }

    [Fact]
    public async Task MoveAsync_ToSubdirectory_MovesCorrectly() {
        // Arrange
        var sourcePath = "source.txt";
        var targetPath = "subdir/target.txt";
        var sourceFullPath = Path.Combine(_testDirectory, sourcePath);
        var content = "test content";
        await File.WriteAllTextAsync(sourceFullPath, content);

        // Act
        await _storage.MoveAsync(sourcePath, targetPath);

        // Assert
        Assert.False(File.Exists(sourceFullPath));
        var targetFullPath = Path.Combine(_testDirectory, targetPath);
        Assert.True(File.Exists(targetFullPath));
        var targetContent = await File.ReadAllTextAsync(targetFullPath);
        Assert.Equal(content, targetContent);
    }

    [Fact]
    public async Task GetItemAsync_Directory_ReturnsDirectoryItem() {
        // Arrange
        var dirPath = "test_dir";
        var fullPath = Path.Combine(_testDirectory, dirPath);
        Directory.CreateDirectory(fullPath);

        // Act
        var item = await _storage.GetItemAsync(dirPath);

        // Assert
        Assert.NotNull(item);
        Assert.True(item.IsDirectory);
        Assert.Equal(dirPath, item.Path);
    }

    [Fact]
    public async Task GetItemAsync_NonexistentItem_ReturnsNull() {
        // Act
        var item = await _storage.GetItemAsync("nonexistent.txt");

        // Assert
        Assert.Null(item);
    }

    [Fact]
    public async Task ComputeHashAsync_DifferentContent_DifferentHashes() {
        // Arrange
        var file1 = "file1.txt";
        var file2 = "file2.txt";
        await File.WriteAllTextAsync(Path.Combine(_testDirectory, file1), "content 1");
        await File.WriteAllTextAsync(Path.Combine(_testDirectory, file2), "content 2");

        // Act
        var hash1 = await _storage.ComputeHashAsync(file1);
        var hash2 = await _storage.ComputeHashAsync(file2);

        // Assert
        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public async Task ComputeHashAsync_SameContent_SameHashes() {
        // Arrange
        var file1 = "file1.txt";
        var file2 = "file2.txt";
        var content = "same content";
        await File.WriteAllTextAsync(Path.Combine(_testDirectory, file1), content);
        await File.WriteAllTextAsync(Path.Combine(_testDirectory, file2), content);

        // Act
        var hash1 = await _storage.ComputeHashAsync(file1);
        var hash2 = await _storage.ComputeHashAsync(file2);

        // Assert
        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public async Task ComputeHashAsync_NonexistentFile_ThrowsException() {
        // Act & Assert
        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            _storage.ComputeHashAsync("nonexistent.txt"));
    }

    [Fact]
    public async Task ExistsAsync_Directory_ReturnsTrue() {
        // Arrange
        var dirPath = "test_exists_dir";
        Directory.CreateDirectory(Path.Combine(_testDirectory, dirPath));

        // Act
        var result = await _storage.ExistsAsync(dirPath);

        // Assert
        Assert.True(result);
    }

    [Theory]
    [InlineData("file with spaces.txt")]
    [InlineData("file-with-dashes.txt")]
    [InlineData("file_with_underscores.txt")]
    [InlineData("file.multiple.dots.txt")]
    public async Task WriteFileAsync_SpecialFileNames_HandlesCorrectly(string fileName) {
        // Arrange
        var content = "test content";

        // Act
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content));
        await _storage.WriteFileAsync(fileName, stream);

        // Assert
        var fullPath = Path.Combine(_testDirectory, fileName);
        Assert.True(File.Exists(fullPath));
    }

    [Fact]
    public async Task CreateDirectoryAsync_AlreadyExists_DoesNotThrow() {
        // Arrange
        var dirPath = "existing_dir";
        await _storage.CreateDirectoryAsync(dirPath);

        // Act & Assert - should not throw
        await _storage.CreateDirectoryAsync(dirPath);

        // Verify it still exists
        var fullPath = Path.Combine(_testDirectory, dirPath);
        Assert.True(Directory.Exists(fullPath));
    }

    [Fact]
    public async Task GetItemAsync_WithMetadata_IncludesMetadata() {
        // Arrange
        var filePath = "metadata.txt";
        var content = "test content";
        var fullPath = Path.Combine(_testDirectory, filePath);
        await File.WriteAllTextAsync(fullPath, content);

        // Act
        var item = await _storage.GetItemAsync(filePath);

        // Assert
        Assert.NotNull(item);
        Assert.NotEqual(default(DateTime), item.LastModified);
        Assert.True(item.Size > 0);
        // Hash is not computed by GetItemAsync, use ComputeHashAsync separately
    }

    [Theory]
    [InlineData(".txt", "text/plain")]
    [InlineData(".json", "application/json")]
    [InlineData(".xml", "application/xml")]
    [InlineData(".pdf", "application/pdf")]
    [InlineData(".jpg", "image/jpeg")]
    [InlineData(".png", "image/png")]
    public async Task GetItemAsync_MimeTypes_DetectsCorrectly(string extension, string expectedMimeType) {
        // Arrange
        var fileName = $"test{extension}";
        var fullPath = Path.Combine(_testDirectory, fileName);
        await File.WriteAllTextAsync(fullPath, "content");

        // Act
        var item = await _storage.GetItemAsync(fileName);

        // Assert
        Assert.NotNull(item);
        Assert.Equal(expectedMimeType, item.MimeType);
    }

    [Fact]
    public void RootPath_Property_ReturnsCorrectPath() {
        // Assert
        Assert.Equal(_testDirectory, _storage.RootPath);
    }

    [Fact]
    public void StorageType_Property_ReturnsLocal() {
        // Assert
        Assert.Equal(StorageType.Local, _storage.StorageType);
    }
}
