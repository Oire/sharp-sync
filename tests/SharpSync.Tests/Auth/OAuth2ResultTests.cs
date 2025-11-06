using Oire.SharpSync.Auth;

namespace Oire.SharpSync.Tests.Auth;

public class OAuth2ResultTests {
    [Fact]
    public void Constructor_RequiredProperties_InitializesCorrectly() {
        // Arrange
        var accessToken = "test_access_token";
        var expiresAt = DateTime.UtcNow.AddHours(1);

        // Act
        var result = new OAuth2Result {
            AccessToken = accessToken,
            ExpiresAt = expiresAt
        };

        // Assert
        Assert.Equal(accessToken, result.AccessToken);
        Assert.Equal(expiresAt, result.ExpiresAt);
    }

    [Fact]
    public void Constructor_AllProperties_InitializesCorrectly() {
        // Arrange
        var accessToken = "test_access_token";
        var refreshToken = "test_refresh_token";
        var expiresAt = DateTime.UtcNow.AddHours(1);
        var tokenType = "Bearer";
        var scopes = new[] { "read", "write" };
        var userId = "user123";

        // Act
        var result = new OAuth2Result {
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            ExpiresAt = expiresAt,
            TokenType = tokenType,
            Scopes = scopes,
            UserId = userId
        };

        // Assert
        Assert.Equal(accessToken, result.AccessToken);
        Assert.Equal(refreshToken, result.RefreshToken);
        Assert.Equal(expiresAt, result.ExpiresAt);
        Assert.Equal(tokenType, result.TokenType);
        Assert.Equal(scopes, result.Scopes);
        Assert.Equal(userId, result.UserId);
    }

    [Fact]
    public void TokenType_DefaultValue_IsBearer() {
        // Arrange & Act
        var result = new OAuth2Result {
            AccessToken = "test_token",
            ExpiresAt = DateTime.UtcNow.AddHours(1)
        };

        // Assert
        Assert.Equal("Bearer", result.TokenType);
    }

    [Fact]
    public void IsValid_WithValidToken_ReturnsTrue() {
        // Arrange
        var result = new OAuth2Result {
            AccessToken = "valid_token",
            ExpiresAt = DateTime.UtcNow.AddHours(1)
        };

        // Act & Assert
        Assert.True(result.IsValid);
    }

    [Fact]
    public void IsValid_WithExpiredToken_ReturnsFalse() {
        // Arrange
        var result = new OAuth2Result {
            AccessToken = "expired_token",
            ExpiresAt = DateTime.UtcNow.AddHours(-1) // Expired 1 hour ago
        };

        // Act & Assert
        Assert.False(result.IsValid);
    }

    [Fact]
    public void IsValid_WithEmptyAccessToken_ReturnsFalse() {
        // Arrange
        var result = new OAuth2Result {
            AccessToken = "",
            ExpiresAt = DateTime.UtcNow.AddHours(1)
        };

        // Act & Assert
        Assert.False(result.IsValid);
    }

    [Fact]
    public void IsValid_WithNullAccessToken_ReturnsFalse() {
        // Arrange
        var result = new OAuth2Result {
            AccessToken = null!,
            ExpiresAt = DateTime.UtcNow.AddHours(1)
        };

        // Act & Assert
        Assert.False(result.IsValid);
    }

    [Fact]
    public void IsValid_ExpiringNow_ReturnsFalse() {
        // Arrange - token expires in 1 second
        var result = new OAuth2Result {
            AccessToken = "test_token",
            ExpiresAt = DateTime.UtcNow.AddSeconds(1)
        };

        // Act
        Thread.Sleep(1100); // Wait for expiration

        // Assert
        Assert.False(result.IsValid);
    }

    [Fact]
    public void WillExpireWithin_BeforeExpiration_ReturnsTrue() {
        // Arrange
        var result = new OAuth2Result {
            AccessToken = "test_token",
            ExpiresAt = DateTime.UtcNow.AddMinutes(30)
        };

        // Act & Assert
        Assert.True(result.WillExpireWithin(TimeSpan.FromHours(1)));
    }

    [Fact]
    public void WillExpireWithin_AfterExpiration_ReturnsFalse() {
        // Arrange
        var result = new OAuth2Result {
            AccessToken = "test_token",
            ExpiresAt = DateTime.UtcNow.AddHours(2)
        };

        // Act & Assert
        Assert.False(result.WillExpireWithin(TimeSpan.FromHours(1)));
    }

    [Fact]
    public void WillExpireWithin_ExactlyAtExpiration_ReturnsTrue() {
        // Arrange
        var expiresAt = DateTime.UtcNow.AddHours(1);
        var result = new OAuth2Result {
            AccessToken = "test_token",
            ExpiresAt = expiresAt
        };

        // Act & Assert
        Assert.True(result.WillExpireWithin(TimeSpan.FromHours(1)));
    }

    [Fact]
    public void WillExpireWithin_AlreadyExpired_ReturnsTrue() {
        // Arrange
        var result = new OAuth2Result {
            AccessToken = "test_token",
            ExpiresAt = DateTime.UtcNow.AddHours(-1) // Already expired
        };

        // Act & Assert
        Assert.True(result.WillExpireWithin(TimeSpan.FromMinutes(30)));
    }

    [Theory]
    [InlineData(5)]
    [InlineData(10)]
    [InlineData(15)]
    [InlineData(30)]
    [InlineData(60)]
    public void WillExpireWithin_VariousTimeSpans_WorksCorrectly(int minutes) {
        // Arrange
        var result = new OAuth2Result {
            AccessToken = "test_token",
            ExpiresAt = DateTime.UtcNow.AddMinutes(minutes - 1) // Will expire before the threshold
        };

        // Act & Assert
        Assert.True(result.WillExpireWithin(TimeSpan.FromMinutes(minutes)));
    }

