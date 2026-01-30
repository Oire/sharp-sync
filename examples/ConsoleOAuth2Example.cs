// =============================================================================
// SharpSync Console OAuth2 Provider Example
// =============================================================================
// This file demonstrates how to implement IOAuth2Provider for a console or
// headless application that needs to authenticate with Nextcloud or OCIS
// via OAuth2.
//
// The flow:
// 1. Open the user's default browser to the authorization URL
// 2. Listen on a local HTTP endpoint for the OAuth2 callback
// 3. Exchange the authorization code for tokens
// 4. Return the tokens to SharpSync for use with WebDavStorage
//
// Required NuGet packages:
//   - Oire.SharpSync
//   - System.Net.Http (included in .NET 8+)
// =============================================================================

using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using Oire.SharpSync.Auth;
using Oire.SharpSync.Core;
using Oire.SharpSync.Database;
using Oire.SharpSync.Storage;
using Oire.SharpSync.Sync;

namespace YourApp;

/// <summary>
/// A console-based OAuth2 provider that opens the system browser for
/// authorization and listens on localhost for the callback.
/// </summary>
/// <remarks>
/// <para>
/// This is a reference implementation. Production applications should:
/// </para>
/// <list type="bullet">
/// <item><description>Store tokens securely (e.g., Windows Credential Manager, macOS Keychain)</description></item>
/// <item><description>Handle token persistence across application restarts</description></item>
/// <item><description>Implement proper error handling for network failures</description></item>
/// <item><description>Use PKCE (Proof Key for Code Exchange) for public clients</description></item>
/// </list>
/// </remarks>
public class ConsoleOAuth2Provider: IOAuth2Provider {
    private readonly HttpClient _httpClient = new();

