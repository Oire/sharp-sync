using Oire.SharpSync.Auth;

namespace Oire.SharpSync.Tests.Auth;

public class OAuth2ConfigTests {
    [Fact]
    public void Constructor_RequiredProperties_InitializesCorrectly() {
        // Arrange
        var clientId = "test_client_id";
        var authorizeUrl = "https://example.com/oauth/authorize";
        var tokenUrl = "https://example.com/oauth/token";
        var redirectUri = "https://app.example.com/callback";

        // Act
        var config = new OAuth2Config {
            ClientId = clientId,
            AuthorizeUrl = authorizeUrl,
            TokenUrl = tokenUrl,
            RedirectUri = redirectUri
        };

        // Assert
        Assert.Equal(clientId, config.ClientId);
        Assert.Equal(authorizeUrl, config.AuthorizeUrl);
        Assert.Equal(tokenUrl, config.TokenUrl);
        Assert.Equal(redirectUri, config.RedirectUri);
    }

    [Fact]
    public void Constructor_AllProperties_InitializesCorrectly() {
        // Arrange
        var clientId = "test_client_id";
        var clientSecret = "test_client_secret";
        var authorizeUrl = "https://example.com/oauth/authorize";
        var tokenUrl = "https://example.com/oauth/token";
        var redirectUri = "https://app.example.com/callback";
        var scopes = new[] { "read", "write" };
        var additionalParams = new Dictionary<string, string> {
            { "response_type", "code" },
            { "custom_param", "value" }
        };

        // Act
        var config = new OAuth2Config {
            ClientId = clientId,
            ClientSecret = clientSecret,
            AuthorizeUrl = authorizeUrl,
            TokenUrl = tokenUrl,
            RedirectUri = redirectUri,
            Scopes = scopes,
            AdditionalParameters = additionalParams
        };

        // Assert
        Assert.Equal(clientId, config.ClientId);
        Assert.Equal(clientSecret, config.ClientSecret);
        Assert.Equal(authorizeUrl, config.AuthorizeUrl);
        Assert.Equal(tokenUrl, config.TokenUrl);
        Assert.Equal(redirectUri, config.RedirectUri);
        Assert.Equal(scopes, config.Scopes);
        Assert.Equal(additionalParams, config.AdditionalParameters);
    }

    [Fact]
    public void Scopes_DefaultValue_IsEmpty() {
        // Arrange & Act
        var config = new OAuth2Config {
            ClientId = "test_client",
            AuthorizeUrl = "https://example.com/authorize",
            TokenUrl = "https://example.com/token",
            RedirectUri = "https://app.example.com/callback"
        };

        // Assert
        Assert.Empty(config.Scopes);
    }

    [Fact]
    public void AdditionalParameters_DefaultValue_IsEmpty() {
        // Arrange & Act
        var config = new OAuth2Config {
            ClientId = "test_client",
            AuthorizeUrl = "https://example.com/authorize",
            TokenUrl = "https://example.com/token",
            RedirectUri = "https://app.example.com/callback"
        };

        // Assert
        Assert.Empty(config.AdditionalParameters);
    }

    [Fact]
    public void ClientSecret_Null_IsAllowed() {
        // Arrange & Act
        var config = new OAuth2Config {
            ClientId = "test_client",
            ClientSecret = null,
            AuthorizeUrl = "https://example.com/authorize",
            TokenUrl = "https://example.com/token",
            RedirectUri = "https://app.example.com/callback"
        };

        // Assert
        Assert.Null(config.ClientSecret);
    }

    [Fact]
    public void ForNextcloud_CreatesValidConfiguration() {
        // Arrange
        var serverUrl = "https://cloud.example.com";
        var clientId = "nextcloud_client";
        var redirectUri = "https://app.example.com/callback";

        // Act
        var config = OAuth2Config.ForNextcloud(serverUrl, clientId, redirectUri);

        // Assert
        Assert.Equal(clientId, config.ClientId);
        Assert.Equal("https://cloud.example.com/apps/oauth2/authorize", config.AuthorizeUrl);
        Assert.Equal("https://cloud.example.com/apps/oauth2/api/v1/token", config.TokenUrl);
        Assert.Equal(redirectUri, config.RedirectUri);
        Assert.Single(config.Scopes);
        Assert.Contains("files", config.Scopes);
        Assert.Contains("response_type", config.AdditionalParameters.Keys);
        Assert.Equal("code", config.AdditionalParameters["response_type"]);
    }

