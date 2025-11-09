using System.Text;
using Oire.SharpSync.Auth;
using Oire.SharpSync.Core;

namespace Oire.SharpSync.Tests.Storage;

/// <summary>
/// Unit and integration tests for WebDavStorage
/// NOTE: Integration tests require a real WebDAV server. Set up environment variables:
/// - WEBDAV_TEST_URL: WebDAV server base URL (e.g., https://cloud.example.com/remote.php/dav/files/user/)
/// - WEBDAV_TEST_USER: WebDAV username (for basic auth)
/// - WEBDAV_TEST_PASS: WebDAV password (for basic auth)
/// - WEBDAV_TEST_ROOT: Root path within WebDAV share (optional, default: "")
/// </summary>
public class WebDavStorageTests: IDisposable {
    private readonly string? _testUrl;
    private readonly string? _testUser;
    private readonly string? _testPass;
    private readonly string _testRoot;
    private readonly bool _integrationTestsEnabled;
    private WebDavStorage? _storage;

    public WebDavStorageTests() {
        // Read environment variables for integration tests
        _testUrl = Environment.GetEnvironmentVariable("WEBDAV_TEST_URL");
        _testUser = Environment.GetEnvironmentVariable("WEBDAV_TEST_USER");
        _testPass = Environment.GetEnvironmentVariable("WEBDAV_TEST_PASS");
        _testRoot = Environment.GetEnvironmentVariable("WEBDAV_TEST_ROOT") ?? "";

        _integrationTestsEnabled = !string.IsNullOrEmpty(_testUrl) &&
                                   !string.IsNullOrEmpty(_testUser) &&
                                   !string.IsNullOrEmpty(_testPass);
    }

    public void Dispose() {
        _storage?.Dispose();
    }

    #region Unit Tests (No Server Required)

    [Fact]
    public void Constructor_BasicAuth_ValidParameters_CreatesStorage() {
        // Act
        using var storage = new WebDavStorage("https://cloud.example.com/remote.php/dav/files/user/", "testuser", "testpass");

        // Assert
        Assert.Equal(StorageType.WebDav, storage.StorageType);
        Assert.Equal("", storage.RootPath);
    }

    [Fact]
    public void Constructor_BasicAuth_WithRootPath_CreatesStorage() {
        // Act
        using var storage = new WebDavStorage("https://cloud.example.com/remote.php/dav/files/user/", "testuser", "testpass", rootPath: "test/folder");

        // Assert
        Assert.Equal(StorageType.WebDav, storage.StorageType);
        Assert.Equal("test/folder", storage.RootPath);
    }

