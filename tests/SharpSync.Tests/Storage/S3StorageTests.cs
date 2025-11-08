namespace Oire.SharpSync.Tests.Storage;

/// <summary>
/// Unit and integration tests for S3Storage
/// NOTE: Integration tests require an S3-compatible service (AWS S3 or LocalStack). Set up environment variables:
/// - S3_TEST_BUCKET: S3 bucket name
/// - S3_TEST_ACCESS_KEY: AWS access key ID
/// - S3_TEST_SECRET_KEY: AWS secret access key
/// - S3_TEST_REGION: AWS region (default: us-east-1) - optional for LocalStack
/// - S3_TEST_ENDPOINT: Custom endpoint URL (e.g., http://localhost:4566 for LocalStack)
/// - S3_TEST_PREFIX: Prefix (folder path) within bucket (default: sharpsync-tests)
/// </summary>
public class S3StorageTests: IDisposable {
    private readonly string? _testBucket;
    private readonly string? _testAccessKey;
    private readonly string? _testSecretKey;
    private readonly string _testRegion;
    private readonly string? _testEndpoint;
    private readonly string _testPrefix;
    private readonly bool _integrationTestsEnabled;
    private S3Storage? _storage;

    public S3StorageTests() {
        // Read environment variables for integration tests
        _testBucket = Environment.GetEnvironmentVariable("S3_TEST_BUCKET");
        _testAccessKey = Environment.GetEnvironmentVariable("S3_TEST_ACCESS_KEY");
        _testSecretKey = Environment.GetEnvironmentVariable("S3_TEST_SECRET_KEY");
        _testRegion = Environment.GetEnvironmentVariable("S3_TEST_REGION") ?? "us-east-1";
        _testEndpoint = Environment.GetEnvironmentVariable("S3_TEST_ENDPOINT");
        _testPrefix = Environment.GetEnvironmentVariable("S3_TEST_PREFIX") ?? "sharpsync-tests";

        _integrationTestsEnabled = !string.IsNullOrEmpty(_testBucket) &&
                                   !string.IsNullOrEmpty(_testAccessKey) &&
                                   !string.IsNullOrEmpty(_testSecretKey);
    }

    public void Dispose() {
        _storage?.Dispose();
    }

    #region Unit Tests (No S3 Service Required)

    [Fact]
    public void Constructor_AwsRegion_ValidParameters_CreatesStorage() {
        // Act
        using var storage = new S3Storage("test-bucket", "access-key", "secret-key", "us-east-1");

        // Assert
        Assert.Equal(StorageType.S3, storage.StorageType);
    }

