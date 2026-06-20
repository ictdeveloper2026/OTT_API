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

        // Aggregate content views per day
        var viewEvents = await _db.AnalyticsEvents
            .Where(e => e.EventType == "content_view"
                && e.CreatedAt >= today
                && e.CreatedAt < today.AddDays(1))
            .GroupBy(e => e.ContentId)
            .Select(g => new { ContentId = g.Key, Views = g.Count() })
            .ToListAsync();

        foreach (var item in viewEvents.Where(i => i.ContentId.HasValue))
        {
            var analytics = await _db.ContentAnalytics
                .FirstOrDefaultAsync(a => a.ContentId == item.ContentId!.Value && a.Date == today);

            if (analytics == null)
            {
                analytics = new Domain.Entities.ContentAnalytics
                {
                    ContentId = item.ContentId!.Value,
                    Date = today,
                    Views = item.Views
                };
                _db.ContentAnalytics.Add(analytics);
            }
            else
            {
                analytics.Views = item.Views;
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
        _logger.LogInformation("Analytics aggregation complete, processed {Count} content items", viewEvents.Count);
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

// ── Hangfire Scheduler ────────────────────────────────────────────────────────

public static class HangfireScheduler
{
    public static void ConfigureRecurringJobs()
    {
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
