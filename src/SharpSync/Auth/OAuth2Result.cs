namespace Oire.SharpSync.Auth;

/// <summary>
/// OAuth2 authentication result
/// </summary>
public record OAuth2Result
{
    /// <summary>
    /// Access token for API calls
    /// </summary>
    public required string AccessToken { get; init; }

    /// <summary>
    /// Refresh token for token renewal
    /// </summary>
    public string? RefreshToken { get; init; }

    /// <summary>
    /// Token expiration time (UTC)
    /// </summary>
    public required DateTime ExpiresAt { get; init; }

    /// <summary>
    /// Token type (usually "Bearer")
    /// </summary>
    public string TokenType { get; init; } = "Bearer";

    /// <summary>
    /// Granted scopes
    /// </summary>
    public string[]? Scopes { get; init; }

    /// <summary>
    /// User identifier from the OAuth provider
    /// </summary>
    public string? UserId { get; init; }

    /// <summary>
    /// Whether the token is currently valid and not expired
    /// </summary>
    public bool IsValid => !string.IsNullOrEmpty(AccessToken) && DateTime.UtcNow < ExpiresAt;

    /// <summary>
    /// Whether the token will expire within the specified timespan
    /// </summary>
    public bool WillExpireWithin(TimeSpan timespan) => DateTime.UtcNow.Add(timespan) >= ExpiresAt;
}