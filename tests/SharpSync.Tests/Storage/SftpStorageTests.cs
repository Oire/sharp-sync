namespace Oire.SharpSync.Tests.Storage;

/// <summary>
/// Unit and integration tests for SftpStorage
/// NOTE: Integration tests require a real SFTP server. Set up environment variables:
/// - SFTP_TEST_HOST: SFTP server hostname
/// - SFTP_TEST_PORT: SFTP server port (default 22)
/// - SFTP_TEST_USER: SFTP username
/// - SFTP_TEST_PASS: SFTP password (for password auth)
/// - SFTP_TEST_KEY: Path to private key file (for key auth)
/// - SFTP_TEST_ROOT: Root path on server (default: /tmp/sharpsync-tests)
/// </summary>
public class SftpStorageTests: IDisposable {
    private readonly string? _testHost;
    private readonly int _testPort;
    private readonly string? _testUser;
    private readonly string? _testPass;
    private readonly string? _testKey;
    private readonly string _testRoot;
    private readonly bool _integrationTestsEnabled;
    private SftpStorage? _storage;

    public SftpStorageTests() {
        // Read environment variables for integration tests
        _testHost = Environment.GetEnvironmentVariable("SFTP_TEST_HOST");
        _testUser = Environment.GetEnvironmentVariable("SFTP_TEST_USER");
        _testPass = Environment.GetEnvironmentVariable("SFTP_TEST_PASS");
        _testKey = Environment.GetEnvironmentVariable("SFTP_TEST_KEY");
        _testRoot = Environment.GetEnvironmentVariable("SFTP_TEST_ROOT") ?? "/tmp/sharpsync-tests";

        var portStr = Environment.GetEnvironmentVariable("SFTP_TEST_PORT");
        _testPort = int.TryParse(portStr, out var port) ? port : 22;

        _integrationTestsEnabled = !string.IsNullOrEmpty(_testHost) &&
                                   !string.IsNullOrEmpty(_testUser) &&
                                   (!string.IsNullOrEmpty(_testPass) || !string.IsNullOrEmpty(_testKey));
    }

    public void Dispose() {
        _storage?.Dispose();
    }

    #region Unit Tests (No Server Required)

    [Fact]
    public void Constructor_PasswordAuth_ValidParameters_CreatesStorage() {
        // Act
        using var storage = new SftpStorage("example.com", 22, "user", "password");

        // Assert
        Assert.Equal(StorageType.Sftp, storage.StorageType);
    }