    [Fact]
    public void RefreshToken_Null_IsAllowed() {
        // Arrange & Act
        var result = new OAuth2Result {
            AccessToken = "test_token",
            ExpiresAt = DateTime.UtcNow.AddHours(1),
            RefreshToken = null
        };

        // Assert
        Assert.Null(result.RefreshToken);
    }

    [Fact]
    public void Scopes_Null_IsAllowed() {
        // Arrange & Act
        var result = new OAuth2Result {
            AccessToken = "test_token",
            ExpiresAt = DateTime.UtcNow.AddHours(1),
            Scopes = null
        };

        // Assert
        Assert.Null(result.Scopes);
    }

    [Fact]
    public void Scopes_EmptyArray_IsAllowed() {
        // Arrange & Act
        var result = new OAuth2Result {
            AccessToken = "test_token",
            ExpiresAt = DateTime.UtcNow.AddHours(1),
            Scopes = Array.Empty<string>()
        };

        // Assert
        Assert.Empty(result.Scopes);
    }

    [Fact]
    public void Scopes_MultipleScopes_StoresCorrectly() {
        // Arrange
        var scopes = new[] { "read", "write", "delete", "admin" };

        // Act
        var result = new OAuth2Result {
            AccessToken = "test_token",
            ExpiresAt = DateTime.UtcNow.AddHours(1),
            Scopes = scopes
        };

        // Assert
        Assert.NotNull(result.Scopes);
        Assert.Equal(4, result.Scopes.Length);
        Assert.Contains("read", result.Scopes);
        Assert.Contains("write", result.Scopes);
        Assert.Contains("delete", result.Scopes);
        Assert.Contains("admin", result.Scopes);
    }

    [Fact]
    public void UserId_Null_IsAllowed() {
        // Arrange & Act
        var result = new OAuth2Result {
            AccessToken = "test_token",
            ExpiresAt = DateTime.UtcNow.AddHours(1),
            UserId = null
        };

        // Assert
        Assert.Null(result.UserId);
    }

    [Fact]
    public void Record_WithWith_CreatesNewInstance() {
        // Arrange
        var original = new OAuth2Result {
            AccessToken = "old_token",
            ExpiresAt = DateTime.UtcNow.AddHours(1),
            RefreshToken = "refresh"
        };

        // Act
        var modified = original with { AccessToken = "new_token" };

        // Assert
        Assert.Equal("old_token", original.AccessToken);
        Assert.Equal("new_token", modified.AccessToken);
        Assert.Equal(original.RefreshToken, modified.RefreshToken);
        Assert.Equal(original.ExpiresAt, modified.ExpiresAt);
    }

    [Fact]
    public void Record_Equality_WorksCorrectly() {
        // Arrange
        var expiresAt = DateTime.UtcNow.AddHours(1);
        var result1 = new OAuth2Result {
            AccessToken = "test_token",
            ExpiresAt = expiresAt,
            RefreshToken = "refresh",
            TokenType = "Bearer"
        };

        var result2 = new OAuth2Result {
            AccessToken = "test_token",
            ExpiresAt = expiresAt,
            RefreshToken = "refresh",
            TokenType = "Bearer"
        };

        // Act & Assert
        Assert.Equal(result1, result2);
    }

    [Theory]
    [InlineData("Bearer")]
    [InlineData("Token")]
    [InlineData("Basic")]
    public void TokenType_CustomValues_SetsCorrectly(string tokenType) {
        // Arrange & Act
        var result = new OAuth2Result {
            AccessToken = "test_token",
            ExpiresAt = DateTime.UtcNow.AddHours(1),
            TokenType = tokenType
        };

        // Assert
        Assert.Equal(tokenType, result.TokenType);
    }

    [Fact]
    public void IsValid_JustBeforeExpiry_ReturnsTrue() {
        // Arrange
        var result = new OAuth2Result {
            AccessToken = "test_token",
            ExpiresAt = DateTime.UtcNow.AddSeconds(5)
        };

        // Act & Assert
        Assert.True(result.IsValid);
    }

    [Fact]
    public void ExpiresAt_UtcTime_StoresCorrectly() {
        // Arrange
        var expiresAt = new DateTime(2025, 12, 31, 23, 59, 59, DateTimeKind.Utc);

        // Act
        var result = new OAuth2Result {
            AccessToken = "test_token",
            ExpiresAt = expiresAt
        };

        // Assert
        Assert.Equal(expiresAt, result.ExpiresAt);
        Assert.Equal(DateTimeKind.Utc, result.ExpiresAt.Kind);
    }

    [Fact]
    public void WillExpireWithin_ZeroTimeSpan_ReturnsTrueIfExpired() {
        // Arrange
        var result = new OAuth2Result {
            AccessToken = "test_token",
            ExpiresAt = DateTime.UtcNow.AddSeconds(-1)
        };

        // Act & Assert
        Assert.True(result.WillExpireWithin(TimeSpan.Zero));
    }

    [Fact]
    public void WillExpireWithin_NegativeTimeSpan_HandlesProperly() {
        // Arrange
        var result = new OAuth2Result {
            AccessToken = "test_token",
            ExpiresAt = DateTime.UtcNow.AddHours(1)
        };

        // Act & Assert
        // A negative timespan means "already expired" from current perspective
        Assert.False(result.WillExpireWithin(TimeSpan.FromHours(-1)));
    }
}
