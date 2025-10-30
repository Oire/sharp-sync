namespace Oire.SharpSync.Auth;

/// <summary>
/// Interface for OAuth2 authentication providers
/// UI-free - implementations handle browser interaction
/// </summary>
public interface IOAuth2Provider {
    /// <summary>
    /// Initiates OAuth2 authentication flow
    /// Implementation should open browser and handle callback
    /// </summary>
    /// <param name="config">OAuth2 configuration</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Authentication result with tokens</returns>
    Task<OAuth2Result> AuthenticateAsync(OAuth2Config config, CancellationToken cancellationToken = default);

    /// <summary>
    /// Refreshes an expired access token using refresh token
    /// </summary>
    /// <param name="config">OAuth2 configuration</param>
    /// <param name="refreshToken">Refresh token</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>New authentication result</returns>
    Task<OAuth2Result> RefreshTokenAsync(OAuth2Config config, string refreshToken, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates if the current token is still valid
    /// May make a test API call to verify
    /// </summary>
    /// <param name="result">Current OAuth result</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if token is valid</returns>
    Task<bool> ValidateTokenAsync(OAuth2Result result, CancellationToken cancellationToken = default);
}
