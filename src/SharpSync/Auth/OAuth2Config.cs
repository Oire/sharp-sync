namespace Oire.SharpSync.Auth;

/// <summary>
/// OAuth2 configuration for different providers
/// </summary>
public record OAuth2Config {
    /// <summary>
    /// OAuth2 client ID
    /// </summary>
    public required string ClientId { get; init; }

    /// <summary>
    /// OAuth2 client secret (if applicable)
    /// </summary>
    public string? ClientSecret { get; init; }

    /// <summary>
    /// Authorization endpoint URL
    /// </summary>
    public required string AuthorizeUrl { get; init; }

    /// <summary>
    /// Token endpoint URL
    /// </summary>
    public required string TokenUrl { get; init; }

    /// <summary>
    /// Redirect URI for authorization callback
    /// </summary>
    public required string RedirectUri { get; init; }

    /// <summary>
    /// Requested OAuth scopes
    /// </summary>
    public string[] Scopes { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Additional parameters for authorization request
    /// </summary>
    public Dictionary<string, string> AdditionalParameters { get; init; } = new();

    /// <summary>
    /// Creates Nextcloud OAuth2 configuration
    /// </summary>
    public static OAuth2Config ForNextcloud(string serverUrl, string clientId, string redirectUri) {
        return new OAuth2Config {
            ClientId = clientId,
            AuthorizeUrl = $"{serverUrl.TrimEnd('/')}/apps/oauth2/authorize",
            TokenUrl = $"{serverUrl.TrimEnd('/')}/apps/oauth2/api/v1/token",
            RedirectUri = redirectUri,
            Scopes = new[] { "files" },
            AdditionalParameters = new Dictionary<string, string>
            {
                { "response_type", "code" }
            }
        };
    }

    /// <summary>
    /// Creates OCIS OAuth2 configuration  
    /// </summary>
    public static OAuth2Config ForOcis(string serverUrl, string clientId, string redirectUri) {
        return new OAuth2Config {
            ClientId = clientId,
            AuthorizeUrl = $"{serverUrl.TrimEnd('/')}/oauth2/auth",
            TokenUrl = $"{serverUrl.TrimEnd('/')}/oauth2/token",
            RedirectUri = redirectUri,
            Scopes = new[] { "openid", "profile", "offline_access" },
            AdditionalParameters = new Dictionary<string, string>
            {
                { "response_type", "code" }
            }
        };
    }
}
