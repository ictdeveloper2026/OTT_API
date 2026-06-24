using Hangfire;
using Microsoft.EntityFrameworkCore;
using OTT.Application.Services;
using OTT.Infrastructure.Data;
using OTT.Infrastructure.Services;

namespace OTT.API.Jobs;

// ── Transcoding Job ────────────────────────────────────────────────────────────

public class TranscodingJob
{
    private readonly IVideoService _videoService;
    private readonly OttDbContext _db;
    private readonly ILogger<TranscodingJob> _logger;

    public TranscodingJob(IVideoService videoService, OttDbContext db, ILogger<TranscodingJob> logger)
    {
        _videoService = videoService;
        _db = db;
        _logger = logger;
    }

    [AutomaticRetry(Attempts = 2, DelaysInSeconds = new[] { 60, 300 })]
    public async Task ProcessAsync(Guid assetId, string sourceKey)
    {
        _logger.LogInformation("Processing transcoding job for asset {AssetId}", assetId);
        var success = await _videoService.ProcessTranscodingJobAsync(assetId, sourceKey);
        if (!success)
            throw new InvalidOperationException($"Transcoding failed for asset {assetId}");
    }
}

// ── Subscription Renewal Job ──────────────────────────────────────────────────

public class SubscriptionRenewalJob
{
    private readonly ISubscriptionService _subscriptionService;
    private readonly ILogger<SubscriptionRenewalJob> _logger;

    public SubscriptionRenewalJob(ISubscriptionService subscriptionService, ILogger<SubscriptionRenewalJob> logger)
    {
        _subscriptionService = subscriptionService;
        _logger = logger;
    }

    [DisableConcurrentExecution(timeoutInSeconds: 300)]
    public async Task ProcessAsync()
    {
        _logger.LogInformation("Running subscription renewal job");
        await _subscriptionService.ProcessRenewalsAsync();
        _logger.LogInformation("Subscription renewal job complete");
    }
}

// ── Analytics Aggregation Job ─────────────────────────────────────────────────

public class AnalyticsJob
{
    private readonly OttDbContext _db;
    private readonly IRedisCacheService _cache;
    private readonly ILogger<AnalyticsJob> _logger;

    public AnalyticsJob(OttDbContext db, IRedisCacheService cache, ILogger<AnalyticsJob> logger)
    {
        _db = db;
        _cache = cache;
        _logger = logger;
    }

    [DisableConcurrentExecution(timeoutInSeconds: 600)]
    public async Task AggregateAsync()
    {
        _logger.LogInformation("Aggregating analytics data");
        var today = DateTime.UtcNow.Date;

        // Distinct viewers per title today. Views/TotalWatchSeconds are owned by the write-behind
        // job; this job owns UniqueViewers (a metric the dashboards otherwise never get), so the
        // two paths don't fight over the same column.
        var viewerPairs = await _db.AnalyticsEvents
            .Where(e => e.EventType == "content_view"
                && e.ContentId != null && e.UserId != null
                && e.CreatedAt >= today && e.CreatedAt < today.AddDays(1))
            .Select(e => new { e.ContentId, e.UserId })
            .Distinct()
            .ToListAsync();

        var uniqueViewers = viewerPairs
            .GroupBy(p => p.ContentId!.Value)
            .Select(g => new { ContentId = g.Key, Unique = g.Count() })
            .ToList();

        foreach (var item in uniqueViewers)
        {
            var analytics = await _db.ContentAnalytics
                .FirstOrDefaultAsync(a => a.ContentId == item.ContentId && a.Date == today);

            if (analytics == null)
            {
                analytics = new Domain.Entities.ContentAnalytics
                {
                    ContentId = item.ContentId,
                    Date = today,
                    UniqueViewers = item.Unique
                };
                _db.ContentAnalytics.Add(analytics);
            }
            else
            {
                analytics.UniqueViewers = item.Unique;
            }
        }

        await _db.SaveChangesAsync();

        // Aggregate watch time
        var watchEvents = await _db.AnalyticsEvents
            .Where(e => e.EventType == "watch_progress"
                && e.CreatedAt >= today
                && e.CreatedAt < today.AddDays(1)
                && e.ContentId != null)
            .GroupBy(e => e.ContentId)
            .Select(g => new
            {
                ContentId = g.Key,
                TotalWatchSeconds = g.Sum(e => (int)(e.WatchDurationSeconds ?? 0))
            })
            .ToListAsync();

        foreach (var item in watchEvents.Where(i => i.ContentId.HasValue))
        {
            var analytics = await _db.ContentAnalytics
                .FirstOrDefaultAsync(a => a.ContentId == item.ContentId!.Value && a.Date == today);

            if (analytics != null)
                analytics.TotalWatchSeconds = item.TotalWatchSeconds;
        }

        await _db.SaveChangesAsync();
        _logger.LogInformation("Analytics aggregation complete, processed {Count} content items", uniqueViewers.Count);
    }
}

