using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OTT.Application.Services;
using OTT.Infrastructure.Data;
using OTT.Infrastructure.Services;
using Xunit;

namespace OTT.Tests;

// Covers the first-party analytics producers added to ContentService: granular playback events
// and the watch_progress delta that feeds ContentAnalytics.TotalWatchSeconds.
public class ContentServiceAnalyticsTests
{
    private static OttDbContext NewDb() =>
        new(new DbContextOptionsBuilder<OttDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private sealed class NoCdn : ICloudFrontCdnService
    {
        public string GetSignedUrl(string s3Key, int expiryMinutes = 360) => s3Key;
        public string GetSignedHlsUrl(string contentId, string quality, int expiryMinutes = 360) => contentId;
        public string GetThumbnailUrl(string s3Key) => s3Key;
        public string GetPublicUrl(string s3Key) => s3Key;
    }

    // Captures analytics buffer pushes and backs the string get/set used for delta tracking.
    private sealed class CapturingCache : IRedisCacheService
    {
        public readonly List<string> Pushes = new();
        private readonly Dictionary<string, string> _strings = new();

        public List<AnalyticsEventBuffer> Events(string? type = null) => Pushes
            .Select(p => JsonSerializer.Deserialize<AnalyticsEventBuffer>(p)!)
            .Where(e => type == null || e.EventType == type)
            .ToList();

        public Task ListPushAsync(string key, string value) { Pushes.Add(value); return Task.CompletedTask; }
        public Task<string?> GetStringAsync(string key) => Task.FromResult(_strings.TryGetValue(key, out var v) ? v : null);
        public Task SetStringAsync(string key, string value, TimeSpan? expiry = null) { _strings[key] = value; return Task.CompletedTask; }

        public Task<T?> GetAsync<T>(string key) where T : class => Task.FromResult<T?>(null);
        public Task SetAsync<T>(string key, T value, TimeSpan? expiry = null) where T : class => Task.CompletedTask;
        public Task RemoveAsync(string key) => Task.CompletedTask;
        public Task RemoveByPatternAsync(string pattern) => Task.CompletedTask;
        public Task<bool> ExistsAsync(string key) => Task.FromResult(false);
        public Task<long> IncrementAsync(string key, TimeSpan? expiry = null) => Task.FromResult(0L);
        public Task HashIncrementAsync(string key, string field, long value = 1) => Task.CompletedTask;
        public Task HashSetAsync(string key, string field, string value) => Task.CompletedTask;
        public Task<Dictionary<string, string>> HashGetAllAndClearAsync(string key) => Task.FromResult(new Dictionary<string, string>());
        public Task<List<string>> ListDrainAsync(string key, int max) => Task.FromResult(new List<string>());
        public Task<long> TryAcquireStreamSlotAsync(string key, string member, int maxConcurrent, TimeSpan ttl) => Task.FromResult(1L);
        public Task RenewStreamSlotAsync(string key, string member, TimeSpan ttl) => Task.CompletedTask;
        public Task ReleaseStreamSlotAsync(string key, string member) => Task.CompletedTask;
    }

    private static (ContentService svc, CapturingCache cache) NewService()
    {
        var cache = new CapturingCache();
        return (new ContentService(NewDb(), new NoCdn(), cache, NullLogger<ContentService>.Instance), cache);
    }

    private static ClientContext Ctx(Guid tenantId) => new(tenantId, "android", "mobile", "IN");

    [Fact]
    public async Task RecordPlaybackEvent_Pause_BuffersEventWithPositionAndContext()
    {
        var (svc, cache) = NewService();
        var tenant = Guid.NewGuid();
        var profile = Guid.NewGuid();
        var content = Guid.NewGuid();

        await svc.RecordPlaybackEventAsync(profile, content, "pause", 123, null, null, Ctx(tenant));

        var evt = Assert.Single(cache.Events("pause"));
        Assert.Equal(tenant, evt.TenantId);
        Assert.Equal(profile, evt.ViewerId);
        Assert.Equal(content, evt.ContentId);
        Assert.Equal("android", evt.Platform);
        Assert.Equal("mobile", evt.DeviceType);
        Assert.Equal("IN", evt.Country);
        Assert.Contains("\"pos\":123", evt.ExtraData);
    }

    [Fact]
    public async Task RecordPlaybackEvent_Seek_CapturesTarget()
    {
        var (svc, cache) = NewService();
        await svc.RecordPlaybackEventAsync(Guid.NewGuid(), Guid.NewGuid(), "seek", 100, 250, null, Ctx(Guid.NewGuid()));

        var evt = Assert.Single(cache.Events("seek"));
        Assert.Contains("\"pos\":100", evt.ExtraData);
        Assert.Contains("\"to\":250", evt.ExtraData);
    }

    [Fact]
    public async Task RecordPlaybackEvent_UnknownType_Throws()
    {
        var (svc, _) = NewService();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            svc.RecordPlaybackEventAsync(Guid.NewGuid(), Guid.NewGuid(), "explode", 0, null, null, Ctx(Guid.NewGuid())));
    }