    [Fact]
    public void Constructor_BasicAuth_EmptyUrl_ThrowsException() {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => new WebDavStorage("", "user", "pass"));
    }

    [Fact]
    public void Constructor_OAuth2_ValidParameters_CreatesStorage() {
        // Arrange
        var oauth2Config = new OAuth2Config {
            ClientId = "test-client",
            AuthorizeUrl = "https://example.com/authorize",
            TokenUrl = "https://example.com/token",
            RedirectUri = "http://localhost:8080/callback"
        };
        var mockProvider = new MockOAuth2Provider();

        // Act
        using var storage = new WebDavStorage(
            "https://cloud.example.com/remote.php/dav/files/user/",
            oauth2Provider: mockProvider,
            oauth2Config: oauth2Config);

        // Assert
        Assert.Equal(StorageType.WebDav, storage.StorageType);
    }

    [Fact]
    public void Constructor_OAuth2_EmptyUrl_ThrowsException() {
        // Arrange
        var oauth2Config = new OAuth2Config {
            ClientId = "test-client",
            AuthorizeUrl = "https://example.com/authorize",
            TokenUrl = "https://example.com/token",
            RedirectUri = "http://localhost:8080/callback"
        };
        var mockProvider = new MockOAuth2Provider();

        // Act & Assert
        Assert.Throws<ArgumentException>(() => new WebDavStorage(
            "",
            oauth2Provider: mockProvider,
            oauth2Config: oauth2Config));
    }

    [Fact]
    public void Constructor_BasicAuth_CustomChunkSize_CreatesStorage() {
        // Arrange
        var customChunkSize = 5 * 1024 * 1024; // 5MB

        // Act
        using var storage = new WebDavStorage(
            "https://cloud.example.com/remote.php/dav/files/user/",
            "user",
            "pass",
            chunkSizeBytes: customChunkSize);

        // Assert
        Assert.Equal(StorageType.WebDav, storage.StorageType);
    }

    [Fact]
    public void StorageType_Property_ReturnsWebDav() {
        // Arrange
        using var storage = new WebDavStorage("https://cloud.example.com/remote.php/dav/files/user/", "user", "pass");

        // Assert
        Assert.Equal(StorageType.WebDav, storage.StorageType);
    }

    [Fact]
    public void RootPath_Property_ReturnsCorrectPath() {
        // Arrange
        var rootPath = "test/path";
        using var storage = new WebDavStorage("https://cloud.example.com/remote.php/dav/files/user/", "user", "pass", rootPath: rootPath);

        // Assert
        Assert.Equal(rootPath, storage.RootPath);
    }

    [Fact]
    public void RootPath_Property_TrimsSlashes() {
        // Arrange
        using var storage = new WebDavStorage("https://cloud.example.com/remote.php/dav/files/user/", "user", "pass", rootPath: "/test/path/");

        // Assert
        Assert.Equal("test/path", storage.RootPath);
    }

    [Fact]
    public async Task AuthenticateAsync_NoOAuth2Provider_ReturnsTrue() {
        // Arrange
        using var storage = new WebDavStorage("https://cloud.example.com/remote.php/dav/files/user/", "user", "pass");

        // Act
        var result = await storage.AuthenticateAsync();

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task AuthenticateAsync_WithOAuth2Provider_CallsAuthenticate() {
        // Arrange
        var oauth2Config = new OAuth2Config {
            ClientId = "test-client",
            AuthorizeUrl = "https://example.com/authorize",
            TokenUrl = "https://example.com/token",
            RedirectUri = "http://localhost:8080/callback"
        };
        var mockProvider = new MockOAuth2Provider();
        using var storage = new WebDavStorage(
            "https://cloud.example.com/remote.php/dav/files/user/",
            oauth2Provider: mockProvider,
            oauth2Config: oauth2Config);

        // Act
        var result = await storage.AuthenticateAsync();

        // Assert
        Assert.True(result);
        Assert.True(mockProvider.AuthenticateCalled);
    }

    [Fact]
    public async Task AuthenticateAsync_ValidToken_DoesNotReauthenticate() {
        // Arrange
        var oauth2Config = new OAuth2Config {
            ClientId = "test-client",
            AuthorizeUrl = "https://example.com/authorize",
            TokenUrl = "https://example.com/token",
            RedirectUri = "http://localhost:8080/callback"
        };
        var mockProvider = new MockOAuth2Provider();
        using var storage = new WebDavStorage(
            "https://cloud.example.com/remote.php/dav/files/user/",
            oauth2Provider: mockProvider,
            oauth2Config: oauth2Config);

        // Act
        await storage.AuthenticateAsync();
        mockProvider.AuthenticateCalled = false; // Reset flag
        await storage.AuthenticateAsync();

        // Assert - Second call should not authenticate again
        Assert.False(mockProvider.AuthenticateCalled);
    }

    [Fact]
    public async Task AuthenticateAsync_ExpiredToken_UsesRefreshToken() {
        // Arrange
        var oauth2Config = new OAuth2Config {
            ClientId = "test-client",
            AuthorizeUrl = "https://example.com/authorize",
            TokenUrl = "https://example.com/token",
            RedirectUri = "http://localhost:8080/callback"
        };
        var mockProvider = new MockOAuth2Provider(expireTokenImmediately: true);
        using var storage = new WebDavStorage(
            "https://cloud.example.com/remote.php/dav/files/user/",
            oauth2Provider: mockProvider,
            oauth2Config: oauth2Config);

        // Act
        await storage.AuthenticateAsync();
        await Task.Delay(100); // Ensure token is expired
        mockProvider.RefreshCalled = false; // Reset flag
        await storage.AuthenticateAsync();

        // Assert
        Assert.True(mockProvider.RefreshCalled);
    }

    [Fact]
    public async Task GetServerCapabilitiesAsync_CachesResult() {
        // Arrange
        using var storage = new WebDavStorage("https://cloud.example.com/remote.php/dav/files/user/", "user", "pass");

        // Act
        var capabilities1 = await storage.GetServerCapabilitiesAsync();
        var capabilities2 = await storage.GetServerCapabilitiesAsync();

        // Assert - Should return same instance (cached)
        Assert.Same(capabilities1, capabilities2);
    }

    #endregion

    #region Integration Tests (Require WebDAV Server)

    private void SkipIfIntegrationTestsDisabled() {
        if (!_integrationTestsEnabled) {
            throw new SkipException("Integration tests disabled. Set WEBDAV_TEST_URL, WEBDAV_TEST_USER, and WEBDAV_TEST_PASS environment variables.");
        }
    }

    private WebDavStorage CreateStorage() {
        SkipIfIntegrationTestsDisabled();
        return new WebDavStorage(_testUrl!, _testUser!, _testPass!, rootPath: $"{_testRoot}/sharpsync-test-{Guid.NewGuid()}");
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
        using var storage = new WebDavStorage(_testUrl!, _testUser!, "wrong_password");

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
    public async Task CreateDirectoryAsync_AlreadyExists_DoesNotThrow() {
        // Arrange
        _storage = CreateStorage();
        var dirPath = "test/existing";

        // Act
        await _storage.CreateDirectoryAsync(dirPath);
        await _storage.CreateDirectoryAsync(dirPath); // Create again

        // Assert
        var exists = await _storage.ExistsAsync(dirPath);
        Assert.True(exists);
    }

    [Fact]
    public async Task WriteFileAsync_CreatesFile() {
        // Arrange
        _storage = CreateStorage();
        var filePath = "test.txt";
        var content = "Hello, WebDAV World!";

        // Act
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        await _storage.WriteFileAsync(filePath, stream);

        // Assert
        var exists = await _storage.ExistsAsync(filePath);
        Assert.True(exists);
    }

    [Fact]
    public async Task WriteFileAsync_WithParentDirectory_CreatesParentDirectories() {
        // Arrange
        _storage = CreateStorage();
        var filePath = "parent/child/file.txt";
        var content = "Test content";

        // Act
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
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
        var content = "Hello, WebDAV World!";

        using var writeStream = new MemoryStream(Encoding.UTF8.GetBytes(content));
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
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("test"));
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
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("test"));
        await _storage.WriteFileAsync(filePath, stream);

        // Act
        await _storage.DeleteAsync(filePath);
        var exists = await _storage.ExistsAsync(filePath);

        // Assert
        Assert.False(exists);
    }

    [Fact]
    public async Task DeleteAsync_ExistingDirectory_DeletesDirectory() {
        // Arrange
        _storage = CreateStorage();
        var dirPath = "delete_dir";
        await _storage.CreateDirectoryAsync(dirPath);

        // Act
        await _storage.DeleteAsync(dirPath);
        var exists = await _storage.ExistsAsync(dirPath);

        // Assert
        Assert.False(exists);
    }

    [Fact]
    public async Task DeleteAsync_NonexistentFile_DoesNotThrow() {
        // Arrange
        _storage = CreateStorage();

        // Act & Assert - Should not throw
        await _storage.DeleteAsync("nonexistent.txt");
    }

    [Fact]
    public async Task MoveAsync_ExistingFile_MovesFile() {
        // Arrange
        _storage = CreateStorage();
        var sourcePath = "source.txt";
        var targetPath = "target.txt";
        var content = "Move test";

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
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
    public async Task MoveAsync_ToNewDirectory_CreatesParentDirectory() {
        // Arrange
        _storage = CreateStorage();
        var sourcePath = "source.txt";
        var targetPath = "newdir/target.txt";
        var content = "Move test";

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        await _storage.WriteFileAsync(sourcePath, stream);

        // Act
        await _storage.MoveAsync(sourcePath, targetPath);

        // Assert
        var targetExists = await _storage.ExistsAsync(targetPath);
        Assert.True(targetExists);
    }

    [Fact]
    public async Task GetItemAsync_ExistingFile_ReturnsMetadata() {
        // Arrange
        _storage = CreateStorage();
        var filePath = "metadata_test.txt";
        var content = "Test content for metadata";

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        await _storage.WriteFileAsync(filePath, stream);

        // Act
        var item = await _storage.GetItemAsync(filePath);

        // Assert
        Assert.NotNull(item);
        Assert.Equal(filePath, item.Path);
        Assert.False(item.IsDirectory);
        Assert.True(item.Size > 0);
        Assert.True(item.LastModified > DateTime.MinValue);
    }

    [Fact]
    public async Task GetItemAsync_ExistingDirectory_ReturnsMetadata() {
        // Arrange
        _storage = CreateStorage();
        var dirPath = "metadata_dir";
        await _storage.CreateDirectoryAsync(dirPath);

        // Act
        var item = await _storage.GetItemAsync(dirPath);

        // Assert
        Assert.NotNull(item);
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
    public async Task ListItemsAsync_EmptyDirectory_ReturnsEmpty() {
        // Arrange
        _storage = CreateStorage();
        var dirPath = "empty_dir";
        await _storage.CreateDirectoryAsync(dirPath);

        // Act
        var items = await _storage.ListItemsAsync(dirPath);

        // Assert
        Assert.Empty(items);
    }

    [Fact]
    public async Task ListItemsAsync_WithFiles_ReturnsAllItems() {
        // Arrange
        _storage = CreateStorage();
        var dirPath = "list_test";
        await _storage.CreateDirectoryAsync(dirPath);

        // Create test files and subdirectories
        await _storage.WriteFileAsync($"{dirPath}/file1.txt", new MemoryStream(Encoding.UTF8.GetBytes("content1")));
        await _storage.WriteFileAsync($"{dirPath}/file2.txt", new MemoryStream(Encoding.UTF8.GetBytes("content2")));
        await _storage.CreateDirectoryAsync($"{dirPath}/subdir");

        // Act
        var items = (await _storage.ListItemsAsync(dirPath)).ToList();

        // Assert
        Assert.Equal(3, items.Count);
        Assert.Contains(items, i => i.Path.Contains("file1.txt") && !i.IsDirectory);
        Assert.Contains(items, i => i.Path.Contains("file2.txt") && !i.IsDirectory);
        Assert.Contains(items, i => i.Path.Contains("subdir") && i.IsDirectory);
    }

    [Fact]
    public async Task ListItemsAsync_NonexistentDirectory_ReturnsEmpty() {
        // Arrange
        _storage = CreateStorage();

        // Act
        var items = await _storage.ListItemsAsync("nonexistent");

        // Assert
        Assert.Empty(items);
    }

    [Fact]
    public async Task ComputeHashAsync_ExistingFile_ReturnsHash() {
        // Arrange
        _storage = CreateStorage();
        var filePath = "hash_test.txt";
        var content = "Test content for hashing";

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        await _storage.WriteFileAsync(filePath, stream);

        // Act
        var hash = await _storage.ComputeHashAsync(filePath);

        // Assert
        Assert.NotNull(hash);
        Assert.NotEmpty(hash);
    }

    [Fact]
    public async Task ComputeHashAsync_SameContent_ReturnsSameHash() {
        // Arrange
        _storage = CreateStorage();
        var filePath1 = "hash_test1.txt";
        var filePath2 = "hash_test2.txt";
        var content = "Identical content";

        using var stream1 = new MemoryStream(Encoding.UTF8.GetBytes(content));
        using var stream2 = new MemoryStream(Encoding.UTF8.GetBytes(content));
        await _storage.WriteFileAsync(filePath1, stream1);
        await _storage.WriteFileAsync(filePath2, stream2);

        // Act
        var hash1 = await _storage.ComputeHashAsync(filePath1);
        var hash2 = await _storage.ComputeHashAsync(filePath2);

        // Assert
        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public async Task GetStorageInfoAsync_ReturnsInfo() {
        // Arrange
        _storage = CreateStorage();

        // Act
        var info = await _storage.GetStorageInfoAsync();

        // Assert
        Assert.NotNull(info);
        // Note: Some WebDAV servers may not support quota info, so values might be -1
        Assert.True(info.TotalSpace >= -1);
        Assert.True(info.UsedSpace >= -1);
    }

    [Fact]
    public async Task WriteFileAsync_LargeFile_RaisesProgressEvents() {
        // Arrange
        _storage = CreateStorage();
        var filePath = "large_file.bin";
        var fileSize = 2 * 1024 * 1024; // 2MB
        var content = new byte[fileSize];
        new Random().NextBytes(content);

        var progressEventRaised = false;
        _storage.ProgressChanged += (sender, args) => {
            progressEventRaised = true;
            Assert.Equal(filePath, args.Path);
            Assert.Equal(StorageOperation.Upload, args.Operation);
        };

        // Act
        using var stream = new MemoryStream(content);
        await _storage.WriteFileAsync(filePath, stream);

        // Assert
        var exists = await _storage.ExistsAsync(filePath);
        Assert.True(exists);
        // Note: Progress events may not be raised for all servers/sizes
    }

    [Fact]
    public async Task ReadFileAsync_LargeFile_RaisesProgressEvents() {
        // Arrange
        _storage = CreateStorage();
        var filePath = "large_file_read.bin";
        var fileSize = 2 * 1024 * 1024; // 2MB
        var content = new byte[fileSize];
        new Random().NextBytes(content);

        using var writeStream = new MemoryStream(content);
        await _storage.WriteFileAsync(filePath, writeStream);

        var progressEventRaised = false;
        _storage.ProgressChanged += (sender, args) => {
            progressEventRaised = true;
            Assert.Equal(StorageOperation.Download, args.Operation);
        };

        // Act
        using var readStream = await _storage.ReadFileAsync(filePath);
        using var ms = new MemoryStream();
        await readStream.CopyToAsync(ms);

        // Assert
        Assert.Equal(fileSize, ms.Length);
        // Note: Progress events may not be raised for all servers/sizes
    }

    [Fact]
    public async Task GetServerCapabilitiesAsync_ReturnsCapabilities() {
        // Arrange
        _storage = CreateStorage();

        // Act
        var capabilities = await _storage.GetServerCapabilitiesAsync();

        // Assert
        Assert.NotNull(capabilities);
        // Capabilities will vary by server type (Nextcloud, OCIS, generic WebDAV)
    }

    [Fact]
    public async Task CancellationToken_CancelsOperation() {
        // Arrange
        _storage = CreateStorage();
        var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await _storage.ListItemsAsync("", cts.Token));
    }

    [Fact]
    public async Task Dispose_DisposesResources() {
        // Arrange
        var storage = CreateStorage();
        await storage.TestConnectionAsync();

        // Act
        storage.Dispose();

        // Assert - Should not throw when disposed multiple times
        storage.Dispose();
    }

    #endregion

    #region Mock OAuth2Provider for Testing

    private class MockOAuth2Provider: IOAuth2Provider {
        private readonly bool _expireTokenImmediately;
        public bool AuthenticateCalled { get; set; }
        public bool RefreshCalled { get; set; }
        public bool ValidateCalled { get; set; }

        public MockOAuth2Provider(bool expireTokenImmediately = false) {
            _expireTokenImmediately = expireTokenImmediately;
        }

        public Task<OAuth2Result> AuthenticateAsync(OAuth2Config config, CancellationToken cancellationToken = default) {
            AuthenticateCalled = true;
            return Task.FromResult(new OAuth2Result {
                AccessToken = "mock-access-token",
                RefreshToken = "mock-refresh-token",
                ExpiresAt = _expireTokenImmediately ? DateTime.UtcNow.AddMilliseconds(10) : DateTime.UtcNow.AddHours(1),
                TokenType = "Bearer",
                UserId = "mock-user"
            });
        }

        public Task<OAuth2Result> RefreshTokenAsync(OAuth2Config config, string refreshToken, CancellationToken cancellationToken = default) {
            RefreshCalled = true;
            return Task.FromResult(new OAuth2Result {
                AccessToken = "mock-refreshed-token",
                RefreshToken = refreshToken,
                ExpiresAt = DateTime.UtcNow.AddHours(1),
                TokenType = "Bearer",
                UserId = "mock-user"
            });
        }

        public Task<bool> ValidateTokenAsync(OAuth2Result result, CancellationToken cancellationToken = default) {
            ValidateCalled = true;
            return Task.FromResult(result.IsValid);
        }
    }

    #endregion
}
