using System.Security.Claims;
using Microsoft.Extensions.Configuration;
using OTT.Domain.Entities;
using OTT.Infrastructure.Services;
using Xunit;

namespace OTT.Tests;

public class JwtTokenServiceTests
{
    private static JwtTokenService NewService()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Secret"] = "unit_test_secret_key_at_least_64_characters_long_abcdefghijklmnop",
                ["Jwt:Issuer"] = "OTTPlatform",
                ["Jwt:Audience"] = "OTTPlatformUsers",
                ["Jwt:ExpiryMinutes"] = "60"
            })
            .Build();
        return new JwtTokenService(config);
    }

    private static User SampleUser() => new()
    {
        Id = Guid.NewGuid(),
        TenantId = Guid.NewGuid(),
        Email = "user@example.com",
        Role = "viewer",
        FirstName = "Ada",
        LastName = "Lovelace"
    };

    [Fact]
    public void GeneratedToken_RoundTrips_WithExpectedClaims()
    {
        var svc = NewService();
        var user = SampleUser();

        var token = svc.GenerateAccessToken(user);
        var principal = svc.ValidateToken(token);

        Assert.NotNull(principal);
        Assert.Equal(user.Id.ToString(), principal!.FindFirst(ClaimTypes.NameIdentifier)?.Value);
        Assert.Equal(user.Email, principal.FindFirst(ClaimTypes.Email)?.Value);
        Assert.Equal("viewer", principal.FindFirst(ClaimTypes.Role)?.Value);
        Assert.Equal(user.TenantId.ToString(), principal.FindFirst("tenant_id")?.Value);
    }

    [Fact]
    public void GeneratedToken_CarriesProfileId_WhenProvided()
    {
        var svc = NewService();
        var profileId = Guid.NewGuid().ToString();

        var token = svc.GenerateAccessToken(SampleUser(), profileId);
        var principal = svc.ValidateToken(token);

        Assert.Equal(profileId, principal!.FindFirst("profile_id")?.Value);
    }

    [Fact]
    public void TamperedToken_FailsValidation()
    {
        var svc = NewService();
        var token = svc.GenerateAccessToken(SampleUser());

        // Flip the last character of the signature.
        var tampered = token[..^1] + (token[^1] == 'A' ? 'B' : 'A');

        Assert.Null(svc.ValidateToken(tampered));
    }

    [Fact]
    public void RefreshToken_IsHighEntropyAndUnique()
    {
        var svc = NewService();
        var a = svc.GenerateRefreshToken();
        var b = svc.GenerateRefreshToken();

        Assert.NotEqual(a, b);
        Assert.True(a.Length >= 44); // 64 random bytes, base64
    }
}
