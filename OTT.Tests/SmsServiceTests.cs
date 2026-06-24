using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using OTT.Application.Services;
using OTT.Infrastructure.Services;
using Xunit;

namespace OTT.Tests;

public class SmsServiceTests
{
    private sealed class NullSettings : IDynamicSettingsService
    {
        public Task<string?> GetAsync(Guid tenantId, string key, string? fallback = null) => Task.FromResult(fallback);
        public Task<bool> GetBoolAsync(Guid tenantId, string key, bool fallback = false) => Task.FromResult(fallback);
        public Task<Dictionary<string, string?>> GetAllAsync(Guid tenantId, bool publicOnly = false) => Task.FromResult(new Dictionary<string, string?>());
        public Task SetAsync(Guid tenantId, string key, string? value, bool isPublic = false) => Task.CompletedTask;
        public Task SetManyAsync(Guid tenantId, IReadOnlyDictionary<string, string?> values, bool isPublic = false) => Task.CompletedTask;
        public void Invalidate(Guid tenantId) { }
    }

    // Fails the test if any HTTP client is created — proves SMS short-circuits before the network
    // when the provider isn't configured.
    private sealed class ThrowingHttpFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => throw new Xunit.Sdk.XunitException("HTTP client must not be created when Twilio is unconfigured");
    }

    private static TwilioSmsService NewSut(IConfiguration config) =>
        new(new ThrowingHttpFactory(), config, new NullSettings(), NullLogger<TwilioSmsService>.Instance);

    [Fact]
    public void IsConfigured_FalseWhenNoCredentials()
    {
        var sut = NewSut(new ConfigurationBuilder().Build());
        Assert.False(sut.IsConfigured);
    }

    [Fact]
    public async Task Send_ReturnsFalseAndMakesNoCall_WhenUnconfigured()
    {
        var sut = NewSut(new ConfigurationBuilder().Build());
        var result = await sut.SendAsync("+15551234567", "code 123456");
        Assert.False(result); // and ThrowingHttpFactory was never invoked
    }

    [Fact]
    public void IsConfigured_TrueWhenCredentialsPresent()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Twilio:AccountSid"] = "AC123",
            ["Twilio:AuthToken"] = "secret"
        }).Build();
        Assert.True(NewSut(config).IsConfigured);
    }
}
