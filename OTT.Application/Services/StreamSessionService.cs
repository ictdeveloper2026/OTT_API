using Microsoft.EntityFrameworkCore;
using OTT.Infrastructure.Data;
using OTT.Infrastructure.Services;

namespace OTT.Application.Services;

/// <summary>
/// Enforces the per-plan concurrent-stream limit (<c>SubscriptionPlan.MaxStreams</c>).
/// Each active playback holds a slot in Redis that expires unless the client heartbeats it;
/// this caps how many devices can stream at once without a synchronous SQL write per play.
/// </summary>
public interface IStreamSessionService
{
    /// <summary>
    /// Attempts to reserve a concurrent-stream slot for <paramref name="userId"/>. On success
    /// returns a session id the client must heartbeat (and should stop on exit).
    /// </summary>
    Task<StreamSlotResult> StartAsync(Guid userId);

    /// <summary>Keeps a slot alive. The Flutter client's 15s progress sync is the natural heartbeat.</summary>
    Task HeartbeatAsync(Guid userId, Guid sessionId);

    /// <summary>Frees a slot immediately when playback stops.</summary>
    Task StopAsync(Guid userId, Guid sessionId);
}

public record StreamSlotResult(bool Allowed, Guid SessionId, int ActiveStreams, int MaxStreams);

public class StreamSessionService : IStreamSessionService
{
    // Slots live 2 minutes; the client refreshes every 15s, leaving a wide margin for jitter.
    private static readonly TimeSpan SlotTtl = TimeSpan.FromMinutes(2);

    private readonly OttDbContext _db;
    private readonly IRedisCacheService _cache;

    public StreamSessionService(OttDbContext db, IRedisCacheService cache)
    {
        _db = db;
        _cache = cache;
    }

    public async Task<StreamSlotResult> StartAsync(Guid userId)
    {
        var maxStreams = await ResolveMaxStreamsAsync(userId);
        var sessionId = Guid.NewGuid();
        var active = await _cache.TryAcquireStreamSlotAsync(Key(userId), sessionId.ToString(), maxStreams, SlotTtl);

        return active < 0
            ? new StreamSlotResult(false, Guid.Empty, maxStreams, maxStreams)
            : new StreamSlotResult(true, sessionId, (int)active, maxStreams);
    }

    public Task HeartbeatAsync(Guid userId, Guid sessionId) =>
        _cache.RenewStreamSlotAsync(Key(userId), sessionId.ToString(), SlotTtl);

    public Task StopAsync(Guid userId, Guid sessionId) =>
        _cache.ReleaseStreamSlotAsync(Key(userId), sessionId.ToString());

    private static string Key(Guid userId) => $"streams:{userId}";

    // The MaxStreams of the user's active plan; free/unsubscribed users get a single stream.
    private async Task<int> ResolveMaxStreamsAsync(Guid userId)
    {
        var max = await _db.UserSubscriptions
            .AsNoTracking()
            .Where(s => s.UserId == userId && s.Status == "active" && s.EndDate > DateTime.UtcNow)
            .Join(_db.SubscriptionPlans, s => s.PlanId, p => p.Id, (s, p) => (int?)p.MaxStreams)
            .OrderByDescending(m => m)
            .FirstOrDefaultAsync();

        return max is > 0 ? max.Value : 1;
    }
}
