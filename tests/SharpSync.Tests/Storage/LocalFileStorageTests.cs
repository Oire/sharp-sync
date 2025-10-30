namespace Oire.SharpSync.Tests.Storage;

public class LocalFileStorageTests : IDisposable
{
    private readonly string _testDirectory;
    private readonly LocalFileStorage _storage;

    public LocalFileStorageTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), "SharpSyncTests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_testDirectory);
        _storage = new LocalFileStorage(_testDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, recursive: true);
        }
    }

    [Fact]
    public void Constructor_ValidPath_CreatesStorage()
    {
        // Assert
        Assert.Equal(StorageType.Local, _storage.StorageType);
        Assert.Equal(_testDirectory, _storage.RootPath);
    }

    [Fact]
    public void Constructor_InvalidPath_ThrowsException()
    {
        // Arrange
        var invalidPath = Path.Combine(_testDirectory, "nonexistent");

        // Act & Assert
        Assert.Throws<DirectoryNotFoundException>(() => new LocalFileStorage(invalidPath));
    }

    [Fact]
    public void Constructor_EmptyPath_ThrowsException()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => new LocalFileStorage(""));
    }

    [Fact]
    public async Task TestConnectionAsync_ReturnsTrue()
    {
        // Act
        var result = await _storage.TestConnectionAsync();

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task CreateDirectoryAsync_CreatesDirectory()
    {
        // Arrange
        var dirPath = "test/subdir";
        var fullPath = Path.Combine(_testDirectory, "test", "subdir");

        // Act
        await _storage.CreateDirectoryAsync(dirPath);

        // Assert
        Assert.True(Directory.Exists(fullPath));
    }

    [Fact]
    public async Task WriteFileAsync_CreatesFile()
    {
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
    public async Task ReadFileAsync_ReturnsFileContent()
    {
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
    public async Task ReadFileAsync_NonexistentFile_ThrowsException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<FileNotFoundException>(() => _storage.ReadFileAsync("nonexistent.txt"));
    }

    [Fact]
    public async Task ExistsAsync_ExistingFile_ReturnsTrue()
    {
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
    public async Task ExistsAsync_NonexistentFile_ReturnsFalse()
    {
        // Act
        var result = await _storage.ExistsAsync("nonexistent.txt");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task DeleteAsync_ExistingFile_DeletesFile()
    {
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
    public async Task MoveAsync_MovesFile()
    {
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
    public async Task ListItemsAsync_ReturnsItems()
    {
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
    public async Task GetItemAsync_ExistingFile_ReturnsItem()
    {
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
    public async Task ComputeHashAsync_ReturnsConsistentHash()
    {
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
    public async Task GetStorageInfoAsync_ReturnsInfo()
    {
        // Act
        var info = await _storage.GetStorageInfoAsync();

        // Assert
        Assert.NotNull(info);
        Assert.True(info.TotalSpace > 0);
        Assert.True(info.UsedSpace >= 0);
        Assert.True(info.AvailableSpace >= 0);
    }
}