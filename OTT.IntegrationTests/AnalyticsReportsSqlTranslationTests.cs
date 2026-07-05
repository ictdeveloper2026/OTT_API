using Microsoft.EntityFrameworkCore;
using OTT.Application.Services;
using OTT.Infrastructure.Data;
using Xunit;

namespace OTT.IntegrationTests;

/// <summary>
/// Runtime smoke test for the studio analytics report queries against a REAL SQL Server
/// (localhost/ott_platform) — no Docker/Testcontainers. InMemory unit tests evaluate LINQ
/// client-side and therefore cannot catch a query that fails to translate to T-SQL. Each test here
/// executes the exact query shape used by AdminController; if EF can't translate it (e.g.
/// GroupBy(CreatedAt.Hour) / .Date, or the genre join), ToListAsync throws and the test fails.
///
/// Uses a random tenant id so results are empty — we're proving translation + execution against the
/// real schema/columns, not asserting data. Not in the "containers" collection, so it runs without Docker.
/// </summary>
public class AnalyticsReportsSqlTranslationTests
{
    private const string ConnectionString =
        "Server=localhost;Database=ott_platform;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=True;";

    private static readonly string[] ViewingEventTypes =
        { AnalyticsEventTypes.ContentView, AnalyticsEventTypes.WatchProgress, AnalyticsEventTypes.Play };

    private static OttDbContext NewDb() =>
        new(new DbContextOptionsBuilder<OttDbContext>().UseSqlServer(ConnectionString).Options);

    [Fact]
    public async Task TimeOfDay_HourAndDate_GroupBy_TranslatesAndRuns()
    {
        await using var db = NewDb();
        var tenant = Guid.NewGuid();
        var since = DateTime.UtcNow.Date.AddDays(-30);

        var events = db.AnalyticsEvents.AsNoTracking()
            .Where(e => e.TenantId == tenant && e.CreatedAt >= since && ViewingEventTypes.Contains(e.EventType));

        // The two translation-sensitive projections: DATEPART(hour, …) and CAST(… AS date).
        var hourly = await events.GroupBy(e => e.CreatedAt.Hour)
            .Select(g => new { Hour = g.Key, Count = g.Count() }).ToListAsync();
        var daily = await events.GroupBy(e => e.CreatedAt.Date)
            .Select(g => new { g.Key, Count = g.Count() }).ToListAsync();

        Assert.NotNull(hourly);
        Assert.NotNull(daily);
    }

    [Fact]
    public async Task GenreConsumption_JoinGroupSum_TranslatesAndRuns()
    {
        await using var db = NewDb();
        var tenant = Guid.NewGuid();
        var since = DateTime.UtcNow.Date.AddDays(-30);

        var rows = await (
            from a in db.ContentAnalytics.AsNoTracking()
            join cg in db.ContentGenres on a.ContentId equals cg.ContentId
            join g in db.Genres on cg.GenreId equals g.Id
            where a.Date >= since && a.Content!.TenantId == tenant
            group a by new { g.Id, g.Name } into grp
            select new
            {
                Genre = grp.Key.Name,
                Views = grp.Sum(x => x.Views),
                UniqueViewers = grp.Sum(x => x.UniqueViewers),
                WatchSeconds = grp.Sum(x => (long)x.TotalWatchSeconds)
            })
            .OrderByDescending(x => x.WatchSeconds)
            .ToListAsync();

        Assert.NotNull(rows);
    }

    [Fact]
    public async Task Devices_GroupByDeviceAndPlatform_TranslatesAndRuns()
    {
        await using var db = NewDb();
        var tenant = Guid.NewGuid();
        var since = DateTime.UtcNow.Date.AddDays(-30);

        var rows = await db.AnalyticsEvents.AsNoTracking()
            .Where(e => e.TenantId == tenant && e.CreatedAt >= since)
            .GroupBy(e => new { e.DeviceType, e.Platform })
            .Select(g => new { g.Key.DeviceType, g.Key.Platform, Count = g.Count() })
            .ToListAsync();

        Assert.NotNull(rows);
    }

    [Fact]
    public async Task Engagement_PauseSeekWithExtraData_TranslatesAndRuns()
    {
        await using var db = NewDb();
        var tenant = Guid.NewGuid();
        var content = Guid.NewGuid();
        var since = DateTime.UtcNow.Date.AddDays(-90);

        var events = await db.AnalyticsEvents.AsNoTracking()
            .Where(e => e.TenantId == tenant && e.ContentId == content && e.CreatedAt >= since
                && (e.EventType == AnalyticsEventTypes.Pause || e.EventType == AnalyticsEventTypes.Seek)
                && e.ExtraData != null)
            .OrderByDescending(e => e.CreatedAt)
            .Take(50000)
            .Select(e => new { e.EventType, e.ExtraData })
            .ToListAsync();

        Assert.NotNull(events);
    }

    [Fact]
    public async Task Regions_GroupByCountry_TranslatesAndRuns()
    {
        await using var db = NewDb();
        var tenant = Guid.NewGuid();

        var rows = await db.AnalyticsEvents.AsNoTracking()
            .Where(e => e.TenantId == tenant && e.Country != null && e.Country != "")
            .GroupBy(e => e.Country!)
            .Select(g => new { Country = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .Take(10)
            .ToListAsync();

        Assert.NotNull(rows);
    }

    [Fact]
    public async Task Qoe_GroupByPlatformAndType_WithAvg_TranslatesAndRuns()
    {
        await using var db = NewDb();
        var tenant = Guid.NewGuid();
        var since = DateTime.UtcNow.Date.AddDays(-30);

        // The QoE report's grouped pass: counts + AVG(metric) per (platform, type).
        var grouped = await db.AnalyticsEvents.AsNoTracking()
            .Where(e => e.TenantId == tenant && e.CreatedAt >= since
                && (e.EventType == AnalyticsEventTypes.Startup
                    || e.EventType == AnalyticsEventTypes.Rebuffer
                    || e.EventType == AnalyticsEventTypes.PlaybackError))
            .GroupBy(e => new { e.Platform, e.EventType })
            .Select(g => new { g.Key.Platform, g.Key.EventType, Count = g.Count(), AvgMs = g.Average(x => (double?)x.WatchDurationSeconds) })
            .ToListAsync();

        var slowStarts = await db.AnalyticsEvents.AsNoTracking()
            .CountAsync(e => e.TenantId == tenant && e.CreatedAt >= since
                && e.EventType == AnalyticsEventTypes.Startup && e.WatchDurationSeconds > 4000);

        Assert.NotNull(grouped);
        Assert.True(slowStarts >= 0);
    }

    [Fact]
    public async Task WatchProgressAggregation_SumByContent_TranslatesAndRuns()
    {
        // Mirrors AnalyticsJob's watch-time rollup that now has a real producer.
        await using var db = NewDb();
        var today = DateTime.UtcNow.Date;

        var watch = await db.AnalyticsEvents.AsNoTracking()
            .Where(e => e.EventType == AnalyticsEventTypes.WatchProgress
                && e.CreatedAt >= today && e.CreatedAt < today.AddDays(1) && e.ContentId != null)
            .GroupBy(e => e.ContentId)
            .Select(g => new { ContentId = g.Key, TotalWatchSeconds = g.Sum(e => (int)(e.WatchDurationSeconds ?? 0)) })
            .ToListAsync();

        Assert.NotNull(watch);
    }
}
