namespace Oire.SharpSync.Tests.Storage;

/// <summary>
/// Unit and integration tests for FtpStorage
/// NOTE: Integration tests require a real FTP server. Set up environment variables:
/// - FTP_TEST_HOST: FTP server hostname
/// - FTP_TEST_PORT: FTP server port (default 21)
/// - FTP_TEST_USER: FTP username
/// - FTP_TEST_PASS: FTP password
/// - FTP_TEST_ROOT: Root path on server (default: /tmp/sharpsync-tests)
/// - FTP_TEST_USE_FTPS: Use FTPS/explicit SSL (default: false)
/// - FTP_TEST_USE_IMPLICIT_FTPS: Use implicit FTPS (default: false)
/// </summary>
public class FtpStorageTests : IDisposable {
    private readonly string? _testHost;
    private readonly int _testPort;
    private readonly string? _testUser;
    private readonly string? _testPass;
    private readonly string _testRoot;
    private readonly bool _useFtps;
    private readonly bool _useImplicitFtps;
    private readonly bool _integrationTestsEnabled;
    private FtpStorage? _storage;

    public FtpStorageTests() {
        // Read environment variables for integration tests
        _testHost = Environment.GetEnvironmentVariable("FTP_TEST_HOST");
        _testUser = Environment.GetEnvironmentVariable("FTP_TEST_USER");
        _testPass = Environment.GetEnvironmentVariable("FTP_TEST_PASS");
        _testRoot = Environment.GetEnvironmentVariable("FTP_TEST_ROOT") ?? "/tmp/sharpsync-tests";

        var portStr = Environment.GetEnvironmentVariable("FTP_TEST_PORT");
        _testPort = int.TryParse(portStr, out var port) ? port : 21;

        var useFtpsStr = Environment.GetEnvironmentVariable("FTP_TEST_USE_FTPS");
        _useFtps = bool.TryParse(useFtpsStr, out var useFtps) && useFtps;

        var useImplicitFtpsStr = Environment.GetEnvironmentVariable("FTP_TEST_USE_IMPLICIT_FTPS");
        _useImplicitFtps = bool.TryParse(useImplicitFtpsStr, out var useImplicitFtps) && useImplicitFtps;

        _integrationTestsEnabled = !string.IsNullOrEmpty(_testHost) &&
                                   !string.IsNullOrEmpty(_testUser) &&
                                   !string.IsNullOrEmpty(_testPass);
    }

    public void Dispose() {
        _storage?.Dispose();
    }

    #region Unit Tests (No Server Required)

    [Fact]
    public void Constructor_ValidParameters_CreatesStorage() {
        // Act
        using var storage = new FtpStorage("example.com", 21, "user", "password");

        // Assert
        Assert.Equal(StorageType.Ftp, storage.StorageType);
    }

