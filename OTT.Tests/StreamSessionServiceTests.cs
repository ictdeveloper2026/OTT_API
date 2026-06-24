using Microsoft.EntityFrameworkCore;
using OTT.Application.Services;
using OTT.Domain.Entities;
using OTT.Infrastructure.Data;
using OTT.Infrastructure.Services;
using Xunit;

namespace OTT.Tests;

public class StreamSessionServiceTests
{
    private static OttDbContext NewDb() =>
        new(new DbContextOptionsBuilder<OttDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    // Minimal in-memory stand-in that reproduces the atomic slot semantics of the Redis script.
    private sealed class FakeSlotCache : IRedisCacheService
    {
        private readonly Dictionary<string, HashSet<string>> _slots = new();

        public Task<long> TryAcquireStreamSlotAsync(string key, string member, int maxConcurrent, TimeSpan ttl)
        {
            if (!_slots.TryGetValue(key, out var set)) _slots[key] = set = new();
            if (!set.Contains(member) && set.Count >= maxConcurrent) return Task.FromResult(-1L);
            set.Add(member);
            return Task.FromResult((long)set.Count);
        }
        public Task RenewStreamSlotAsync(string key, string member, TimeSpan ttl) => Task.CompletedTask;
        public Task ReleaseStreamSlotAsync(string key, string member)
        {
            if (_slots.TryGetValue(key, out var set)) set.Remove(member);
            return Task.CompletedTask;
        }

        // Unused by StreamSessionService.
        public Task<T?> GetAsync<T>(string key) where T : class => Task.FromResult<T?>(null);
        public Task SetAsync<T>(string key, T value, TimeSpan? expiry = null) where T : class => Task.CompletedTask;
        public Task RemoveAsync(string key) => Task.CompletedTask;
        public Task RemoveByPatternAsync(string pattern) => Task.CompletedTask;
        public Task<bool> ExistsAsync(string key) => Task.FromResult(false);
        public Task<long> IncrementAsync(string key, TimeSpan? expiry = null) => Task.FromResult(0L);
        public Task SetStringAsync(string key, string value, TimeSpan? expiry = null) => Task.CompletedTask;
        public Task<string?> GetStringAsync(string key) => Task.FromResult<string?>(null);
        public Task HashIncrementAsync(string key, string field, long value = 1) => Task.CompletedTask;
        public Task HashSetAsync(string key, string field, string value) => Task.CompletedTask;
        public Task<Dictionary<string, string>> HashGetAllAndClearAsync(string key) => Task.FromResult(new Dictionary<string, string>());
    }

    private static async Task<(OttDbContext db, Guid userId)> SeedAsync(int? planMaxStreams)
    {
        var db = NewDb();
        var userId = Guid.NewGuid();
        if (planMaxStreams is int max)
        {
            var planId = Guid.NewGuid();
            db.SubscriptionPlans.Add(new SubscriptionPlan { Id = planId, TenantId = Guid.NewGuid(), Name = "P", MaxStreams = max, IsActive = true });
            db.UserSubscriptions.Add(new UserSubscription
            {
                UserId = userId, PlanId = planId, Status = "active",
                StartDate = DateTime.UtcNow.AddDays(-1), EndDate = DateTime.UtcNow.AddDays(30)
            });
            await db.SaveChangesAsync();
        }
        return (db, userId);
    }

    [Fact]
    public async Task FreeUser_GetsExactlyOneStream()
    {
        var (db, userId) = await SeedAsync(planMaxStreams: null);
        var sut = new StreamSessionService(db, new FakeSlotCache());

        var first = await sut.StartAsync(userId);
        var second = await sut.StartAsync(userId);

        Assert.True(first.Allowed);
        Assert.Equal(1, first.MaxStreams);
        Assert.False(second.Allowed); // second concurrent stream rejected
    }

    [Fact]
    public async Task Plan_AllowsUpToMaxStreams_ThenRejects()
    {
        var (db, userId) = await SeedAsync(planMaxStreams: 2);
        var sut = new StreamSessionService(db, new FakeSlotCache());

        var s1 = await sut.StartAsync(userId);
        var s2 = await sut.StartAsync(userId);
        var s3 = await sut.StartAsync(userId);

        Assert.True(s1.Allowed);
        Assert.True(s2.Allowed);
        Assert.False(s3.Allowed);
        Assert.Equal(2, s3.MaxStreams);
    }

    [Fact]
    public async Task StoppingAStream_FreesASlot()
    {
        var (db, userId) = await SeedAsync(planMaxStreams: 1);
        var sut = new StreamSessionService(db, new FakeSlotCache());

        var s1 = await sut.StartAsync(userId);
        Assert.True(s1.Allowed);
        Assert.False((await sut.StartAsync(userId)).Allowed);

        await sut.StopAsync(userId, s1.SessionId);

        Assert.True((await sut.StartAsync(userId)).Allowed); // slot freed
    }
}