    /// <inheritdoc />
    public async Task<OAuth2Result> AuthenticateAsync(
        OAuth2Config config,
        CancellationToken cancellationToken = default) {
        // 1. Start local HTTP listener for the callback
        using var listener = new HttpListener();
        listener.Prefixes.Add(config.RedirectUri.EndsWith('/')
            ? config.RedirectUri
            : config.RedirectUri + "/");
        listener.Start();

        // 2. Build authorization URL
        var scopes = string.Join(" ", config.Scopes);
        var state = Guid.NewGuid().ToString("N");

        var authUrl = $"{config.AuthorizeUrl}"
            + $"?client_id={Uri.EscapeDataString(config.ClientId)}"
            + $"&redirect_uri={Uri.EscapeDataString(config.RedirectUri)}"
            + $"&response_type=code"
            + $"&scope={Uri.EscapeDataString(scopes)}"
            + $"&state={state}";

        // 3. Open the user's default browser
        Console.WriteLine("Opening browser for authentication...");
        Console.WriteLine($"If the browser doesn't open, navigate to:\n{authUrl}\n");
        OpenBrowser(authUrl);

        // 4. Wait for the OAuth2 callback
        Console.WriteLine("Waiting for authorization callback...");
        var context = await listener.GetContextAsync().WaitAsync(cancellationToken);
        var query = context.Request.QueryString;
        var code = query["code"];
        var returnedState = query["state"];

        // Send a success page to the browser
        var responseBytes = Encoding.UTF8.GetBytes(
            "<html><body><h2>Authorization successful!</h2>"
            + "<p>You can close this tab and return to the application.</p>"
            + "</body></html>");
        context.Response.ContentType = "text/html";
        context.Response.ContentLength64 = responseBytes.Length;
        await context.Response.OutputStream.WriteAsync(responseBytes, cancellationToken);
        context.Response.Close();

        // 5. Validate state parameter
        if (returnedState != state) {
            throw new InvalidOperationException(
                "OAuth2 state mismatch. Possible CSRF attack.");
        }

        if (string.IsNullOrEmpty(code)) {
            var error = query["error"] ?? "unknown";
            var description = query["error_description"] ?? "No authorization code received.";
            throw new InvalidOperationException(
                $"OAuth2 authorization failed: {error} - {description}");
        }

        // 6. Exchange authorization code for tokens
        return await ExchangeCodeForTokensAsync(config, code, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<OAuth2Result> RefreshTokenAsync(
        OAuth2Config config,
        string refreshToken,
        CancellationToken cancellationToken = default) {
        var parameters = new Dictionary<string, string> {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["client_id"] = config.ClientId,
        };

        if (!string.IsNullOrEmpty(config.ClientSecret)) {
            parameters["client_secret"] = config.ClientSecret;
        }

        var content = new FormUrlEncodedContent(parameters);
        var response = await _httpClient.PostAsync(config.TokenUrl, content, cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode) {
            throw new InvalidOperationException(
                $"Token refresh failed ({response.StatusCode}): {json}");
        }

        return ParseTokenResponse(json);
    }

    /// <inheritdoc />
    public async Task<bool> ValidateTokenAsync(
        OAuth2Result result,
        CancellationToken cancellationToken = default) {
        // Quick local check first
        if (!result.IsValid) {
            return false;
        }

        // Check if token will expire within 30 seconds
        if (result.WillExpireWithin(TimeSpan.FromSeconds(30))) {
            return false;
        }

        // Token appears valid based on expiry time.
        // A production implementation could make a lightweight API call
        // (e.g., GET /ocs/v2.php/cloud/user for Nextcloud) to verify
        // the token is actually accepted by the server.
        await Task.CompletedTask; // Placeholder for async API validation
        return true;
    }

    private async Task<OAuth2Result> ExchangeCodeForTokensAsync(
        OAuth2Config config,
        string code,
        CancellationToken cancellationToken) {
        var parameters = new Dictionary<string, string> {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = config.RedirectUri,
            ["client_id"] = config.ClientId,
        };

        if (!string.IsNullOrEmpty(config.ClientSecret)) {
            parameters["client_secret"] = config.ClientSecret;
        }

        var content = new FormUrlEncodedContent(parameters);
        var response = await _httpClient.PostAsync(config.TokenUrl, content, cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode) {
            throw new InvalidOperationException(
                $"Token exchange failed ({response.StatusCode}): {json}");
        }

        return ParseTokenResponse(json);
    }

    private static OAuth2Result ParseTokenResponse(string json) {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var accessToken = root.GetProperty("access_token").GetString()
            ?? throw new InvalidOperationException("Missing access_token in response.");

        var expiresIn = root.TryGetProperty("expires_in", out var exp)
            ? exp.GetInt32()
            : 3600;

        var refreshToken = root.TryGetProperty("refresh_token", out var rt)
            ? rt.GetString()
            : null;

        var tokenType = root.TryGetProperty("token_type", out var tt)
            ? tt.GetString() ?? "Bearer"
            : "Bearer";

        var userId = root.TryGetProperty("user_id", out var uid)
            ? uid.GetString()
            : null;

        return new OAuth2Result {
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            ExpiresAt = DateTime.UtcNow.AddSeconds(expiresIn),
            TokenType = tokenType,
            UserId = userId
        };
    }

    private static void OpenBrowser(string url) {
        try {
            // Cross-platform browser launch
            if (OperatingSystem.IsWindows()) {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            } else if (OperatingSystem.IsMacOS()) {
                Process.Start("open", url);
            } else {
                Process.Start("xdg-open", url);
            }
        } catch {
            // If browser launch fails, the user can manually navigate
            // (the URL was already printed to the console)
        }
    }
}

/// <summary>
/// Demonstrates using the ConsoleOAuth2Provider with WebDavStorage for Nextcloud sync.
/// </summary>
public static class OAuth2SyncExample {
    public static async Task RunAsync() {
        // --- Configuration ---
        var nextcloudUrl = "https://cloud.example.com";
        var clientId = "your-oauth2-client-id";
        var redirectUri = "http://localhost:9090/";
        var localSyncPath = "/path/to/local/sync/folder";
        var dbPath = "/path/to/sync-state.db";

        // 1. Create OAuth2 config for Nextcloud
        var oauthConfig = OAuth2Config.ForNextcloud(nextcloudUrl, clientId, redirectUri);
        Console.WriteLine($"Auth URL: {oauthConfig.AuthorizeUrl}");
        Console.WriteLine($"Token URL: {oauthConfig.TokenUrl}");

        // 2. Create the OAuth2 provider and authenticate
        var oauthProvider = new ConsoleOAuth2Provider();
        Console.WriteLine("Starting OAuth2 authentication...");
        var authResult = await oauthProvider.AuthenticateAsync(oauthConfig);
        Console.WriteLine($"Authenticated as: {authResult.UserId ?? "unknown"}");
        Console.WriteLine($"Token expires at: {authResult.ExpiresAt:u}");

        // 3. Create storage instances
        var localStorage = new LocalFileStorage(localSyncPath);
        var webDavUrl = $"{nextcloudUrl}/remote.php/dav/files/{authResult.UserId}/";
        var remoteStorage = new WebDavStorage(webDavUrl, oauth2Provider: oauthProvider);

        // 4. Create database and sync engine
        var database = new SqliteSyncDatabase(dbPath);
        await database.InitializeAsync();

        var filter = new SyncFilter();
        var resolver = new SmartConflictResolver(
            conflictHandler: async (analysis, ct) => {
                Console.WriteLine($"Conflict on: {analysis.FileName}");
                Console.WriteLine($"  Recommendation: {analysis.Recommendation}");
                Console.WriteLine($"  Reason: {analysis.ReasonForRecommendation}");
                return analysis.Recommendation;
            },
            defaultResolution: ConflictResolution.Ask);

        using var engine = new SyncEngine(
            localStorage, remoteStorage, database, filter, resolver);

        // 5. Wire up progress reporting
        engine.ProgressChanged += (s, e) =>
            Console.WriteLine($"[{e.Progress.Percentage:F0}%] {e.Operation}: {e.Progress.CurrentItem}");
        engine.FileProgressChanged += (s, e) =>
            Console.WriteLine($"  {e.Operation}: {e.Path} — {e.PercentComplete}%");

        // 6. Preview and sync
        var plan = await engine.GetSyncPlanAsync();
        Console.WriteLine($"\nSync plan: {plan.Summary}");

        if (plan.HasChanges) {
            Console.Write("Proceed with sync? [y/N] ");
            if (Console.ReadLine()?.Trim().ToLowerInvariant() == "y") {
                var result = await engine.SynchronizeAsync();
                Console.WriteLine($"Sync complete: {result.FilesSynchronized} files synced");
            }
        } else {
            Console.WriteLine("Everything is up to date.");
        }

        // 7. Token refresh example (for long-running applications)
        if (authResult.WillExpireWithin(TimeSpan.FromMinutes(5))
            && authResult.RefreshToken is not null) {
            Console.WriteLine("Token expiring soon, refreshing...");
            var newResult = await oauthProvider.RefreshTokenAsync(
                oauthConfig, authResult.RefreshToken);
            Console.WriteLine($"Token refreshed, new expiry: {newResult.ExpiresAt:u}");
        }
    }
}