// ── Cleanup Job ───────────────────────────────────────────────────────────────

public class CleanupJob
{
    private readonly OttDbContext _db;
    private readonly IS3StorageService _s3;
    private readonly ILogger<CleanupJob> _logger;

    public CleanupJob(OttDbContext db, IS3StorageService s3, ILogger<CleanupJob> logger)
    {
        _db = db;
        _s3 = s3;
        _logger = logger;
    }

    public async Task CleanExpiredTokensAsync()
    {
        var cutoff = DateTime.UtcNow.AddDays(-30);
        var expired = await _db.RefreshTokens
            .Where(t => t.IsRevoked || t.ExpiresAt < cutoff)
            .ToListAsync();

        _db.RefreshTokens.RemoveRange(expired);
        await _db.SaveChangesAsync();
        _logger.LogInformation("Cleaned {Count} expired refresh tokens", expired.Count);
    }

    public async Task CleanOldAnalyticsAsync()
    {
        var cutoff = DateTime.UtcNow.AddDays(-90);
        var old = await _db.AnalyticsEvents
            .Where(e => e.CreatedAt < cutoff)
            .Take(10000) // Batch delete
            .ToListAsync();

        _db.AnalyticsEvents.RemoveRange(old);
        await _db.SaveChangesAsync();
        _logger.LogInformation("Cleaned {Count} old analytics events", old.Count);
    }
}

// ── Write-Behind Flush Job ─────────────────────────────────────────────────────
// Drains the Redis buffers populated by the hot read/write paths (view counts and
// watch progress) and persists them to SQL in batches. Keeps ~thousands of writes/sec
// off the database.

public class WriteBehindFlushJob
{
    private readonly OttDbContext _db;
    private readonly IRedisCacheService _cache;
    private readonly ILogger<WriteBehindFlushJob> _logger;

    public WriteBehindFlushJob(OttDbContext db, IRedisCacheService cache, ILogger<WriteBehindFlushJob> logger)
    {
        _db = db;
        _cache = cache;
        _logger = logger;
    }

    [DisableConcurrentExecution(timeoutInSeconds: 120)]
    public async Task FlushAsync()
    {
        await FlushViewCountsAsync();
        await FlushWatchProgressAsync();
        await FlushAnalyticsEventsAsync();
    }

    // Drains the append-only analytics buffer and batch-inserts AnalyticsEvent rows — the
    // producer the pipeline was missing (AnalyticsJob then aggregates these into UniqueViewers).
    private async Task FlushAnalyticsEventsAsync()
    {
        var raw = await _cache.ListDrainAsync("analytics:pending", 1000);
        if (raw.Count == 0) return;

        var events = new List<Domain.Entities.AnalyticsEvent>(raw.Count);
        foreach (var json in raw)
        {
            AnalyticsEventBuffer? e;
            try { e = System.Text.Json.JsonSerializer.Deserialize<AnalyticsEventBuffer>(json); }
            catch { continue; } // skip a malformed buffer entry rather than failing the batch
            if (e is null) continue;

            events.Add(new Domain.Entities.AnalyticsEvent
            {
                TenantId = e.TenantId,
                UserId = e.ViewerId,
                ContentId = e.ContentId,
                EpisodeId = e.EpisodeId,
                EventType = e.EventType,
                WatchDurationSeconds = e.WatchDurationSeconds,
                CreatedAt = e.CreatedAt
            });
        }

        if (events.Count == 0) return;
        _db.AnalyticsEvents.AddRange(events);
        await _db.SaveChangesAsync();
        _logger.LogInformation("Flushed {Count} analytics events", events.Count);
    }

    private async Task FlushViewCountsAsync()
    {
        var views = await _cache.HashGetAllAndClearAsync("views:pending");
        if (views.Count == 0) return;

        var today = DateTime.UtcNow.Date;
        foreach (var (idStr, countStr) in views)
        {
            if (!Guid.TryParse(idStr, out var id) || !long.TryParse(countStr, out var n) || n <= 0) continue;

            await _db.Contents.IgnoreQueryFilters()
                .Where(c => c.Id == id)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.ViewCount, c => c.ViewCount + (int)n));

