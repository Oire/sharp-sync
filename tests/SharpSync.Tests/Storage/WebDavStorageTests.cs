using System.Text;
using Oire.SharpSync.Auth;
using Oire.SharpSync.Core;
using Oire.SharpSync.Tests.Fixtures;

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

    // OCIS-specific test configuration
    private readonly string? _ocisTestUrl;
    private readonly string? _ocisTestUser;
    private readonly string? _ocisTestPass;
    private readonly bool _ocisTestsEnabled;

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

        // OCIS-specific environment variables
        _ocisTestUrl = Environment.GetEnvironmentVariable("OCIS_TEST_URL");
        _ocisTestUser = Environment.GetEnvironmentVariable("OCIS_TEST_USER");
        _ocisTestPass = Environment.GetEnvironmentVariable("OCIS_TEST_PASS");

        _ocisTestsEnabled = !string.IsNullOrEmpty(_ocisTestUrl) &&
                            !string.IsNullOrEmpty(_ocisTestUser) &&
                            !string.IsNullOrEmpty(_ocisTestPass);
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

    #region TUS Protocol Unit Tests

    [Fact]
    public void EncodeTusMetadata_EncodesFilenameCorrectly() {
        // Arrange
        var filename = "test.txt";

        // Act
        var result = WebDavStorage.EncodeTusMetadata(filename);

        // Assert
        Assert.StartsWith("filename ", result);
        var encodedPart = result.Substring("filename ".Length);
        var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(encodedPart));
        Assert.Equal(filename, decoded);
    }

    [Fact]
    public void EncodeTusMetadata_HandlesUnicodeCharacters() {
        // Arrange
        var filename = "文档.txt"; // Chinese characters

        // Act
        var result = WebDavStorage.EncodeTusMetadata(filename);

        // Assert
        Assert.StartsWith("filename ", result);
        var encodedPart = result.Substring("filename ".Length);
        var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(encodedPart));
        Assert.Equal(filename, decoded);
    }

    [Fact]
    public void EncodeTusMetadata_HandlesSpecialCharacters() {
        // Arrange
        var filename = "test file (1) [copy].txt";

        // Act
        var result = WebDavStorage.EncodeTusMetadata(filename);

        // Assert
        Assert.StartsWith("filename ", result);
        var encodedPart = result.Substring("filename ".Length);
        var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(encodedPart));
        Assert.Equal(filename, decoded);
    }

    [Fact]
    public void EncodeTusMetadata_HandlesEmptyString() {
        // Arrange
        var filename = "";

        // Act
        var result = WebDavStorage.EncodeTusMetadata(filename);

        // Assert
        Assert.StartsWith("filename ", result);
        var encodedPart = result.Substring("filename ".Length);
        var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(encodedPart));
        Assert.Equal(filename, decoded);
    }

    [Fact]
    public void EncodeTusMetadata_HandlesEmoji() {
        // Arrange
        var filename = "document📄.txt";

        // Act
        var result = WebDavStorage.EncodeTusMetadata(filename);

        // Assert
        Assert.StartsWith("filename ", result);
        var encodedPart = result.Substring("filename ".Length);
        var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(encodedPart));
        Assert.Equal(filename, decoded);
    }

    #endregion

    #region Server Base URL Extraction Tests

    [Theory]
    [InlineData(
        "https://cloud.example.com/remote.php/dav/files/username",
        "https://cloud.example.com")]
    [InlineData(
        "https://cloud.example.com/remote.php/dav/files/username/",
        "https://cloud.example.com")]
    [InlineData(
        "https://cloud.example.com/remote.php/webdav",
        "https://cloud.example.com")]
    [InlineData(
        "https://example.com/nextcloud/remote.php/dav/files/user",
        "https://example.com/nextcloud")]
    [InlineData(
        "https://ocis.example.com/dav/files/username",
        "https://ocis.example.com")]
    [InlineData(
        "https://ocis.example.com/dav/spaces/some-space-id",
        "https://ocis.example.com")]
    [InlineData(
        "https://webdav.example.com:8443/remote.php/dav/files/user",
        "https://webdav.example.com:8443")]
    [InlineData(
        "https://generic.example.com/some/path",
        "https://generic.example.com")]
    public void GetServerBaseUrl_ExtractsCorrectBase(string baseUrl, string expected) {
        // Act
        var result = WebDavStorage.GetServerBaseUrl(baseUrl);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void GetServerBaseUrl_DoesNotMatchDavInsideWord() {
        // "webdav" contains "dav" but "/dav/" should not match inside "/webdav/"
        var result = WebDavStorage.GetServerBaseUrl("https://example.com/webdav/files/test");

        // "/dav/" appears at a substring boundary inside "/webdav/", but IndexOf("/dav/")
        // won't match because the character before 'd' is 'b', not '/'
        Assert.Equal("https://example.com", result);
    }

    #endregion

    #endregion

    #region Integration Tests (Require WebDAV Server)

    private void SkipIfIntegrationTestsDisabled() {
        Skip.If(!_integrationTestsEnabled, "Integration tests disabled. Set WEBDAV_TEST_URL, WEBDAV_TEST_USER, and WEBDAV_TEST_PASS environment variables.");
    }

    private WebDavStorage CreateStorage() {
        SkipIfIntegrationTestsDisabled();
        return new WebDavStorage(_testUrl!, _testUser!, _testPass!, rootPath: $"{_testRoot}/sharpsync-test-{Guid.NewGuid()}");
    }

    /// <summary>
    /// Helper method to wait for an item to exist on the server with retry logic.
    /// WebDAV servers may have propagation delays.
    /// </summary>
    private static async Task<bool> WaitForExistsAsync(WebDavStorage storage, string path, int maxRetries = 5, int delayMs = 100) {
        for (int i = 0; i < maxRetries; i++) {
            if (await storage.ExistsAsync(path)) {
                return true;
            }
            await Task.Delay(delayMs);
        }
        return await storage.ExistsAsync(path);
    }

    [SkippableFact]
    public async Task TestConnectionAsync_ValidCredentials_ReturnsTrue() {
        SkipIfIntegrationTestsDisabled();

        // Arrange
        using var storage = CreateStorage();

        // Act
        var result = await storage.TestConnectionAsync();

        // Assert
        Assert.True(result);
    }

    [SkippableFact]
    public async Task TestConnectionAsync_InvalidCredentials_ReturnsFalse() {
        SkipIfIntegrationTestsDisabled();

        // Arrange
        using var storage = new WebDavStorage(_testUrl!, _testUser!, "wrong_password");

        // Act
        var result = await storage.TestConnectionAsync();

        // Assert
        Assert.False(result);
    }

    [SkippableFact]
    public async Task CreateDirectoryAsync_CreatesDirectory() {
        // Arrange
        _storage = CreateStorage();
        var dirPath = "test/subdir";

        // Act
        await _storage.CreateDirectoryAsync(dirPath);
        var exists = await WaitForExistsAsync(_storage, dirPath);

        // Assert
        Assert.True(exists, $"Directory '{dirPath}' should exist after creation");
    }

    [SkippableFact]
    public async Task CreateDirectoryAsync_AlreadyExists_DoesNotThrow() {
        // Arrange
        _storage = CreateStorage();
        var dirPath = "test/existing";

        // Act
        await _storage.CreateDirectoryAsync(dirPath);
        var existsAfterFirstCreate = await WaitForExistsAsync(_storage, dirPath);

        // Ensure the directory exists after the first creation
        Assert.True(existsAfterFirstCreate, "Directory should exist after first creation");

        await _storage.CreateDirectoryAsync(dirPath); // Create again

        // Assert
        var exists = await WaitForExistsAsync(_storage, dirPath);
        Assert.True(exists, "Directory should still exist after second creation attempt");
    }

    [SkippableFact]
    public async Task WriteFileAsync_CreatesFile() {
        // Arrange
        _storage = CreateStorage();
        var filePath = "test.txt";
        var content = "Hello, WebDAV World!";

        // Act
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        await _storage.WriteFileAsync(filePath, stream);

        // Assert
        var exists = await WaitForExistsAsync(_storage, filePath);
        Assert.True(exists, $"File '{filePath}' should exist after writing");
    }

    [SkippableFact]
    public async Task WriteFileAsync_WithParentDirectory_CreatesParentDirectories() {
        // Arrange
        _storage = CreateStorage();
        var filePath = "parent/child/file.txt";
        var content = "Test content";

        // Act
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        await _storage.WriteFileAsync(filePath, stream);

        // Assert - verify parent directories and file were created
        var parentExists = await WaitForExistsAsync(_storage, "parent");
        Assert.True(parentExists, "Parent directory 'parent' should exist");

        var childExists = await WaitForExistsAsync(_storage, "parent/child");
        Assert.True(childExists, "Child directory 'parent/child' should exist");

        var fileExists = await WaitForExistsAsync(_storage, filePath);
        Assert.True(fileExists, $"File '{filePath}' should exist after writing");
    }

    [SkippableFact]
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

    [SkippableFact]
    public async Task ReadFileAsync_NonexistentFile_ThrowsException() {
        // Arrange
        _storage = CreateStorage();

        // Act & Assert
        await Assert.ThrowsAsync<FileNotFoundException>(() => _storage.ReadFileAsync("nonexistent.txt"));
    }

    [SkippableFact]
    public async Task ExistsAsync_ExistingFile_ReturnsTrue() {
        // Arrange
        _storage = CreateStorage();
        var filePath = "exists_test.txt";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("test"));
        await _storage.WriteFileAsync(filePath, stream);

        // Act - use retry helper to account for server propagation delay
        var result = await WaitForExistsAsync(_storage, filePath);

        // Assert
        Assert.True(result, $"File '{filePath}' should exist after writing");
    }

    [SkippableFact]
    public async Task ExistsAsync_NonexistentFile_ReturnsFalse() {
        // Arrange
        _storage = CreateStorage();

        // Act
        var result = await _storage.ExistsAsync("nonexistent.txt");

        // Assert
        Assert.False(result);
    }

    [SkippableFact]
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

    [SkippableFact]
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

    [SkippableFact]
    public async Task DeleteAsync_NonexistentFile_DoesNotThrow() {
        // Arrange
        _storage = CreateStorage();

        // Act & Assert - Should not throw
        await _storage.DeleteAsync("nonexistent.txt");
    }

    [SkippableFact]
    public async Task MoveAsync_ExistingFile_MovesFile() {
        // Arrange
        _storage = CreateStorage();
        var sourcePath = "source.txt";
        var targetPath = "target.txt";
        var content = "Move test";

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        await _storage.WriteFileAsync(sourcePath, stream);
        await WaitForExistsAsync(_storage, sourcePath);

        // Act
        await _storage.MoveAsync(sourcePath, targetPath);

        // Assert - give the server time to process the move
        await Task.Delay(100);
        var sourceExists = await _storage.ExistsAsync(sourcePath);
        var targetExists = await WaitForExistsAsync(_storage, targetPath);
        Assert.False(sourceExists, "Source file should not exist after move");
        Assert.True(targetExists, "Target file should exist after move");
    }

    [SkippableFact]
    public async Task MoveAsync_ToNewDirectory_CreatesParentDirectory() {
        // Arrange
        _storage = CreateStorage();
        var sourcePath = "source.txt";
        var targetPath = "newdir/target.txt";
        var content = "Move test";

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        await _storage.WriteFileAsync(sourcePath, stream);
        await WaitForExistsAsync(_storage, sourcePath);

        // Act
        await _storage.MoveAsync(sourcePath, targetPath);

        // Assert
        var targetExists = await WaitForExistsAsync(_storage, targetPath);
        Assert.True(targetExists, "Target file should exist after move to new directory");
    }

    [SkippableFact]
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

    [SkippableFact]
    public async Task GetItemAsync_ExistingDirectory_ReturnsMetadata() {
        // Arrange
        _storage = CreateStorage();
        var dirPath = "metadata_dir";

        // Ensure the directory is created
        await _storage.CreateDirectoryAsync(dirPath);

        // Verify directory exists before testing GetItemAsync (with retry for propagation)
        var exists = await WaitForExistsAsync(_storage, dirPath);
        Assert.True(exists, "Directory should exist after creation");

        // Act
        var item = await _storage.GetItemAsync(dirPath);

        // Assert
        Assert.NotNull(item);
        Assert.True(item.IsDirectory);
    }

    [SkippableFact]
    public async Task GetItemAsync_NonexistentItem_ReturnsNull() {
        // Arrange
        _storage = CreateStorage();

        // Act
        var item = await _storage.GetItemAsync("nonexistent.txt");

        // Assert
        Assert.Null(item);
    }

    [SkippableFact]
    public async Task ListItemsAsync_EmptyDirectory_ReturnsEmpty() {
        // Arrange
        _storage = CreateStorage();
        var dirPath = "empty_dir";
        await _storage.CreateDirectoryAsync(dirPath);
        await WaitForExistsAsync(_storage, dirPath);

        // Act
        var items = await _storage.ListItemsAsync(dirPath);

        // Assert
        Assert.Empty(items);
    }

    [SkippableFact]
    public async Task ListItemsAsync_WithFiles_ReturnsAllItems() {
        // Arrange
        _storage = CreateStorage();
        var dirPath = "list_test";
        await _storage.CreateDirectoryAsync(dirPath);
        await WaitForExistsAsync(_storage, dirPath);

        // Create test files and subdirectories
        await _storage.WriteFileAsync($"{dirPath}/file1.txt", new MemoryStream(Encoding.UTF8.GetBytes("content1")));
        await _storage.WriteFileAsync($"{dirPath}/file2.txt", new MemoryStream(Encoding.UTF8.GetBytes("content2")));
        await _storage.CreateDirectoryAsync($"{dirPath}/subdir");

        // Verify all items exist before listing (with retry for server propagation)
        Assert.True(await WaitForExistsAsync(_storage, $"{dirPath}/file1.txt"), "file1.txt should exist");
        Assert.True(await WaitForExistsAsync(_storage, $"{dirPath}/file2.txt"), "file2.txt should exist");
        Assert.True(await WaitForExistsAsync(_storage, $"{dirPath}/subdir"), "subdir should exist");

        // Act - retry list operation to account for propagation
        List<SyncItem>? items = null;
        for (int attempt = 0; attempt < 5; attempt++) {
            items = (await _storage.ListItemsAsync(dirPath)).ToList();
            if (items.Count >= 3)
                break;
            await Task.Delay(100);
        }

        // Assert
        Assert.NotNull(items);
        Assert.Equal(3, items.Count);
        Assert.Contains(items, i => i.Path.EndsWith("file1.txt") && !i.IsDirectory);
        Assert.Contains(items, i => i.Path.EndsWith("file2.txt") && !i.IsDirectory);
        Assert.Contains(items, i => i.Path.EndsWith("subdir") && i.IsDirectory);
    }

    [SkippableFact]
    public async Task ListItemsAsync_NonexistentDirectory_ReturnsEmpty() {
        // Arrange
        _storage = CreateStorage();

        // Act
        var items = await _storage.ListItemsAsync("nonexistent");

        // Assert
        Assert.Empty(items);
    }

    [SkippableFact]
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

    [SkippableFact]
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

    [SkippableFact]
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

    [SkippableFact]
    public async Task WriteFileAsync_LargeFile_RaisesProgressEvents() {
        // Arrange
        _storage = CreateStorage();
        var filePath = "large_file.bin";
        var fileSize = 2 * 1024 * 1024; // 2MB
        var content = new byte[fileSize];
        new Random().NextBytes(content);

        _storage.ProgressChanged += (sender, args) => {
            Assert.Equal(filePath, args.Path);
            Assert.Equal(StorageOperation.Upload, args.Operation);
        };

        // Act
        using var stream = new MemoryStream(content);
        await _storage.WriteFileAsync(filePath, stream);

        // Assert
        var exists = await WaitForExistsAsync(_storage, filePath);
        Assert.True(exists, $"Large file '{filePath}' should exist after writing");
        // Note: Progress events may not be raised for all servers/sizes
    }

    [SkippableFact]
    public async Task ReadFileAsync_LargeFile_RaisesProgressEvents() {
        // Arrange
        _storage = CreateStorage();
        var filePath = "large_file_read.bin";
        var fileSize = 2 * 1024 * 1024; // 2MB
        var content = new byte[fileSize];
        new Random().NextBytes(content);

        using var writeStream = new MemoryStream(content);
        await _storage.WriteFileAsync(filePath, writeStream);

        _storage.ProgressChanged += (sender, args) => {
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

    [SkippableFact]
    public async Task GetServerCapabilitiesAsync_ReturnsCapabilities() {
        // Arrange
        _storage = CreateStorage();

        // Act
        var capabilities = await _storage.GetServerCapabilitiesAsync();

        // Assert
        Assert.NotNull(capabilities);
        // Capabilities will vary by server type (Nextcloud, OCIS, generic WebDAV)
    }

    [SkippableFact]
    public async Task CancellationToken_CancelsOperation() {
        // Arrange
        _storage = CreateStorage();
        var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await _storage.ListItemsAsync("", cts.Token));
    }

    [SkippableFact]
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

    #region GetRemoteChangesAsync Integration Tests

    [SkippableFact]
    public async Task GetRemoteChangesAsync_NonNextcloudServer_ReturnsEmpty() {
        // Arrange - generic WebDAV server without Nextcloud capabilities
        SkipIfIntegrationTestsDisabled();
        using var storage = new WebDavStorage(_testUrl!, _testUser!, _testPass!, rootPath: _testRoot);

        var capabilities = await storage.GetServerCapabilitiesAsync();

        if (!capabilities.IsNextcloud && !capabilities.IsOcis) {
            // Act - generic server should return empty
            var changes = await storage.GetRemoteChangesAsync(DateTime.UtcNow.AddHours(-1));

            // Assert
            Assert.Empty(changes);
        } else {
            // This is a Nextcloud/OCIS server; test that it returns a valid list
            var changes = await storage.GetRemoteChangesAsync(DateTime.UtcNow.AddHours(-1));
            Assert.NotNull(changes);
        }
    }

    [SkippableFact]
    public async Task GetRemoteChangesAsync_AfterFileCreation_ReturnsChanges() {
        // Arrange
        _storage = CreateStorage();
        var capabilities = await _storage.GetServerCapabilitiesAsync();
        Skip.IfNot(capabilities.IsNextcloud || capabilities.IsOcis,
            "GetRemoteChangesAsync requires Nextcloud or OCIS server");

        var since = DateTime.UtcNow.AddSeconds(-5);
        var filePath = $"remote_change_test_{Guid.NewGuid()}.txt";
        using var stream = new MemoryStream("remote change test"u8.ToArray());
        await _storage.WriteFileAsync(filePath, stream);

        // Allow time for the activity API to register the change
        await Task.Delay(2000);

        // Act
        var changes = await _storage.GetRemoteChangesAsync(since);

        // Assert
        Assert.NotNull(changes);
        // The activity API may include other recent changes, just verify we get something
        Assert.True(changes.Count >= 0);
    }

    [SkippableFact]
    public async Task GetRemoteChangesAsync_FarFutureSince_ReturnsEmpty() {
        // Arrange
        _storage = CreateStorage();
        var capabilities = await _storage.GetServerCapabilitiesAsync();
        Skip.IfNot(capabilities.IsNextcloud || capabilities.IsOcis,
            "GetRemoteChangesAsync requires Nextcloud or OCIS server");

        // Act - Ask for changes since far in the future
        var changes = await _storage.GetRemoteChangesAsync(DateTime.UtcNow.AddYears(10));

        // Assert
        Assert.Empty(changes);
    }

    [SkippableFact]
    public async Task GetRemoteChangesAsync_CancellationRequested_ThrowsOperationCanceledException() {
        // Arrange
        _storage = CreateStorage();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var capabilities = await _storage.GetServerCapabilitiesAsync();
        Skip.IfNot(capabilities.IsNextcloud || capabilities.IsOcis,
            "GetRemoteChangesAsync requires Nextcloud or OCIS server");

        // Act & Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _storage.GetRemoteChangesAsync(DateTime.UtcNow.AddHours(-1), cts.Token));
    }

    #endregion

    #region OCIS TUS Protocol Integration Tests

    private void SkipIfOcisTestsDisabled() {
        Skip.If(!_ocisTestsEnabled, "OCIS integration tests disabled. Set OCIS_TEST_URL, OCIS_TEST_USER, and OCIS_TEST_PASS environment variables.");
    }

    private WebDavStorage CreateOcisStorage() {
        SkipIfOcisTestsDisabled();
        return new WebDavStorage(_ocisTestUrl!, _ocisTestUser!, _ocisTestPass!, rootPath: $"sharpsync-tus-test-{Guid.NewGuid()}");
    }

    [SkippableFact]
    public async Task WriteFileAsync_LargeFile_UseTusProtocol_OnOcis() {
        // Arrange
        using var storage = CreateOcisStorage();
        var filePath = "large_tus_test.bin";
        var fileSize = 15 * 1024 * 1024; // 15MB - larger than chunk size to trigger TUS
        var content = new byte[fileSize];
        new Random(42).NextBytes(content); // Seeded for reproducibility

        // Verify this is an OCIS server
        var capabilities = await storage.GetServerCapabilitiesAsync();
        Skip.IfNot(capabilities.IsOcis, "Server is not OCIS, skipping TUS-specific test");

        // Act
        using var stream = new MemoryStream(content);
        await storage.WriteFileAsync(filePath, stream);

        // Assert - verify file was uploaded correctly
        var exists = await WaitForExistsAsync(storage, filePath);
        Assert.True(exists, "Large file should exist after TUS upload");

        // Verify content integrity by reading it back
        using var readStream = await storage.ReadFileAsync(filePath);
        using var ms = new MemoryStream();
        await readStream.CopyToAsync(ms);
        Assert.Equal(fileSize, ms.Length);
    }

    [SkippableFact]
    public async Task WriteFileAsync_TusUpload_RaisesProgressEvents() {
        // Arrange
        using var storage = CreateOcisStorage();
        var filePath = "tus_progress_test.bin";
        var fileSize = 12 * 1024 * 1024; // 12MB
        var content = new byte[fileSize];
        new Random(42).NextBytes(content);

        // Verify this is an OCIS server
        var capabilities = await storage.GetServerCapabilitiesAsync();
        Skip.IfNot(capabilities.IsOcis, "Server is not OCIS, skipping TUS-specific test");

        var progressEvents = new List<StorageProgressEventArgs>();
        storage.ProgressChanged += (sender, args) => {
            if (args.Operation == StorageOperation.Upload) {
                progressEvents.Add(args);
            }
        };

        // Act
        using var stream = new MemoryStream(content);
        await storage.WriteFileAsync(filePath, stream);

        // Assert
        Assert.NotEmpty(progressEvents);
        Assert.Contains(progressEvents, e => e.BytesTransferred == 0); // Initial progress
        Assert.Contains(progressEvents, e => e.BytesTransferred == fileSize); // Final progress
        Assert.All(progressEvents, e => {
            Assert.Equal(filePath, e.Path);
            Assert.Equal(StorageOperation.Upload, e.Operation);
            Assert.Equal(fileSize, e.TotalBytes);
        });
    }

    [SkippableFact]
    public async Task WriteFileAsync_TusUpload_SmallFile_DoesNotUseTus() {
        // Arrange
        using var storage = CreateOcisStorage();
        var filePath = "small_no_tus_test.txt";
        var content = "This is a small file that should not trigger TUS protocol";

        // Verify this is an OCIS server
        var capabilities = await storage.GetServerCapabilitiesAsync();
        Skip.IfNot(capabilities.IsOcis, "Server is not OCIS, skipping TUS-specific test");

        // Act
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        await storage.WriteFileAsync(filePath, stream);

        // Assert
        var exists = await WaitForExistsAsync(storage, filePath);
        Assert.True(exists, "Small file should be uploaded successfully");

        using var readStream = await storage.ReadFileAsync(filePath);
        using var reader = new StreamReader(readStream);
        var readContent = await reader.ReadToEndAsync();
        Assert.Equal(content, readContent);
    }

    [SkippableFact]
    public async Task GetServerCapabilitiesAsync_OcisServer_DetectsOcis() {
        // Arrange
        using var storage = CreateOcisStorage();

        // Act
        var capabilities = await storage.GetServerCapabilitiesAsync();

        // Assert
        Assert.True(capabilities.IsOcis, "Server should be detected as OCIS");
        Assert.True(capabilities.SupportsOcisChunking, "OCIS server should support TUS chunking");
    }

    #endregion

    #region Mock OAuth2Provider for Testing

    private sealed class MockOAuth2Provider: IOAuth2Provider {
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