    [Fact]
    public async Task UpdateWatchProgress_EmitsDeltaBetweenPings()
    {
        var (svc, cache) = NewService();
        var tenant = Guid.NewGuid();
        var profile = Guid.NewGuid();
        var content = Guid.NewGuid();

        // First ping establishes the baseline — no watch time can be inferred yet.
        await svc.UpdateWatchProgressAsync(profile, content, 10, 3600, null, Ctx(tenant));
        Assert.Empty(cache.Events(AnalyticsEventTypes.WatchProgress));

        // Second ping 25s later credits exactly the elapsed 25s of viewing.
        await svc.UpdateWatchProgressAsync(profile, content, 35, 3600, null, Ctx(tenant));
        var evt = Assert.Single(cache.Events(AnalyticsEventTypes.WatchProgress));
        Assert.Equal(25, evt.WatchDurationSeconds);
        Assert.Equal(tenant, evt.TenantId);
    }

    [Fact]
    public async Task UpdateWatchProgress_ClampsForwardSeek()
    {
        var (svc, cache) = NewService();
        var tenant = Guid.NewGuid();
        var profile = Guid.NewGuid();
        var content = Guid.NewGuid();

        await svc.UpdateWatchProgressAsync(profile, content, 0, 3600, null, Ctx(tenant));
        // A 500s jump (forward seek) must not count as 500s of watch time — capped at 60.
        await svc.UpdateWatchProgressAsync(profile, content, 500, 3600, null, Ctx(tenant));

        var evt = Assert.Single(cache.Events(AnalyticsEventTypes.WatchProgress));
        Assert.Equal(60, evt.WatchDurationSeconds);
    }

    [Fact]
    public async Task UpdateWatchProgress_NoContext_DoesNotEmitAnalytics()
    {
        var (svc, cache) = NewService();
        var profile = Guid.NewGuid();
        var content = Guid.NewGuid();

        await svc.UpdateWatchProgressAsync(profile, content, 10, 3600);
        await svc.UpdateWatchProgressAsync(profile, content, 35, 3600);

        Assert.Empty(cache.Events(AnalyticsEventTypes.WatchProgress));
    }

    [Fact]
    public async Task RecordQoe_Startup_StoresMillisecondsAndContext()
    {
        var (svc, cache) = NewService();
        var tenant = Guid.NewGuid();

        await svc.RecordQoeEventAsync(Guid.NewGuid(), Guid.NewGuid(), "startup", 2500, 0, null, Ctx(tenant));

        var evt = Assert.Single(cache.Events(AnalyticsEventTypes.Startup));
        Assert.Equal(2500, evt.WatchDurationSeconds); // ms carried in the duration column for QoE
        Assert.Equal("android", evt.Platform);
        Assert.Contains("\"pos\":0", evt.ExtraData);
    }

    [Fact]
    public async Task RecordQoe_Rebuffer_StoresStallMs()
    {
        var (svc, cache) = NewService();
        await svc.RecordQoeEventAsync(Guid.NewGuid(), Guid.NewGuid(), "rebuffer", 1800, 340, null, Ctx(Guid.NewGuid()));

        var evt = Assert.Single(cache.Events(AnalyticsEventTypes.Rebuffer));
        Assert.Equal(1800, evt.WatchDurationSeconds);
        Assert.Contains("\"pos\":340", evt.ExtraData);
    }

    [Fact]
    public async Task RecordQoe_Error_HasNoValue()
    {
        var (svc, cache) = NewService();
        await svc.RecordQoeEventAsync(Guid.NewGuid(), Guid.NewGuid(), "playback_error", 0, 120, null, Ctx(Guid.NewGuid()));

        var evt = Assert.Single(cache.Events(AnalyticsEventTypes.PlaybackError));
        Assert.Null(evt.WatchDurationSeconds);
    }

    [Fact]
    public async Task RecordQoe_UnknownType_Throws()
    {
        var (svc, _) = NewService();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            svc.RecordQoeEventAsync(Guid.NewGuid(), Guid.NewGuid(), "meltdown", 100, 0, null, Ctx(Guid.NewGuid())));
    }
}