            // Populate daily analytics — the producer the dashboards were missing.
            var ca = await _db.ContentAnalytics.FirstOrDefaultAsync(a => a.ContentId == id && a.Date == today);
            if (ca == null)
                _db.ContentAnalytics.Add(new Domain.Entities.ContentAnalytics { ContentId = id, Date = today, Views = (int)n });
            else
                ca.Views += (int)n;
        }
        await _db.SaveChangesAsync();
        _logger.LogInformation("Flushed view counts for {Count} titles", views.Count);
    }

    private async Task FlushWatchProgressAsync()
    {
        var entries = await _cache.HashGetAllAndClearAsync("wh:pending");
        if (entries.Count == 0) return;

        var parsed = new List<(Guid ProfileId, Guid ContentId, Guid? EpisodeId, int Position, int Duration)>();
        foreach (var (field, value) in entries)
        {
            var fk = field.Split('|');
            var vv = value.Split(':');
            if (fk.Length < 3 || vv.Length < 2) continue;
            if (!Guid.TryParse(fk[0], out var pid) || !Guid.TryParse(fk[1], out var cid)) continue;
            Guid? eid = Guid.TryParse(fk[2], out var ep) ? ep : null;
            if (!int.TryParse(vv[0], out var pos)) continue;
            int.TryParse(vv[1], out var dur);
            parsed.Add((pid, cid, eid, pos, dur));
        }
        if (parsed.Count == 0) return;

        var profileIds = parsed.Select(p => p.ProfileId).Distinct().ToList();
        var contentIds = parsed.Select(p => p.ContentId).Distinct().ToList();
        var existing = await _db.WatchHistories
            .Where(w => profileIds.Contains(w.ProfileId) && contentIds.Contains(w.ContentId))
            .ToListAsync();

        foreach (var p in parsed)
        {
            var h = existing.FirstOrDefault(w =>
                w.ProfileId == p.ProfileId && w.ContentId == p.ContentId && w.EpisodeId == p.EpisodeId);
            if (h == null)
            {
                h = new Domain.Entities.WatchHistory
                {
                    ProfileId = p.ProfileId,
                    ContentId = p.ContentId,
                    EpisodeId = p.EpisodeId
                };
                _db.WatchHistories.Add(h);
                existing.Add(h);
            }
            h.PositionSeconds = p.Position;
            h.LastWatchedAt = DateTime.UtcNow;
            if (p.Duration > 0)
                h.IsCompleted = p.Position >= p.Duration * 0.9;
        }

        await _db.SaveChangesAsync();
        _logger.LogInformation("Flushed watch progress for {Count} entries", parsed.Count);
    }
}

// ── Outbox Dispatch Job ────────────────────────────────────────────────────────
// Drains the email/push outbox and performs the actual SendGrid/FCM delivery off the
// request thread, with retry + dead-lettering handled inside OutboxService.

public class OutboxDispatchJob
{
    private readonly IOutboxService _outbox;
    private readonly ILogger<OutboxDispatchJob> _logger;

    public OutboxDispatchJob(IOutboxService outbox, ILogger<OutboxDispatchJob> logger)
    {
        _outbox = outbox;
        _logger = logger;
    }

    [DisableConcurrentExecution(timeoutInSeconds: 120)]
    public async Task DispatchAsync()
    {
        var sent = await _outbox.DispatchPendingAsync(batchSize: 100);
        if (sent > 0)
            _logger.LogInformation("Outbox dispatched {Count} message(s)", sent);
    }
}

// ── Hangfire Scheduler ────────────────────────────────────────────────────────

public static class HangfireScheduler
{
    public static void ConfigureRecurringJobs()
    {
        // Write-behind flush (view counts + watch progress) - every minute
        RecurringJob.AddOrUpdate<WriteBehindFlushJob>(
            "write-behind-flush",
            job => job.FlushAsync(),
            "* * * * *");

        // Outbox dispatch (email/push) - every minute
        RecurringJob.AddOrUpdate<OutboxDispatchJob>(
            "outbox-dispatch",
            job => job.DispatchAsync(),
            "* * * * *");

        // Subscription renewals - every hour
        RecurringJob.AddOrUpdate<SubscriptionRenewalJob>(
            "subscription-renewal",
            job => job.ProcessAsync(),
            Cron.Hourly);

        // Analytics aggregation - every 15 minutes
        RecurringJob.AddOrUpdate<AnalyticsJob>(
            "analytics-aggregation",
            job => job.AggregateAsync(),
            "*/15 * * * *");

        // Token cleanup - daily at 3am
        RecurringJob.AddOrUpdate<CleanupJob>(
            "token-cleanup",
            job => job.CleanExpiredTokensAsync(),
            "0 3 * * *");

        // Old analytics cleanup - weekly Sunday 4am
        RecurringJob.AddOrUpdate<CleanupJob>(
            "analytics-cleanup",
            job => job.CleanOldAnalyticsAsync(),
            "0 4 * * 0");

        // IPTV channel sync from iptv-org - daily at 5am (keeps the channel list fresh)
        RecurringJob.AddOrUpdate<OTT.Infrastructure.Services.IIptvSyncService>(
            "iptv-sync",
            job => job.SyncAsync(null),
            "0 5 * * *");
    }
}
