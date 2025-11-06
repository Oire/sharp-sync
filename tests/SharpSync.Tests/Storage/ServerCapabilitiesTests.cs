namespace Oire.SharpSync.Tests.Storage;

public class ServerCapabilitiesTests {
    [Fact]
    public void Constructor_DefaultValues_AllPropertiesFalseOrEmpty() {
        // Arrange & Act
        var capabilities = new ServerCapabilities();

        // Assert
        Assert.False(capabilities.IsNextcloud);
        Assert.False(capabilities.IsOcis);
        Assert.False(capabilities.SupportsChunking);
        Assert.False(capabilities.SupportsOcisChunking);
        Assert.Equal("", capabilities.ServerVersion);
        Assert.Equal(0, capabilities.ChunkingVersion);
    }

    [Fact]
    public void IsNextcloud_SetToTrue_UpdatesCorrectly() {
        // Arrange
        var capabilities = new ServerCapabilities();

        // Act
        capabilities.IsNextcloud = true;

        // Assert
        Assert.True(capabilities.IsNextcloud);
    }

    [Fact]
    public void IsOcis_SetToTrue_UpdatesCorrectly() {
        // Arrange
        var capabilities = new ServerCapabilities();

        // Act
        capabilities.IsOcis = true;

        // Assert
        Assert.True(capabilities.IsOcis);
    }

    [Fact]
    public void ServerVersion_SetValue_UpdatesCorrectly() {
        // Arrange
        var capabilities = new ServerCapabilities();
        var version = "28.0.1";

        // Act
        capabilities.ServerVersion = version;

        // Assert
        Assert.Equal(version, capabilities.ServerVersion);
    }

    [Fact]
    public void SupportsChunking_SetToTrue_UpdatesCorrectly() {
        // Arrange
        var capabilities = new ServerCapabilities();

        // Act
        capabilities.SupportsChunking = true;

        // Assert
        Assert.True(capabilities.SupportsChunking);
    }

    [Fact]
    public void ChunkingVersion_SetValue_UpdatesCorrectly() {
        // Arrange
        var capabilities = new ServerCapabilities();

        // Act
        capabilities.ChunkingVersion = 2;

        // Assert
        Assert.Equal(2, capabilities.ChunkingVersion);
    }

    [Fact]
    public void SupportsOcisChunking_SetToTrue_UpdatesCorrectly() {
        // Arrange
        var capabilities = new ServerCapabilities();

        // Act
        capabilities.SupportsOcisChunking = true;

        // Assert
        Assert.True(capabilities.SupportsOcisChunking);
    }

    [Fact]
    public void IsGenericWebDav_WhenNeitherNextcloudNorOcis_ReturnsTrue() {
        // Arrange
        var capabilities = new ServerCapabilities {
            IsNextcloud = false,
            IsOcis = false
        };

        // Act & Assert
        Assert.True(capabilities.IsGenericWebDav);
    }

    [Fact]
    public void IsGenericWebDav_WhenNextcloud_ReturnsFalse() {
        // Arrange
        var capabilities = new ServerCapabilities {
            IsNextcloud = true,
            IsOcis = false
        };

        // Act & Assert
        Assert.False(capabilities.IsGenericWebDav);
    }

    [Fact]
    public void IsGenericWebDav_WhenOcis_ReturnsFalse() {
        // Arrange
        var capabilities = new ServerCapabilities {
            IsNextcloud = false,
            IsOcis = true
        };

        // Act & Assert
        Assert.False(capabilities.IsGenericWebDav);
    }

    [Fact]
    public void IsGenericWebDav_WhenBothNextcloudAndOcis_ReturnsFalse() {
        // Arrange
        var capabilities = new ServerCapabilities {
            IsNextcloud = true,
            IsOcis = true
        };

        // Act & Assert
        Assert.False(capabilities.IsGenericWebDav);
    }

    [Fact]
    public void NextcloudCapabilities_FullConfiguration_SetsCorrectly() {
        // Arrange & Act
        var capabilities = new ServerCapabilities {
            IsNextcloud = true,
            ServerVersion = "28.0.1",
            SupportsChunking = true,
            ChunkingVersion = 2
        };

        // Assert
        Assert.True(capabilities.IsNextcloud);
        Assert.Equal("28.0.1", capabilities.ServerVersion);
        Assert.True(capabilities.SupportsChunking);
        Assert.Equal(2, capabilities.ChunkingVersion);
        Assert.False(capabilities.IsGenericWebDav);
    }

    [Fact]
    public void OcisCapabilities_FullConfiguration_SetsCorrectly() {
        // Arrange & Act
        var capabilities = new ServerCapabilities {
            IsOcis = true,
            ServerVersion = "5.0.0",
            SupportsOcisChunking = true
        };

        // Assert
        Assert.True(capabilities.IsOcis);
        Assert.Equal("5.0.0", capabilities.ServerVersion);
        Assert.True(capabilities.SupportsOcisChunking);
        Assert.False(capabilities.IsGenericWebDav);
    }