    [Fact]
    public void Constructor_EmptyHost_ThrowsException() {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => new FtpStorage("", 21, "user", "password"));
    }

    [Fact]
    public void Constructor_InvalidPort_ThrowsException() {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => new FtpStorage("example.com", 0, "user", "password"));
        Assert.Throws<ArgumentException>(() => new FtpStorage("example.com", 70000, "user", "password"));
    }

    [Fact]
    public void Constructor_EmptyUsername_ThrowsException() {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => new FtpStorage("example.com", 21, "", "password"));
    }

    [Fact]
    public void Constructor_EmptyPassword_ThrowsException() {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => new FtpStorage("example.com", 21, "user", ""));
    }

    [Fact]
    public void RootPath_Property_ReturnsCorrectPath() {
        // Arrange
        var rootPath = "test/path";
        using var storage = new FtpStorage("example.com", 21, "user", "password", rootPath: rootPath);

        // Assert
        Assert.Equal(rootPath, storage.RootPath);
    }

    [Fact]
    public void StorageType_Property_ReturnsFtp() {
        // Arrange
        using var storage = new FtpStorage("example.com", 21, "user", "password");

        // Assert
        Assert.Equal(StorageType.Ftp, storage.StorageType);
    }

    [Fact]
    public void Constructor_WithFtps_CreatesStorage() {
        // Act
        using var storage = new FtpStorage("example.com", 21, "user", "password", useFtps: true);

        // Assert
        Assert.Equal(StorageType.Ftp, storage.StorageType);
    }

    [Fact]
    public void Constructor_WithImplicitFtps_CreatesStorage() {
        // Act
        using var storage = new FtpStorage("example.com", 990, "user", "password", useImplicitFtps: true);

        // Assert
        Assert.Equal(StorageType.Ftp, storage.StorageType);
    }

    #endregion

    #region Integration Tests (Require FTP Server)

    private void SkipIfIntegrationTestsDisabled() {
        if (!_integrationTestsEnabled) {
            throw new SkipException("Integration tests disabled. Set FTP_TEST_HOST, FTP_TEST_USER, and FTP_TEST_PASS environment variables.");
        }
    }

    private FtpStorage CreateStorage() {
        SkipIfIntegrationTestsDisabled();

        return new FtpStorage(
            _testHost!,
            _testPort,
            _testUser!,
            _testPass!,
            rootPath: $"{_testRoot}/{Guid.NewGuid()}",
            useFtps: _useFtps,
            useImplicitFtps: _useImplicitFtps);
    }

    [Fact]
    public async Task TestConnectionAsync_ValidCredentials_ReturnsTrue() {
        SkipIfIntegrationTestsDisabled();

        // Arrange
        using var storage = CreateStorage();

        // Act
        var result = await storage.TestConnectionAsync();

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task TestConnectionAsync_InvalidCredentials_ReturnsFalse() {
        SkipIfIntegrationTestsDisabled();

        // Arrange
        using var storage = new FtpStorage(_testHost!, _testPort, _testUser!, "wrong_password");

        // Act
        var result = await storage.TestConnectionAsync();

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task CreateDirectoryAsync_CreatesDirectory() {
        // Arrange
        _storage = CreateStorage();
        var dirPath = "test/subdir";

        // Act
        await _storage.CreateDirectoryAsync(dirPath);
        var exists = await _storage.ExistsAsync(dirPath);

        // Assert
        Assert.True(exists);
    }

    [Fact]
    public async Task WriteFileAsync_CreatesFile() {
        // Arrange
        _storage = CreateStorage();
        var filePath = "test.txt";
        var content = "Hello, FTP World!";

        // Act
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content));
        await _storage.WriteFileAsync(filePath, stream);

        // Assert
        var exists = await _storage.ExistsAsync(filePath);
        Assert.True(exists);
    }

    [Fact]
    public async Task ReadFileAsync_ReturnsFileContent() {
        // Arrange
        _storage = CreateStorage();
        var filePath = "test_read.txt";
        var content = "Hello, FTP World!";

        using var writeStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content));
        await _storage.WriteFileAsync(filePath, writeStream);

        // Act
        using var readStream = await _storage.ReadFileAsync(filePath);
        using var reader = new StreamReader(readStream);
        var result = await reader.ReadToEndAsync();

        // Assert
        Assert.Equal(content, result);
    }

    [Fact]
    public async Task ReadFileAsync_NonexistentFile_ThrowsException() {
        // Arrange
        _storage = CreateStorage();

        // Act & Assert
        await Assert.ThrowsAsync<FileNotFoundException>(() => _storage.ReadFileAsync("nonexistent.txt"));
    }

    [Fact]
    public async Task ExistsAsync_ExistingFile_ReturnsTrue() {
        // Arrange
        _storage = CreateStorage();
        var filePath = "exists_test.txt";
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("test"));
        await _storage.WriteFileAsync(filePath, stream);

        // Act
        var result = await _storage.ExistsAsync(filePath);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task ExistsAsync_NonexistentFile_ReturnsFalse() {
        // Arrange
        _storage = CreateStorage();

        // Act
        var result = await _storage.ExistsAsync("nonexistent.txt");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task DeleteAsync_ExistingFile_DeletesFile() {
        // Arrange
        _storage = CreateStorage();
        var filePath = "delete_test.txt";
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("test"));
        await _storage.WriteFileAsync(filePath, stream);

        // Act
        await _storage.DeleteAsync(filePath);

        // Assert
        var exists = await _storage.ExistsAsync(filePath);
        Assert.False(exists);
    }

    [Fact]
    public async Task DeleteAsync_Directory_DeletesDirectory() {
        // Arrange
        _storage = CreateStorage();
        var dirPath = "delete_dir_test";
        await _storage.CreateDirectoryAsync(dirPath);

        // Act
        await _storage.DeleteAsync(dirPath);

        // Assert
        var exists = await _storage.ExistsAsync(dirPath);
        Assert.False(exists);
    }

    [Fact]
    public async Task DeleteAsync_DirectoryWithContents_DeletesRecursively() {
        // Arrange
        _storage = CreateStorage();
        var dirPath = "delete_recursive";
        var filePath = "delete_recursive/file.txt";

        await _storage.CreateDirectoryAsync(dirPath);
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("test"));
        await _storage.WriteFileAsync(filePath, stream);

        // Act
        await _storage.DeleteAsync(dirPath);

        // Assert
        var exists = await _storage.ExistsAsync(dirPath);
        Assert.False(exists);
    }

    [Fact]
    public async Task MoveAsync_MovesFile() {
        // Arrange
        _storage = CreateStorage();
        var sourcePath = "source.txt";
        var targetPath = "target.txt";
        var content = "test content";

        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content));
        await _storage.WriteFileAsync(sourcePath, stream);

        // Act
        await _storage.MoveAsync(sourcePath, targetPath);

        // Assert
        var sourceExists = await _storage.ExistsAsync(sourcePath);
        var targetExists = await _storage.ExistsAsync(targetPath);

        Assert.False(sourceExists);
        Assert.True(targetExists);

        // Verify content
        using var readStream = await _storage.ReadFileAsync(targetPath);
        using var reader = new StreamReader(readStream);
        var result = await reader.ReadToEndAsync();
        Assert.Equal(content, result);
    }

    [Fact]
    public async Task MoveAsync_ToSubdirectory_MovesCorrectly() {
        // Arrange
        _storage = CreateStorage();
        var sourcePath = "move_source.txt";
        var targetPath = "subdir/move_target.txt";
        var content = "test content";

        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content));
        await _storage.WriteFileAsync(sourcePath, stream);

        // Act
        await _storage.MoveAsync(sourcePath, targetPath);

        // Assert
        var sourceExists = await _storage.ExistsAsync(sourcePath);
        var targetExists = await _storage.ExistsAsync(targetPath);

        Assert.False(sourceExists);
        Assert.True(targetExists);
    }

    [Fact]
    public async Task ListItemsAsync_ReturnsItems() {
        // Arrange
        _storage = CreateStorage();

        // Create test files and directories
        using var stream1 = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("content1"));
        await _storage.WriteFileAsync("file1.txt", stream1);

        await _storage.CreateDirectoryAsync("subdir");

        using var stream2 = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("content2"));
        await _storage.WriteFileAsync("subdir/file2.txt", stream2);

        // Act
        var items = await _storage.ListItemsAsync("");

        // Assert
        var itemsList = items.ToList();
        Assert.True(itemsList.Count >= 2);

        var file = itemsList.FirstOrDefault(i => i.Path.Contains("file1.txt"));
        var directory = itemsList.FirstOrDefault(i => i.Path.Contains("subdir") && i.IsDirectory);

        Assert.NotNull(file);
        Assert.NotNull(directory);
        Assert.False(file.IsDirectory);
        Assert.True(directory.IsDirectory);
    }

    [Fact]
    public async Task GetItemAsync_ExistingFile_ReturnsItem() {
        // Arrange
        _storage = CreateStorage();
        var filePath = "get_item_test.txt";
        var content = "Hello, World!";

        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content));
        await _storage.WriteFileAsync(filePath, stream);

        // Act
        var item = await _storage.GetItemAsync(filePath);

        // Assert
        Assert.NotNull(item);
        Assert.Equal(filePath, item.Path);
        Assert.False(item.IsDirectory);
        Assert.Equal(content.Length, item.Size);
    }

    [Fact]
    public async Task GetItemAsync_Directory_ReturnsDirectoryItem() {
        // Arrange
        _storage = CreateStorage();
        var dirPath = "get_dir_test";
        await _storage.CreateDirectoryAsync(dirPath);

        // Act
        var item = await _storage.GetItemAsync(dirPath);

        // Assert
        Assert.NotNull(item);
        Assert.Equal(dirPath, item.Path);
        Assert.True(item.IsDirectory);
    }

    [Fact]
    public async Task GetItemAsync_NonexistentItem_ReturnsNull() {
        // Arrange
        _storage = CreateStorage();

        // Act
        var item = await _storage.GetItemAsync("nonexistent.txt");

        // Assert
        Assert.Null(item);
    }

    [Fact]
    public async Task ComputeHashAsync_ReturnsConsistentHash() {
        // Arrange
        _storage = CreateStorage();
        var filePath = "hash_test.txt";
        var content = "Hello, World!";

        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content));
        await _storage.WriteFileAsync(filePath, stream);

        // Act
        var hash1 = await _storage.ComputeHashAsync(filePath);
        var hash2 = await _storage.ComputeHashAsync(filePath);

        // Assert
        Assert.NotNull(hash1);
        Assert.NotEmpty(hash1);
        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public async Task GetStorageInfoAsync_ReturnsStorageInfo() {
        // Arrange
        _storage = CreateStorage();

        // Act
        var info = await _storage.GetStorageInfoAsync();

        // Assert
        Assert.NotNull(info);
        // FTP doesn't support storage info, so we expect -1 values
        Assert.Equal(-1, info.TotalSpace);
        Assert.Equal(-1, info.UsedSpace);
    }

    [Fact]
    public async Task WriteFileAsync_LargeFile_SupportsProgressReporting() {
        // Arrange
        _storage = CreateStorage();
        var filePath = "large_file.bin";
        var content = new byte[15 * 1024 * 1024]; // 15MB (larger than default chunk size)
        new Random().NextBytes(content);

        var progressEvents = new List<StorageProgressEventArgs>();
        _storage.ProgressChanged += (sender, args) => progressEvents.Add(args);

        // Act
        using var stream = new MemoryStream(content);
        await _storage.WriteFileAsync(filePath, stream);

        // Assert
        var exists = await _storage.ExistsAsync(filePath);
        Assert.True(exists);

        // Verify progress events were raised
        Assert.NotEmpty(progressEvents);
        Assert.All(progressEvents, e => Assert.Equal(StorageOperation.Upload, e.Operation));
    }

    [Fact]
    public async Task ReadFileAsync_LargeFile_SupportsProgressReporting() {
        // Arrange
        _storage = CreateStorage();
        var filePath = "large_read.bin";
        var content = new byte[15 * 1024 * 1024]; // 15MB
        new Random().NextBytes(content);

        using var writeStream = new MemoryStream(content);
        await _storage.WriteFileAsync(filePath, writeStream);

        var progressEvents = new List<StorageProgressEventArgs>();
        _storage.ProgressChanged += (sender, args) => progressEvents.Add(args);

        // Act
        using var readStream = await _storage.ReadFileAsync(filePath);
        var buffer = new byte[readStream.Length];
        await readStream.ReadAsync(buffer, 0, buffer.Length);

        // Assert
        Assert.Equal(content.Length, buffer.Length);
        Assert.NotEmpty(progressEvents);
        Assert.All(progressEvents, e => Assert.Equal(StorageOperation.Download, e.Operation));
    }

    [Fact]
    public async Task WriteFileAsync_CreatesParentDirectories() {
        // Arrange
        _storage = CreateStorage();
        var filePath = "parent/child/file.txt";
        var content = "nested file";

        // Act
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content));
        await _storage.WriteFileAsync(filePath, stream);

        // Assert
        var exists = await _storage.ExistsAsync(filePath);
        Assert.True(exists);
    }

    [Fact]
    public async Task DeleteAsync_NonexistentItem_DoesNotThrow() {
        // Arrange
        _storage = CreateStorage();

        // Act & Assert - should not throw
        await _storage.DeleteAsync("nonexistent_file.txt");
    }

    [Fact]
    public async Task MoveAsync_NonexistentSource_ThrowsException() {
        // Arrange
        _storage = CreateStorage();

        // Act & Assert
        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            _storage.MoveAsync("nonexistent_source.txt", "target.txt"));
    }

    #endregion
}