    [Fact]
    public void ForNextcloud_WithTrailingSlash_TrimsCorrectly() {
        // Arrange
        var serverUrl = "https://cloud.example.com/";
        var clientId = "nextcloud_client";
        var redirectUri = "https://app.example.com/callback";

        // Act
        var config = OAuth2Config.ForNextcloud(serverUrl, clientId, redirectUri);

        // Assert
        Assert.Equal("https://cloud.example.com/apps/oauth2/authorize", config.AuthorizeUrl);
        Assert.Equal("https://cloud.example.com/apps/oauth2/api/v1/token", config.TokenUrl);
    }

    [Fact]
    public void ForNextcloud_WithoutTrailingSlash_WorksCorrectly() {
        // Arrange
        var serverUrl = "https://cloud.example.com";
        var clientId = "nextcloud_client";
        var redirectUri = "https://app.example.com/callback";

        // Act
        var config = OAuth2Config.ForNextcloud(serverUrl, clientId, redirectUri);

        // Assert
        Assert.Equal("https://cloud.example.com/apps/oauth2/authorize", config.AuthorizeUrl);
        Assert.Equal("https://cloud.example.com/apps/oauth2/api/v1/token", config.TokenUrl);
    }

    [Fact]
    public void ForOcis_CreatesValidConfiguration() {
        // Arrange
        var serverUrl = "https://ocis.example.com";
        var clientId = "ocis_client";
        var redirectUri = "https://app.example.com/callback";

        // Act
        var config = OAuth2Config.ForOcis(serverUrl, clientId, redirectUri);

        // Assert
        Assert.Equal(clientId, config.ClientId);
        Assert.Equal("https://ocis.example.com/oauth2/auth", config.AuthorizeUrl);
        Assert.Equal("https://ocis.example.com/oauth2/token", config.TokenUrl);
        Assert.Equal(redirectUri, config.RedirectUri);
        Assert.Equal(3, config.Scopes.Length);
        Assert.Contains("openid", config.Scopes);
        Assert.Contains("profile", config.Scopes);
        Assert.Contains("offline_access", config.Scopes);
        Assert.Contains("response_type", config.AdditionalParameters.Keys);
        Assert.Equal("code", config.AdditionalParameters["response_type"]);
    }

    [Fact]
    public void ForOcis_WithTrailingSlash_TrimsCorrectly() {
        // Arrange
        var serverUrl = "https://ocis.example.com/";
        var clientId = "ocis_client";
        var redirectUri = "https://app.example.com/callback";

        // Act
        var config = OAuth2Config.ForOcis(serverUrl, clientId, redirectUri);

        // Assert
        Assert.Equal("https://ocis.example.com/oauth2/auth", config.AuthorizeUrl);
        Assert.Equal("https://ocis.example.com/oauth2/token", config.TokenUrl);
    }

    [Fact]
    public void ForOcis_WithoutTrailingSlash_WorksCorrectly() {
        // Arrange
        var serverUrl = "https://ocis.example.com";
        var clientId = "ocis_client";
        var redirectUri = "https://app.example.com/callback";

        // Act
        var config = OAuth2Config.ForOcis(serverUrl, clientId, redirectUri);

        // Assert
        Assert.Equal("https://ocis.example.com/oauth2/auth", config.AuthorizeUrl);
        Assert.Equal("https://ocis.example.com/oauth2/token", config.TokenUrl);
    }

    [Fact]
    public void ForNextcloud_AndForOcis_HaveDifferentScopes() {
        // Arrange & Act
        var nextcloudConfig = OAuth2Config.ForNextcloud(
            "https://nextcloud.example.com",
            "client1",
            "http://localhost/callback"
        );

        var ocisConfig = OAuth2Config.ForOcis(
            "https://ocis.example.com",
            "client2",
            "http://localhost/callback"
        );

        // Assert
        Assert.NotEqual(nextcloudConfig.Scopes, ocisConfig.Scopes);
        Assert.Single(nextcloudConfig.Scopes);
        Assert.Equal(3, ocisConfig.Scopes.Length);
    }