    [Fact]
    public void Constructor_AwsRegion_EmptyBucket_ThrowsException() {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => new S3Storage("", "access-key", "secret-key"));
    }

    [Fact]
    public void Constructor_AwsRegion_EmptyAccessKey_ThrowsException() {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => new S3Storage("test-bucket", "", "secret-key"));
    }

    [Fact]
    public void Constructor_AwsRegion_EmptySecretKey_ThrowsException() {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => new S3Storage("test-bucket", "access-key", ""));
    }

    [Fact]
    public void Constructor_CustomEndpoint_ValidParameters_CreatesStorage() {
        // Arrange
        var endpoint = new Uri("http://localhost:4566");

        // Act
        using var storage = new S3Storage("test-bucket", "access-key", "secret-key", endpoint);

        // Assert
        Assert.Equal(StorageType.S3, storage.StorageType);
    }

    [Fact]
    public void Constructor_CustomEndpoint_NullEndpoint_ThrowsException() {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new S3Storage("test-bucket", "access-key", "secret-key", (Uri)null!));
    }

    [Fact]
    public void RootPath_Property_ReturnsCorrectPath() {
        // Arrange
        var prefix = "test/path";
        using var storage = new S3Storage("test-bucket", "access-key", "secret-key", prefix: prefix);

        // Assert
        Assert.Equal(prefix, storage.RootPath);
    }

    [Fact]
    public void RootPath_Property_NormalizesPath() {
        // Arrange
        var prefix = "/test/path/";
        using var storage = new S3Storage("test-bucket", "access-key", "secret-key", prefix: prefix);

        // Assert - should remove leading and trailing slashes
        Assert.Equal("test/path", storage.RootPath);
    }

    [Fact]
    public void StorageType_Property_ReturnsS3() {
        // Arrange
        using var storage = new S3Storage("test-bucket", "access-key", "secret-key");

        // Assert
        Assert.Equal(StorageType.S3, storage.StorageType);
    }

    #endregion

    #region Integration Tests (Require S3 Service)

    private void SkipIfIntegrationTestsDisabled() {
        if (!_integrationTestsEnabled) {
            throw new SkipException("Integration tests disabled. Set S3_TEST_BUCKET, S3_TEST_ACCESS_KEY, and S3_TEST_SECRET_KEY environment variables.");
        }
    }

    private S3Storage CreateStorage() {
        SkipIfIntegrationTestsDisabled();

        // Use unique prefix for each test to avoid conflicts
        var uniquePrefix = $"{_testPrefix}/{Guid.NewGuid()}";

        if (!string.IsNullOrEmpty(_testEndpoint)) {
            // Custom endpoint (LocalStack, MinIO, etc.)
            return new S3Storage(_testBucket!, _testAccessKey!, _testSecretKey!, new Uri(_testEndpoint), uniquePrefix);
        } else {
            // AWS S3
            return new S3Storage(_testBucket!, _testAccessKey!, _testSecretKey!, _testRegion, uniquePrefix);
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
    public async Task CreateDirectoryAsync_CreatesDirectoryMarker() {
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
    public async Task WriteFileAsync_CreatesObject() {
        // Arrange
        _storage = CreateStorage();
        var filePath = "test.txt";
        var content = "Hello, S3 World!";

        // Act
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content));
        await _storage.WriteFileAsync(filePath, stream);

        // Assert
        var exists = await _storage.ExistsAsync(filePath);
        Assert.True(exists);
    }

    [Fact]
    public async Task WriteFileAsync_LargeFile_UsesMultipartUpload() {
        // Arrange
        _storage = CreateStorage();
        var filePath = "large_file.bin";

        // Create a 15MB file (larger than default chunk size of 10MB)
        var largeContent = new byte[15 * 1024 * 1024];
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
    public async Task ReadFileAsync_ReturnsObjectContent() {
        // Arrange
        _storage = CreateStorage();
        var filePath = "test_read.txt";
        var content = "Hello, S3 World!";

        using var writeStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content));
        await _storage.WriteFileAsync(filePath, writeStream);

        // Act
        using var readStream = await _storage.ReadFileAsync(filePath);
        using var reader = new StreamReader(readStream);
        var readContent = await reader.ReadToEndAsync();

        // Assert
        Assert.Equal(content, readContent);
    }

    [Fact]
    public async Task ReadFileAsync_NonexistentFile_ThrowsFileNotFoundException() {
        // Arrange
        _storage = CreateStorage();
        var filePath = "nonexistent.txt";

        // Act & Assert
        await Assert.ThrowsAsync<FileNotFoundException>(async () => await _storage.ReadFileAsync(filePath));
    }

    [Fact]
    public async Task ListItemsAsync_EmptyDirectory_ReturnsEmpty() {
        // Arrange
        _storage = CreateStorage();

        // Act
        var items = await _storage.ListItemsAsync("");

        // Assert
        Assert.Empty(items);
    }

    [Fact]
    public async Task ListItemsAsync_WithFiles_ReturnsFiles() {
        // Arrange
        _storage = CreateStorage();

        // Create test files
        await CreateTestFile(_storage, "file1.txt", "Content 1");
        await CreateTestFile(_storage, "file2.txt", "Content 2");
        await CreateTestFile(_storage, "subdir/file3.txt", "Content 3");

        // Act
        var items = (await _storage.ListItemsAsync("")).ToList();

        // Assert
        Assert.Contains(items, i => i.Path == "file1.txt" && !i.IsDirectory);
        Assert.Contains(items, i => i.Path == "file2.txt" && !i.IsDirectory);
        Assert.Contains(items, i => i.Path == "subdir" && i.IsDirectory);
    }

    [Fact]
    public async Task ListItemsAsync_Subdirectory_ReturnsOnlySubdirectoryContents() {
        // Arrange
        _storage = CreateStorage();

        // Create test files
        await CreateTestFile(_storage, "root_file.txt", "Root");
        await CreateTestFile(_storage, "subdir/file1.txt", "Content 1");
        await CreateTestFile(_storage, "subdir/file2.txt", "Content 2");

        // Act
        var items = (await _storage.ListItemsAsync("subdir")).ToList();

        // Assert
        Assert.Equal(2, items.Count);
        Assert.All(items, item => Assert.False(item.IsDirectory));
        Assert.Contains(items, i => i.Path == "subdir/file1.txt");
        Assert.Contains(items, i => i.Path == "subdir/file2.txt");
    }

    [Fact]
    public async Task GetItemAsync_ExistingFile_ReturnsMetadata() {
        // Arrange
        _storage = CreateStorage();
        var filePath = "test_metadata.txt";
        var content = "Test metadata";

        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content));
        await _storage.WriteFileAsync(filePath, stream);

        // Act
        var item = await _storage.GetItemAsync(filePath);

        // Assert
        Assert.NotNull(item);
        Assert.Equal(filePath, item.Path);
        Assert.False(item.IsDirectory);
        Assert.Equal(content.Length, item.Size);
        Assert.NotNull(item.ETag);
    }

    [Fact]
    public async Task GetItemAsync_NonexistentFile_ReturnsNull() {
        // Arrange
        _storage = CreateStorage();
        var filePath = "nonexistent.txt";

        // Act
        var item = await _storage.GetItemAsync(filePath);

        // Assert
        Assert.Null(item);
    }

    [Fact]
    public async Task GetItemAsync_Directory_ReturnsDirectoryMetadata() {
        // Arrange
        _storage = CreateStorage();
        var dirPath = "test_dir";

        await _storage.CreateDirectoryAsync(dirPath);

        // Act
        var item = await _storage.GetItemAsync(dirPath);

        // Assert
        Assert.NotNull(item);
        Assert.True(item.IsDirectory);
        Assert.Equal(0, item.Size);
    }

    [Fact]
    public async Task ExistsAsync_ExistingFile_ReturnsTrue() {
        // Arrange
        _storage = CreateStorage();
        var filePath = "exists_test.txt";

        await CreateTestFile(_storage, filePath, "Test");

        // Act
        var exists = await _storage.ExistsAsync(filePath);

        // Assert
        Assert.True(exists);
    }

    [Fact]
    public async Task ExistsAsync_NonexistentFile_ReturnsFalse() {
        // Arrange
        _storage = CreateStorage();
        var filePath = "does_not_exist.txt";

        // Act
        var exists = await _storage.ExistsAsync(filePath);

        // Assert
        Assert.False(exists);
    }

    [Fact]
    public async Task DeleteAsync_ExistingFile_DeletesFile() {
        // Arrange
        _storage = CreateStorage();
        var filePath = "delete_test.txt";

        await CreateTestFile(_storage, filePath, "Test");

        // Act
        await _storage.DeleteAsync(filePath);

        // Assert
        var exists = await _storage.ExistsAsync(filePath);
        Assert.False(exists);
    }

    [Fact]
    public async Task DeleteAsync_Directory_DeletesAllContents() {
        // Arrange
        _storage = CreateStorage();
        var dirPath = "delete_dir";

        await CreateTestFile(_storage, $"{dirPath}/file1.txt", "Content 1");
        await CreateTestFile(_storage, $"{dirPath}/file2.txt", "Content 2");
        await CreateTestFile(_storage, $"{dirPath}/subdir/file3.txt", "Content 3");

        // Act
        await _storage.DeleteAsync(dirPath);

        // Assert
        var exists1 = await _storage.ExistsAsync($"{dirPath}/file1.txt");
        var exists2 = await _storage.ExistsAsync($"{dirPath}/file2.txt");
        var exists3 = await _storage.ExistsAsync($"{dirPath}/subdir/file3.txt");

        Assert.False(exists1);
        Assert.False(exists2);
        Assert.False(exists3);
    }

    [Fact]
    public async Task DeleteAsync_NonexistentFile_CompletesSuccessfully() {
        // Arrange
        _storage = CreateStorage();
        var filePath = "does_not_exist.txt";

        // Act & Assert - should not throw
        await _storage.DeleteAsync(filePath);
    }

    [Fact]
    public async Task MoveAsync_ExistingFile_MovesFile() {
        // Arrange
        _storage = CreateStorage();
        var sourcePath = "source.txt";
        var targetPath = "target.txt";
        var content = "Move test";

        await CreateTestFile(_storage, sourcePath, content);

        // Act
        await _storage.MoveAsync(sourcePath, targetPath);

        // Assert
        var sourceExists = await _storage.ExistsAsync(sourcePath);
        var targetExists = await _storage.ExistsAsync(targetPath);

        Assert.False(sourceExists);
        Assert.True(targetExists);

        // Verify content
        using var stream = await _storage.ReadFileAsync(targetPath);
        using var reader = new StreamReader(stream);
        var readContent = await reader.ReadToEndAsync();
        Assert.Equal(content, readContent);
    }

    [Fact]
    public async Task MoveAsync_NonexistentFile_ThrowsFileNotFoundException() {
        // Arrange
        _storage = CreateStorage();
        var sourcePath = "does_not_exist.txt";
        var targetPath = "target.txt";

        // Act & Assert
        await Assert.ThrowsAsync<FileNotFoundException>(async () => await _storage.MoveAsync(sourcePath, targetPath));
    }

    [Fact]
    public async Task ComputeHashAsync_ExistingFile_ReturnsHash() {
        // Arrange
        _storage = CreateStorage();
        var filePath = "hash_test.txt";
        var content = "Test hash computation";

        await CreateTestFile(_storage, filePath, content);

        // Act
        var hash = await _storage.ComputeHashAsync(filePath);

        // Assert
        Assert.NotNull(hash);
        Assert.NotEmpty(hash);

        // Verify hash is consistent
        var hash2 = await _storage.ComputeHashAsync(filePath);
        Assert.Equal(hash, hash2);
    }

    [Fact]
    public async Task ComputeHashAsync_NonexistentFile_ThrowsFileNotFoundException() {
        // Arrange
        _storage = CreateStorage();
        var filePath = "nonexistent.txt";

        // Act & Assert
        await Assert.ThrowsAsync<FileNotFoundException>(async () => await _storage.ComputeHashAsync(filePath));
    }

    [Fact]
    public async Task GetStorageInfoAsync_ReturnsInfo() {
        // Arrange
        _storage = CreateStorage();

        // Act
        var info = await _storage.GetStorageInfoAsync();

        // Assert
        Assert.NotNull(info);
        // S3 doesn't provide quota info, so these should be -1
        Assert.Equal(-1, info.TotalSpace);
        Assert.Equal(-1, info.UsedSpace);
    }

    [Fact]
    public async Task ProgressChanged_LargeFileUpload_RaisesEvents() {
        // Arrange
        _storage = CreateStorage();
        var filePath = "progress_test.bin";

        // Create a 12MB file (larger than chunk size)
        var content = new byte[12 * 1024 * 1024];
        new Random().NextBytes(content);

        var progressEvents = new List<StorageProgressEventArgs>();
        _storage.ProgressChanged += (sender, args) => progressEvents.Add(args);

        // Act
        using var stream = new MemoryStream(content);
        await _storage.WriteFileAsync(filePath, stream);

        // Assert
        Assert.NotEmpty(progressEvents);
        Assert.All(progressEvents, evt => {
            Assert.Equal(StorageOperation.Upload, evt.Operation);
            Assert.Equal(filePath, evt.Path);
        });

        // Should have final 100% progress
        var finalEvent = progressEvents.LastOrDefault();
        Assert.NotNull(finalEvent);
        Assert.Equal(100, finalEvent.PercentComplete);
    }

    [Fact]
    public async Task ProgressChanged_LargeFileDownload_RaisesEvents() {
        // Arrange
        _storage = CreateStorage();
        var filePath = "download_progress_test.bin";

        // Create a 12MB file
        var content = new byte[12 * 1024 * 1024];
        new Random().NextBytes(content);

        using (var stream = new MemoryStream(content)) {
            await _storage.WriteFileAsync(filePath, stream);
        }

        var progressEvents = new List<StorageProgressEventArgs>();
        _storage.ProgressChanged += (sender, args) => progressEvents.Add(args);

        // Act
        using var readStream = await _storage.ReadFileAsync(filePath);
        var _ = readStream.ToArray(); // Force read

        // Assert
        Assert.NotEmpty(progressEvents);
        Assert.All(progressEvents, evt => {
            Assert.Equal(StorageOperation.Download, evt.Operation);
            Assert.Equal(filePath, evt.Path);
        });
    }

    #endregion

    #region Helper Methods

    private static async Task CreateTestFile(S3Storage storage, string path, string content) {
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content));
        await storage.WriteFileAsync(path, stream);
    }

    #endregion
}
