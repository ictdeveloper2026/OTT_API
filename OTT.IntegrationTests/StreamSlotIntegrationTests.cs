using Microsoft.Extensions.Logging.Abstractions;
using OTT.Infrastructure.Services;
using Xunit;

namespace OTT.IntegrationTests;

/// <summary>
/// Validates the atomic concurrent-stream slot logic (a Lua script via ScriptEvaluate) on a real
/// Redis. Unit tests can only fake the semantics; this proves the actual script admits/rejects and
/// expires correctly.
/// </summary>
[Collection("containers")]
public class StreamSlotIntegrationTests
{
    private readonly ContainerFixture _fx;
    public StreamSlotIntegrationTests(ContainerFixture fx) => _fx = fx;

    private RedisCacheService NewCache() => new(_fx.Redis, NullLogger<RedisCacheService>.Instance);

    [Fact]
    public async Task AcquireSlot_AdmitsUpToMax_ThenRejects()
    {
        var cache = NewCache();
        var key = $"streams:{Guid.NewGuid()}";
        var ttl = TimeSpan.FromMinutes(2);

        var a = await cache.TryAcquireStreamSlotAsync(key, "session-a", maxConcurrent: 2, ttl);
        var b = await cache.TryAcquireStreamSlotAsync(key, "session-b", maxConcurrent: 2, ttl);
        var c = await cache.TryAcquireStreamSlotAsync(key, "session-c", maxConcurrent: 2, ttl);

        Assert.Equal(1, a);
        Assert.Equal(2, b);
        Assert.Equal(-1, c); // at capacity

        // Re-admitting an existing member is always allowed and doesn't grow the count.
        var aAgain = await cache.TryAcquireStreamSlotAsync(key, "session-a", maxConcurrent: 2, ttl);
        Assert.Equal(2, aAgain);
    }

    [Fact]
    public async Task ReleaseSlot_FreesCapacity()
    {
        var cache = NewCache();
        var key = $"streams:{Guid.NewGuid()}";
        var ttl = TimeSpan.FromMinutes(2);

        await cache.TryAcquireStreamSlotAsync(key, "s1", maxConcurrent: 1, ttl);
        Assert.Equal(-1, await cache.TryAcquireStreamSlotAsync(key, "s2", maxConcurrent: 1, ttl));

        await cache.ReleaseStreamSlotAsync(key, "s1");
        Assert.Equal(1, await cache.TryAcquireStreamSlotAsync(key, "s2", maxConcurrent: 1, ttl));
    }
}