    [Fact]
    public void Constructor_PasswordAuth_EmptyHost_ThrowsException() {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => new SftpStorage("", 22, "user", "password"));
    }

    [Fact]
    public void Constructor_PasswordAuth_InvalidPort_ThrowsException() {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => new SftpStorage("example.com", 0, "user", "password"));
        Assert.Throws<ArgumentException>(() => new SftpStorage("example.com", 70000, "user", "password"));
    }

    [Fact]
    public void Constructor_PasswordAuth_EmptyUsername_ThrowsException() {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => new SftpStorage("example.com", 22, "", "password"));
    }

    [Fact]
    public void Constructor_PasswordAuth_EmptyPassword_ThrowsException() {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => new SftpStorage("example.com", 22, "user", ""));
    }

    [Fact]
    public void Constructor_KeyAuth_NonexistentKeyFile_ThrowsException() {
        // Arrange
        var nonexistentKey = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".key");

        // Act & Assert
        Assert.Throws<FileNotFoundException>(() =>
            new SftpStorage("example.com", 22, "user", privateKeyPath: nonexistentKey, privateKeyPassphrase: null));
    }

    [Fact]
    public void RootPath_Property_ReturnsCorrectPath() {
        // Arrange
        var rootPath = "test/path";
        using var storage = new SftpStorage("example.com", 22, "user", password: "password", rootPath: rootPath);

        // Assert
        Assert.Equal(rootPath, storage.RootPath);
    }

    [Fact]
    public void StorageType_Property_ReturnsSftp() {
        // Arrange
        using var storage = new SftpStorage("example.com", 22, "user", "password");

        // Assert
        Assert.Equal(StorageType.Sftp, storage.StorageType);
    }

    #endregion

    #region Integration Tests (Require SFTP Server)

    private void SkipIfIntegrationTestsDisabled() {
        if (!_integrationTestsEnabled) {
            throw new SkipException("Integration tests disabled. Set SFTP_TEST_HOST, SFTP_TEST_USER, and SFTP_TEST_PASS environment variables.");
        }
    }

    private SftpStorage CreateStorage() {
        SkipIfIntegrationTestsDisabled();

        if (!string.IsNullOrEmpty(_testKey)) {
            // Key-based authentication
            return new SftpStorage(_testHost!, _testPort, _testUser!, privateKeyPath: _testKey, privateKeyPassphrase: null, rootPath: $"{_testRoot}/{Guid.NewGuid()}");
        } else {
            // Password authentication
            return new SftpStorage(_testHost!, _testPort, _testUser!, password: _testPass!, rootPath: $"{_testRoot}/{Guid.NewGuid()}");
        }
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
        using var storage = new SftpStorage(_testHost!, _testPort, _testUser!, "wrong_password");

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
        var content = "Hello, SFTP World!";

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
        var content = "Hello, SFTP World!";

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
        Assert.True(item.IsDirectory);
        Assert.Equal(dirPath, item.Path);
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
    public async Task ComputeHashAsync_DifferentContent_DifferentHashes() {
        // Arrange
        _storage = CreateStorage();
        var file1 = "hash1.txt";
        var file2 = "hash2.txt";

        using var stream1 = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("content 1"));
        await _storage.WriteFileAsync(file1, stream1);

        using var stream2 = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("content 2"));
        await _storage.WriteFileAsync(file2, stream2);

        // Act
        var hash1 = await _storage.ComputeHashAsync(file1);
        var hash2 = await _storage.ComputeHashAsync(file2);

        // Assert
        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public async Task WriteFileAsync_LargeFile_HandlesCorrectly() {
        // Arrange
        _storage = CreateStorage();
        var filePath = "large.bin";
        var largeContent = new byte[5 * 1024 * 1024]; // 5 MB
        new Random().NextBytes(largeContent);

        // Act
        using var stream = new MemoryStream(largeContent);
        await _storage.WriteFileAsync(filePath, stream);

        // Assert
        var exists = await _storage.ExistsAsync(filePath);
        Assert.True(exists);

        var item = await _storage.GetItemAsync(filePath);
        Assert.NotNull(item);
        Assert.Equal(largeContent.Length, item.Size);
    }

    [Fact]
    public async Task ReadFileAsync_LargeFile_ReadsCorrectly() {
        // Arrange
        _storage = CreateStorage();
        var filePath = "large_read.bin";
        var largeContent = new byte[5 * 1024 * 1024]; // 5 MB
        new Random().NextBytes(largeContent);

        using var writeStream = new MemoryStream(largeContent);
        await _storage.WriteFileAsync(filePath, writeStream);

        // Act
        using var readStream = await _storage.ReadFileAsync(filePath);
        using var memoryStream = new MemoryStream();
        await readStream.CopyToAsync(memoryStream);
        var readContent = memoryStream.ToArray();

        // Assert
        Assert.Equal(largeContent.Length, readContent.Length);
        Assert.Equal(largeContent, readContent);
    }

    [Fact]
    public async Task WriteFileAsync_EmptyFile_CreatesEmptyFile() {
        // Arrange
        _storage = CreateStorage();
        var filePath = "empty.txt";

        // Act
        using var stream = new MemoryStream();
        await _storage.WriteFileAsync(filePath, stream);

        // Assert
        var exists = await _storage.ExistsAsync(filePath);
        Assert.True(exists);

        var item = await _storage.GetItemAsync(filePath);
        Assert.NotNull(item);
        Assert.Equal(0, item.Size);
    }

    [Fact]
    public async Task WriteFileAsync_OverwritesExistingFile() {
        // Arrange
        _storage = CreateStorage();
        var filePath = "overwrite.txt";

        using var originalStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("original content"));
        await _storage.WriteFileAsync(filePath, originalStream);

        // Act
        var newContent = "new content";
        using var newStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(newContent));
        await _storage.WriteFileAsync(filePath, newStream);

        // Assert
        using var readStream = await _storage.ReadFileAsync(filePath);
        using var reader = new StreamReader(readStream);
        var result = await reader.ReadToEndAsync();
        Assert.Equal(newContent, result);
    }

    [Fact]
    public async Task CreateDirectoryAsync_AlreadyExists_DoesNotThrow() {
        // Arrange
        _storage = CreateStorage();
        var dirPath = "existing_dir";
        await _storage.CreateDirectoryAsync(dirPath);

        // Act & Assert - should not throw
        await _storage.CreateDirectoryAsync(dirPath);

        // Verify it still exists
        var exists = await _storage.ExistsAsync(dirPath);
        Assert.True(exists);
    }

    [Fact]
    public async Task ListItemsAsync_EmptyDirectory_ReturnsEmpty() {
        // Arrange
        _storage = CreateStorage();
        var subdir = "empty_subdir";
        await _storage.CreateDirectoryAsync(subdir);

        // Act
        var items = await _storage.ListItemsAsync(subdir);

        // Assert
        Assert.Empty(items);
    }

    [Fact]
    public async Task GetStorageInfoAsync_ReturnsInfo() {
        // Arrange
        _storage = CreateStorage();

        // Act
        var info = await _storage.GetStorageInfoAsync();

        // Assert
        Assert.NotNull(info);
        // SFTP doesn't always support storage info, so we just verify it doesn't throw
    }

    [Fact]
    public async Task ProgressChanged_LargeFile_RaisesEvents() {
        // Arrange
        _storage = CreateStorage();
        var filePath = "progress_test.bin";
        var largeContent = new byte[15 * 1024 * 1024]; // 15 MB (larger than chunk size)
        new Random().NextBytes(largeContent);

        var progressEvents = new List<StorageProgressEventArgs>();
        _storage.ProgressChanged += (sender, args) => progressEvents.Add(args);

        // Act
        using var stream = new MemoryStream(largeContent);
        await _storage.WriteFileAsync(filePath, stream);

        // Assert
        Assert.NotEmpty(progressEvents);
        Assert.All(progressEvents, e => Assert.Equal(StorageOperation.Upload, e.Operation));
        Assert.All(progressEvents, e => Assert.Equal(filePath, e.Path));
    }

    [Theory]
    [InlineData("file with spaces.txt")]
    [InlineData("file-with-dashes.txt")]
    [InlineData("file_with_underscores.txt")]
    [InlineData("file.multiple.dots.txt")]
    public async Task WriteFileAsync_SpecialFileNames_HandlesCorrectly(string fileName) {
        // Arrange
        _storage = CreateStorage();
        var content = "test content";

        // Act
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content));
        await _storage.WriteFileAsync(fileName, stream);

        // Assert
        var exists = await _storage.ExistsAsync(fileName);
        Assert.True(exists);
    }

    [Fact]
    public async Task GetItemAsync_IncludesPermissions() {
        // Arrange
        _storage = CreateStorage();
        var filePath = "permissions_test.txt";

        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("test"));
        await _storage.WriteFileAsync(filePath, stream);

        // Act
        var item = await _storage.GetItemAsync(filePath);

        // Assert
        Assert.NotNull(item);
        Assert.NotNull(item.Permissions);
        Assert.NotEmpty(item.Permissions);
        // Permissions should be in format like "-rw-r--r--"
        Assert.Equal(10, item.Permissions.Length);
    }

    #endregion
}

/// <summary>
/// Exception to indicate test should be skipped
/// </summary>
public class SkipException: Exception {
    public SkipException(string message) : base(message) { }
}
