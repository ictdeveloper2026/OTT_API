using OTT.Application.Services;
using OTT.Infrastructure.Services;
using Xunit;

namespace OTT.Tests;

public class FeatureFlagServiceTests
{
    // In-memory settings store keyed by (tenant, key).
    private sealed class MapSettings : IDynamicSettingsService
    {
        private readonly Dictionary<(Guid, string), string?> _v = new();
        public MapSettings Set(Guid tenant, string key, string? value) { _v[(tenant, key)] = value; return this; }

        public Task<string?> GetAsync(Guid tenantId, string key, string? fallback = null)
        {
            if (tenantId != Guid.Empty && _v.TryGetValue((tenantId, key), out var t) && !string.IsNullOrEmpty(t)) return Task.FromResult(t);
            if (_v.TryGetValue((Guid.Empty, key), out var g) && !string.IsNullOrEmpty(g)) return Task.FromResult(g);
            return Task.FromResult(fallback);
        }
        public Task<bool> GetBoolAsync(Guid tenantId, string key, bool fallback = false) => Task.FromResult(fallback);
        public Task<Dictionary<string, string?>> GetAllAsync(Guid tenantId, bool publicOnly = false) => Task.FromResult(new Dictionary<string, string?>());
        public Task SetAsync(Guid tenantId, string key, string? value, bool isPublic = false) { Set(tenantId, key, value); return Task.CompletedTask; }
        public Task SetManyAsync(Guid tenantId, IReadOnlyDictionary<string, string?> values, bool isPublic = false) => Task.CompletedTask;
        public void Invalidate(Guid tenantId) { }
    }

    private const string Flag = SettingKeys.FeatureDownloads;

    [Fact]
    public async Task FallbackUsed_WhenUnset()
    {
        var sut = new FeatureFlagService(new MapSettings());
        Assert.True(await sut.IsEnabledAsync(Flag, Guid.NewGuid(), Guid.NewGuid(), fallback: true));
        Assert.False(await sut.IsEnabledAsync(Flag, Guid.NewGuid(), Guid.NewGuid(), fallback: false));
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("1", true)]
    [InlineData("false", false)]
    [InlineData("0", false)]
    [InlineData("", false)] // empty acts as a kill-switch via fallback=false below
    public async Task PlainBoolean_IsHonoured(string value, bool expected)
    {
        var tenant = Guid.NewGuid();
        var sut = new FeatureFlagService(new MapSettings().Set(tenant, Flag, value));
        Assert.Equal(expected, await sut.IsEnabledAsync(Flag, tenant, Guid.NewGuid(), fallback: false));
    }

    [Fact]
    public async Task Rollout_IsDeterministicAndMonotonicallyAdds()
    {
        var tenant = Guid.NewGuid();
        var users = Enumerable.Range(0, 1000).Select(_ => Guid.NewGuid()).ToList();

        async Task<HashSet<Guid>> EnabledAt(int pct)
        {
            var sut = new FeatureFlagService(new MapSettings().Set(tenant, Flag, $"rollout:{pct}"));
            var set = new HashSet<Guid>();
            foreach (var u in users)
                if (await sut.IsEnabledAsync(Flag, tenant, u)) set.Add(u);
            return set;
        }

        var at10 = await EnabledAt(10);
        var at30 = await EnabledAt(30);

        // Roughly the configured share (deterministic hash, allow generous tolerance).
        Assert.InRange(at10.Count, 50, 170);
        Assert.InRange(at30.Count, 230, 370);
        // Ramping up only adds users — everyone enabled at 10% stays enabled at 30%.
        Assert.True(at10.IsSubsetOf(at30));
    }

    [Fact]
    public async Task JsonRule_KillSwitch_And_Targeting()
    {
        var tenant = Guid.NewGuid();
        var vip = Guid.NewGuid();
        var banned = Guid.NewGuid();

        // Disabled globally, but explicitly allowed for one user.
        var sut = new FeatureFlagService(new MapSettings().Set(tenant, Flag,
            $$"""{"enabled":true,"rollout":0,"allowUsers":["{{vip}}"],"blockUsers":["{{banned}}"]}"""));

        Assert.True(await sut.IsEnabledAsync(Flag, tenant, vip));          // allow-list wins
        Assert.False(await sut.IsEnabledAsync(Flag, tenant, banned));      // block-list wins
        Assert.False(await sut.IsEnabledAsync(Flag, tenant, Guid.NewGuid())); // 0% rollout, not listed

        // Hard kill-switch disables even allow-listed users.
        var killed = new FeatureFlagService(new MapSettings().Set(tenant, Flag,
            $$"""{"enabled":false,"allowUsers":["{{vip}}"]}"""));
        Assert.False(await killed.IsEnabledAsync(Flag, tenant, vip));
    }
}