    [Fact]
    public void GenericWebDavCapabilities_Configuration_SetsCorrectly() {
        // Arrange & Act
        var capabilities = new ServerCapabilities {
            IsNextcloud = false,
            IsOcis = false,
            ServerVersion = "1.0.0"
        };

        // Assert
        Assert.False(capabilities.IsNextcloud);
        Assert.False(capabilities.IsOcis);
        Assert.True(capabilities.IsGenericWebDav);
        Assert.Equal("1.0.0", capabilities.ServerVersion);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void ChunkingVersion_VariousVersions_SetsCorrectly(int version) {
        // Arrange
        var capabilities = new ServerCapabilities();

        // Act
        capabilities.ChunkingVersion = version;

        // Assert
        Assert.Equal(version, capabilities.ChunkingVersion);
    }

    [Theory]
    [InlineData("25.0.0")]
    [InlineData("26.0.1")]
    [InlineData("27.0.0")]
    [InlineData("28.0.1")]
    [InlineData("29.0.0-beta1")]
    public void ServerVersion_VariousVersionFormats_SetsCorrectly(string version) {
        // Arrange
        var capabilities = new ServerCapabilities();

        // Act
        capabilities.ServerVersion = version;

        // Assert
        Assert.Equal(version, capabilities.ServerVersion);
    }

    [Fact]
    public void ServerVersion_EmptyString_SetsCorrectly() {
        // Arrange
        var capabilities = new ServerCapabilities {
            ServerVersion = "1.0.0"
        };

        // Act
        capabilities.ServerVersion = "";

        // Assert
        Assert.Equal("", capabilities.ServerVersion);
    }

    [Fact]
    public void AllProperties_CanBeSetIndependently() {
        // Arrange
        var capabilities = new ServerCapabilities();

        // Act
        capabilities.IsNextcloud = true;
        capabilities.IsOcis = false;
        capabilities.ServerVersion = "Test";
        capabilities.SupportsChunking = true;
        capabilities.ChunkingVersion = 2;
        capabilities.SupportsOcisChunking = false;

        // Assert
        Assert.True(capabilities.IsNextcloud);
        Assert.False(capabilities.IsOcis);
        Assert.Equal("Test", capabilities.ServerVersion);
        Assert.True(capabilities.SupportsChunking);
        Assert.Equal(2, capabilities.ChunkingVersion);
        Assert.False(capabilities.SupportsOcisChunking);
    }

    [Fact]
    public void IsGenericWebDav_DynamicallyUpdates_WhenPropertiesChange() {
        // Arrange
        var capabilities = new ServerCapabilities();

        // Act & Assert - Initially generic WebDAV
        Assert.True(capabilities.IsGenericWebDav);

        // Set as Nextcloud
        capabilities.IsNextcloud = true;
        Assert.False(capabilities.IsGenericWebDav);

        // Revert
        capabilities.IsNextcloud = false;
        Assert.True(capabilities.IsGenericWebDav);

        // Set as OCIS
        capabilities.IsOcis = true;
        Assert.False(capabilities.IsGenericWebDav);

        // Revert
        capabilities.IsOcis = false;
        Assert.True(capabilities.IsGenericWebDav);
    }

    [Fact]
    public void NextcloudWithChunking_Version2_SetsAllPropertiesCorrectly() {
        // Arrange & Act
        var capabilities = new ServerCapabilities {
            IsNextcloud = true,
            ServerVersion = "28.0.1",
            SupportsChunking = true,
            ChunkingVersion = 2,
            SupportsOcisChunking = false
        };

        // Assert
        Assert.True(capabilities.IsNextcloud);
        Assert.False(capabilities.IsOcis);
        Assert.True(capabilities.SupportsChunking);
        Assert.Equal(2, capabilities.ChunkingVersion);
        Assert.False(capabilities.SupportsOcisChunking);
        Assert.False(capabilities.IsGenericWebDav);
    }

    [Fact]
    public void OcisWithTusProtocol_SetsAllPropertiesCorrectly() {
        // Arrange & Act
        var capabilities = new ServerCapabilities {
            IsOcis = true,
            ServerVersion = "5.0.0",
            SupportsOcisChunking = true,
            SupportsChunking = false,
            ChunkingVersion = 0
        };

        // Assert
        Assert.True(capabilities.IsOcis);
        Assert.False(capabilities.IsNextcloud);
        Assert.True(capabilities.SupportsOcisChunking);
        Assert.False(capabilities.SupportsChunking);
        Assert.Equal(0, capabilities.ChunkingVersion);
        Assert.False(capabilities.IsGenericWebDav);
    }
}