    [Fact]
    public void Record_WithWith_CreatesNewInstance() {
        // Arrange
        var original = new OAuth2Config {
            ClientId = "old_client",
            AuthorizeUrl = "https://old.com/authorize",
            TokenUrl = "https://old.com/token",
            RedirectUri = "https://app.com/callback"
        };

        // Act
        var modified = original with { ClientId = "new_client" };

        // Assert
        Assert.Equal("old_client", original.ClientId);
        Assert.Equal("new_client", modified.ClientId);
        Assert.Equal(original.AuthorizeUrl, modified.AuthorizeUrl);
        Assert.Equal(original.TokenUrl, modified.TokenUrl);
    }

    [Fact]
    public void Record_Equality_WorksCorrectly() {
        // Arrange
        var config1 = new OAuth2Config {
            ClientId = "test_client",
            AuthorizeUrl = "https://example.com/authorize",
            TokenUrl = "https://example.com/token",
            RedirectUri = "https://app.example.com/callback"
        };

        var config2 = new OAuth2Config {
            ClientId = "test_client",
            AuthorizeUrl = "https://example.com/authorize",
            TokenUrl = "https://example.com/token",
            RedirectUri = "https://app.example.com/callback"
        };

        // Act & Assert
        Assert.Equal(config1, config2);
    }

    [Fact]
    public void Scopes_MultipleScopes_StoresCorrectly() {
        // Arrange
        var scopes = new[] { "read", "write", "delete", "admin" };

        // Act
        var config = new OAuth2Config {
            ClientId = "test_client",
            AuthorizeUrl = "https://example.com/authorize",
            TokenUrl = "https://example.com/token",
            RedirectUri = "https://app.example.com/callback",
            Scopes = scopes
        };

        // Assert
        Assert.Equal(4, config.Scopes.Length);
        Assert.Contains("read", config.Scopes);
        Assert.Contains("write", config.Scopes);
        Assert.Contains("delete", config.Scopes);
        Assert.Contains("admin", config.Scopes);
    }

    [Fact]
    public void AdditionalParameters_MultipleParameters_StoresCorrectly() {
        // Arrange
        var additionalParams = new Dictionary<string, string> {
            { "response_type", "code" },
            { "access_type", "offline" },
            { "prompt", "consent" }
        };

        // Act
        var config = new OAuth2Config {
            ClientId = "test_client",
            AuthorizeUrl = "https://example.com/authorize",
            TokenUrl = "https://example.com/token",
            RedirectUri = "https://app.example.com/callback",
            AdditionalParameters = additionalParams
        };

        // Assert
        Assert.Equal(3, config.AdditionalParameters.Count);
        Assert.Equal("code", config.AdditionalParameters["response_type"]);
        Assert.Equal("offline", config.AdditionalParameters["access_type"]);
        Assert.Equal("consent", config.AdditionalParameters["prompt"]);
    }

    [Theory]
    [InlineData("http://localhost:8080/callback")]
    [InlineData("https://app.example.com/oauth/callback")]
    [InlineData("myapp://oauth-callback")]
    public void RedirectUri_VariousFormats_AcceptsCorrectly(string redirectUri) {
        // Arrange & Act
        var config = new OAuth2Config {
            ClientId = "test_client",
            AuthorizeUrl = "https://example.com/authorize",
            TokenUrl = "https://example.com/token",
            RedirectUri = redirectUri
        };

        // Assert
        Assert.Equal(redirectUri, config.RedirectUri);
    }

    [Fact]
    public void ForNextcloud_WithComplexServerUrl_HandlesCorrectly() {
        // Arrange
        var serverUrl = "https://cloud.example.com:8443/nextcloud";

        // Act
        var config = OAuth2Config.ForNextcloud(serverUrl, "client", "http://localhost/callback");

        // Assert
        Assert.Equal("https://cloud.example.com:8443/nextcloud/apps/oauth2/authorize", config.AuthorizeUrl);
        Assert.Equal("https://cloud.example.com:8443/nextcloud/apps/oauth2/api/v1/token", config.TokenUrl);
    }

    [Fact]
    public void ForOcis_WithComplexServerUrl_HandlesCorrectly() {
        // Arrange
        var serverUrl = "https://ocis.example.com:9200";

        // Act
        var config = OAuth2Config.ForOcis(serverUrl, "client", "http://localhost/callback");

        // Assert
        Assert.Equal("https://ocis.example.com:9200/oauth2/auth", config.AuthorizeUrl);
        Assert.Equal("https://ocis.example.com:9200/oauth2/token", config.TokenUrl);
    }
}
