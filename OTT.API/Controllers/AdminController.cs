using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OTT.API.Middleware;
using OTT.Application.DTOs;
using OTT.Application.Services;
using OTT.Domain.Entities;
using OTT.Infrastructure.Data;
using OTT.Infrastructure.Services;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Text.Json;

namespace OTT.API.Controllers;

// ── Admin request bodies ────────────────────────────────────────────────────────
public record SaveBannerDto(string? Title, string? Subtitle, string Type, string? ImageUrl,
    string? MobileImageUrl, string? CtaText, string? CtaAction, Guid? ContentId, int SortOrder, bool IsActive);
public record SaveContentRowDto(string Title, string RowType, string? SourceValue, string DisplayStyle, int SortOrder, int MaxItems, bool IsActive);
public record SaveGenreDto(
    [System.ComponentModel.DataAnnotations.Required, System.ComponentModel.DataAnnotations.MaxLength(100)] string Name,
    [System.ComponentModel.DataAnnotations.Required, System.ComponentModel.DataAnnotations.MaxLength(100)] string Slug,
    string? IconUrl, int SortOrder);
public record SavePromoDto(
    [System.ComponentModel.DataAnnotations.Required, System.ComponentModel.DataAnnotations.MaxLength(100)] string Code,
    [System.ComponentModel.DataAnnotations.RegularExpression("^(percentage|fixed)$", ErrorMessage = "DiscountType must be 'percentage' or 'fixed'")] string DiscountType,
    [System.ComponentModel.DataAnnotations.Range(0, 1000000)] decimal DiscountValue,
    [System.ComponentModel.DataAnnotations.Range(1, int.MaxValue)] int? MaxUses,
    DateTime? ExpiresAt);
public record UserStatusDto(
    [System.ComponentModel.DataAnnotations.RegularExpression("^(active|blocked)$", ErrorMessage = "Status must be 'active' or 'blocked'")] string Status); // active | blocked
public record UpdateConfigValueDto(string? Value, bool IsPublic);

// Multipart form for uploading a subtitle file against a title.
public class AddSubtitleForm
{
    public IFormFile? File { get; set; }
    [Required, MaxLength(10)] public string Language { get; set; } = ""; // e.g. en, hi, ta
    [MaxLength(100)] public string? Label { get; set; }                  // e.g. "English (CC)"
    [RegularExpression("^(vtt|srt)$", ErrorMessage = "Format must be 'vtt' or 'srt'")] public string? Format { get; set; }
}

// Multipart form for uploading a source video file (HLS transcode pipeline).
public class SetVideoForm
{
    public IFormFile? File { get; set; }
}

// Declares an audio (language) track. For HLS the rendition lives in the manifest;
// this carries the human label + manifest order for the player's audio menu.
public record AddAudioTrackDto(
    [Required, MaxLength(10)] string Language,
    [MaxLength(100)] string? Label,
    [Range(0, 64)] int TrackIndex,
    bool IsDefault);

[ApiController]
[Route("api/admin")]
[Authorize(Roles = "admin")]
public class AdminController : ControllerBase
{
    private readonly OttDbContext _db;
    private readonly IContentService _content;
    private readonly ILiveStreamService _live;
    private readonly IStorageService _storage;
    private readonly IDynamicSettingsService _settings;
    private readonly IVideoService _video;
    private readonly ICommunityService _community;

    public AdminController(OttDbContext db, IContentService content, ILiveStreamService live,
        IStorageService storage, IDynamicSettingsService settings, IVideoService video, ICommunityService community)
    {
        _db = db; _content = content; _live = live; _storage = storage; _settings = settings; _video = video; _community = community;
    }

    // ── Dashboard ──
    [HttpGet("stats")]
    public async Task<IActionResult> GetStats()
    {
        var tenantId = HttpContext.GetTenantId();
        var monthStart = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        var stats = new DashboardStatsDto
        {
            TotalUsers = await _db.Users.CountAsync(u => u.TenantId == tenantId && !u.IsDeleted),
            ActiveSubscriptions = await _db.UserSubscriptions.CountAsync(s => s.Status == "active" && s.EndDate > DateTime.UtcNow && s.Plan.TenantId == tenantId),
            TotalContent = await _db.Contents.CountAsync(c => c.TenantId == tenantId),
            LiveStreams = await _db.LiveStreams.CountAsync(l => l.TenantId == tenantId && l.Status == "live"),
            MonthlyRevenue = await _db.Payments
                .Where(p => p.TenantId == tenantId && p.Status == "success" && p.CreatedAt >= monthStart)
                .SumAsync(p => (decimal?)p.Amount) ?? 0
        };
        return Ok(ApiResponse<object>.Ok(stats));
    }

    [HttpGet("revenue")]
    public async Task<IActionResult> GetRevenue([FromQuery] string period = "30d")
    {
        var tenantId = HttpContext.GetTenantId();
        var days = period switch { "7d" => 7, "90d" => 90, "365d" => 365, _ => 30 };
        var since = DateTime.UtcNow.Date.AddDays(-days);

        // Fetch then group in memory — SQL Server can't translate GroupBy(date)+ToString.
        var payments = await _db.Payments
            .Where(p => p.TenantId == tenantId && p.Status == "success" && p.CreatedAt >= since)
            .Select(p => new { p.CreatedAt, p.Amount })
            .ToListAsync();
        var rows = payments
            .GroupBy(p => p.CreatedAt.Date)
            .Select(g => new RevenueChartDto { Label = g.Key.ToString("yyyy-MM-dd"), Amount = g.Sum(x => x.Amount) })
            .OrderBy(r => r.Label)
            .ToList();
        return Ok(ApiResponse<object>.Ok(rows));
    }

    // Top content by lifetime views (tenant-scoped).
    [HttpGet("analytics/top-content")]
    public async Task<IActionResult> TopContent([FromQuery] int limit = 10)
    {
        var tenantId = HttpContext.GetTenantId();
        var top = await _db.Contents.AsNoTracking()
            .Where(c => c.TenantId == tenantId)
            .OrderByDescending(c => c.ViewCount)
            .Take(Math.Clamp(limit, 1, 50))
            .Select(c => new { c.Id, c.Title, c.ThumbnailUrl, Views = c.ViewCount, Rating = c.AverageRating ?? 0 })
            .ToListAsync();
        return Ok(ApiResponse<object>.Ok(top));
    }

    // Viewer distribution by country, from analytics events (tenant-scoped).
    [HttpGet("analytics/regions")]
    public async Task<IActionResult> Regions()
    {
        var tenantId = HttpContext.GetTenantId();
        var rows = await _db.AnalyticsEvents.AsNoTracking()
            .Where(e => e.TenantId == tenantId && e.Country != null && e.Country != "")
            .GroupBy(e => e.Country!)
            .Select(g => new { Country = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .Take(10)
            .ToListAsync();
        return Ok(ApiResponse<object>.Ok(rows));
    }

    // Only genuine viewing signals count toward "when/what they watch" — not UI-driven pause/seek noise.
    private static readonly string[] ViewingEventTypes =
        { AnalyticsEventTypes.ContentView, AnalyticsEventTypes.WatchProgress, AnalyticsEventTypes.Play };

    private static int PeriodDays(string period) => period switch { "7d" => 7, "90d" => 90, "365d" => 365, _ => 30 };

    // When viewers watch: hour-of-day (0–23) and day-of-week (0=Sun..6=Sat) histograms, tenant-scoped.
    [HttpGet("analytics/time-of-day")]
    public async Task<IActionResult> TimeOfDay([FromQuery] string period = "30d")
    {
        var tenantId = HttpContext.GetTenantId();
        var since = DateTime.UtcNow.Date.AddDays(-PeriodDays(period));

        var events = _db.AnalyticsEvents.AsNoTracking()
            .Where(e => e.TenantId == tenantId && e.CreatedAt >= since && ViewingEventTypes.Contains(e.EventType));

        // Hour bucket translates to DATEPART(hour, …); 24 tiny rows out of SQL.
        var hourlyRaw = await events
            .GroupBy(e => e.CreatedAt.Hour)
            .Select(g => new { Hour = g.Key, Count = g.Count() })
            .ToListAsync();

        // Day-of-week isn't reliably translatable, so aggregate per-date in SQL then fold to weekday in memory.
        var dailyRaw = await events
            .GroupBy(e => e.CreatedAt.Date)
            .Select(g => new { Date = g.Key, Count = g.Count() })
            .ToListAsync();

        var byHour = Enumerable.Range(0, 24)
            .Select(h => new { Hour = h, Count = hourlyRaw.Where(x => x.Hour == h).Sum(x => x.Count) })
            .ToList();
        var byDayOfWeek = Enumerable.Range(0, 7)
            .Select(d => new { DayOfWeek = d, Count = dailyRaw.Where(x => (int)x.Date.DayOfWeek == d).Sum(x => x.Count) })
            .ToList();

        return Ok(ApiResponse<object>.Ok(new { byHour, byDayOfWeek }));
    }

    // What genres viewers binge: consumption rolled up from the daily ContentAnalytics aggregates.
    // A title counts toward each of its genres, so shares reflect genre reach, not exclusive time.
    [HttpGet("analytics/genres")]
    public async Task<IActionResult> GenreConsumption([FromQuery] string period = "30d")
    {
        var tenantId = HttpContext.GetTenantId();
        var since = DateTime.UtcNow.Date.AddDays(-PeriodDays(period));

        var rows = await (
            from a in _db.ContentAnalytics.AsNoTracking()
            join cg in _db.ContentGenres on a.ContentId equals cg.ContentId
            join g in _db.Genres on cg.GenreId equals g.Id
            where a.Date >= since && a.Content!.TenantId == tenantId
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

        var totalWatch = rows.Sum(r => r.WatchSeconds);
        var result = rows.Select(r => new
        {
            r.Genre,
            r.Views,
            r.UniqueViewers,
            WatchHours = Math.Round(r.WatchSeconds / 3600.0, 1),
            SharePercent = totalWatch > 0 ? Math.Round(r.WatchSeconds * 100.0 / totalWatch, 1) : 0
        });
        return Ok(ApiResponse<object>.Ok(result));
    }

    // Device & platform mix across all captured events (tenant-scoped). Nulls fold to "unknown".
    [HttpGet("analytics/devices")]
    public async Task<IActionResult> Devices([FromQuery] string period = "30d")
    {
        var tenantId = HttpContext.GetTenantId();
        var since = DateTime.UtcNow.Date.AddDays(-PeriodDays(period));

        var raw = await _db.AnalyticsEvents.AsNoTracking()
            .Where(e => e.TenantId == tenantId && e.CreatedAt >= since)
            .GroupBy(e => new { e.DeviceType, e.Platform })
            .Select(g => new { g.Key.DeviceType, g.Key.Platform, Count = g.Count() })
            .ToListAsync();

        var rows = raw
            .Select(r => new { DeviceType = r.DeviceType ?? "unknown", Platform = r.Platform ?? "unknown", r.Count })
            .OrderByDescending(r => r.Count)
            .ToList();
        return Ok(ApiResponse<object>.Ok(rows));
    }

    // Where viewers pause or skip within a single title — the drop-off/re-watch hotspots.
    // Positions are parsed from the event ExtraData and bucketed along the timeline.
    [HttpGet("analytics/engagement/{contentId:guid}")]
    public async Task<IActionResult> Engagement(Guid contentId, [FromQuery] int bucketSeconds = 30, [FromQuery] string period = "90d")
    {
        var tenantId = HttpContext.GetTenantId();
        var since = DateTime.UtcNow.Date.AddDays(-PeriodDays(period));
        bucketSeconds = Math.Clamp(bucketSeconds, 5, 600);

        var events = await _db.AnalyticsEvents.AsNoTracking()
            .Where(e => e.TenantId == tenantId && e.ContentId == contentId && e.CreatedAt >= since
                && (e.EventType == AnalyticsEventTypes.Pause || e.EventType == AnalyticsEventTypes.Seek)
                && e.ExtraData != null)
            .OrderByDescending(e => e.CreatedAt)
            .Take(50000) // cap the in-memory parse; ample for a per-title report
            .Select(e => new { e.EventType, e.ExtraData })
            .ToListAsync();

        var buckets = new Dictionary<int, (int Pauses, int Skips)>();
        foreach (var ev in events)
        {
            var pos = ParsePosition(ev.ExtraData);
            if (pos is not { } p) continue;
            var bucket = p / bucketSeconds * bucketSeconds;
            var cur = buckets.TryGetValue(bucket, out var c) ? c : (0, 0);
            buckets[bucket] = ev.EventType == AnalyticsEventTypes.Pause
                ? (cur.Item1 + 1, cur.Item2)
                : (cur.Item1, cur.Item2 + 1);
        }

        var hotspots = buckets
            .OrderBy(b => b.Key)
            .Select(b => new { PositionSeconds = b.Key, Pauses = b.Value.Pauses, Skips = b.Value.Skips })
            .ToList();
        return Ok(ApiResponse<object>.Ok(new { bucketSeconds, sampled = events.Count, hotspots }));
    }

    // Pulls the integer "pos" out of an event's ExtraData JSON (e.g. {"pos":123,"to":250}).
    private static int? ParsePosition(string? extraData)
    {
        if (string.IsNullOrEmpty(extraData)) return null;
        try
        {
            using var doc = JsonDocument.Parse(extraData);
            return doc.RootElement.TryGetProperty("pos", out var p) && p.TryGetInt32(out var v) ? v : null;
        }
        catch (JsonException) { return null; }
    }

    private const int SlowStartupThresholdMs = 4000; // a start slower than this hurts retention

    // Quality-of-Experience: startup time, rebuffering and errors — the strongest churn predictors.
    // For QoE events WatchDurationSeconds holds the metric in milliseconds (see AnalyticsEventTypes).
    [HttpGet("analytics/qoe")]
    public async Task<IActionResult> Qoe([FromQuery] string period = "30d")
    {
        var tenantId = HttpContext.GetTenantId();
        var since = DateTime.UtcNow.Date.AddDays(-PeriodDays(period));

        // One grouped pass over the QoE events → counts + avg metric per (platform, type).
        var grouped = await _db.AnalyticsEvents.AsNoTracking()
            .Where(e => e.TenantId == tenantId && e.CreatedAt >= since
                && (e.EventType == AnalyticsEventTypes.Startup
                    || e.EventType == AnalyticsEventTypes.Rebuffer
                    || e.EventType == AnalyticsEventTypes.PlaybackError))
            .GroupBy(e => new { e.Platform, e.EventType })
            .Select(g => new
            {
                g.Key.Platform,
                g.Key.EventType,
                Count = g.Count(),
                AvgMs = g.Average(x => (double?)x.WatchDurationSeconds)
            })
            .ToListAsync();

        // Slow-start count and total watched hours need their own filters/units.
        var slowStarts = await _db.AnalyticsEvents.AsNoTracking()
            .CountAsync(e => e.TenantId == tenantId && e.CreatedAt >= since
                && e.EventType == AnalyticsEventTypes.Startup && e.WatchDurationSeconds > SlowStartupThresholdMs);

        var watchSeconds = await _db.AnalyticsEvents.AsNoTracking()
            .Where(e => e.TenantId == tenantId && e.CreatedAt >= since && e.EventType == AnalyticsEventTypes.WatchProgress)
            .SumAsync(e => (long?)e.WatchDurationSeconds) ?? 0;
        var watchHours = watchSeconds / 3600.0;

        double WeightedAvg(string type) =>
            WeightedAverage(grouped.Where(r => r.EventType == type).Select(r => (r.Count, r.AvgMs)));
        int TotalOf(string type) => grouped.Where(r => r.EventType == type).Sum(r => r.Count);

        var rebufferCount = TotalOf(AnalyticsEventTypes.Rebuffer);
        var summary = new
        {
            AvgStartupMs = Math.Round(WeightedAvg(AnalyticsEventTypes.Startup)),
            StartupSamples = TotalOf(AnalyticsEventTypes.Startup),
            SlowStartups = slowStarts,
            RebufferCount = rebufferCount,
            AvgStallMs = Math.Round(WeightedAvg(AnalyticsEventTypes.Rebuffer)),
            ErrorCount = TotalOf(AnalyticsEventTypes.PlaybackError),
            WatchHours = Math.Round(watchHours, 1),
            RebuffersPerHour = watchHours > 0 ? Math.Round(rebufferCount / watchHours, 2) : 0
        };

        var byPlatform = grouped
            .GroupBy(r => r.Platform ?? "unknown")
            .Select(g => new
            {
                Platform = g.Key,
                AvgStartupMs = Math.Round(WeightedAverage(
                    g.Where(r => r.EventType == AnalyticsEventTypes.Startup).Select(r => (r.Count, r.AvgMs)))),
                RebufferCount = g.Where(r => r.EventType == AnalyticsEventTypes.Rebuffer).Sum(r => r.Count),
                ErrorCount = g.Where(r => r.EventType == AnalyticsEventTypes.PlaybackError).Sum(r => r.Count)
            })
            .OrderByDescending(x => x.RebufferCount + x.ErrorCount)
            .ToList();

        return Ok(ApiResponse<object>.Ok(new { summary, byPlatform }));
    }

    // Count-weighted mean of per-group averages (each group already carries its own avg + size).
    private static double WeightedAverage(IEnumerable<(int Count, double? AvgMs)> rows)
    {
        long weight = 0;
        double total = 0;
        foreach (var (count, avg) in rows)
        {
            if (avg is not { } a) continue;
            total += a * count;
            weight += count;
        }
        return weight > 0 ? total / weight : 0;
    }

    // ── Audit Log ──
    [HttpGet("audit-logs")]
    public async Task<IActionResult> GetAuditLogs([FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        var tenantId = HttpContext.GetTenantId();
        pageSize = Math.Clamp(pageSize, 1, 200);
        page = Math.Max(page, 1);

        var query = _db.AuditLogs.AsNoTracking().Where(a => a.TenantId == tenantId);
        var total = await query.CountAsync();
        var items = await query
            .OrderByDescending(a => a.CreatedAt)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(a => new
            {
                a.Id, a.ActorUserId, a.ActorEmail, a.Action, a.Path,
                a.StatusCode, a.IpAddress, a.CreatedAt
            })
            .ToListAsync();

        return Ok(ApiResponse<object>.Ok(new { total, page, pageSize, items }));
    }

    // ── CSV Exports ──
    // Tenant-scoped report downloads. Each returns a text/csv File with a dated filename.

    [HttpGet("exports/revenue.csv")]
    public async Task<IActionResult> ExportRevenue([FromQuery] string period = "30d")
    {
        var tenantId = HttpContext.GetTenantId();
        var days = period switch { "7d" => 7, "90d" => 90, "365d" => 365, _ => 30 };
        var since = DateTime.UtcNow.Date.AddDays(-days);

        var payments = await _db.Payments.AsNoTracking()
            .Where(p => p.TenantId == tenantId && p.Status == "success" && p.CreatedAt >= since)
            .OrderByDescending(p => p.CreatedAt)
            .Select(p => new { p.CreatedAt, p.GatewayPaymentId, p.UserId, p.Gateway, p.Amount, p.Currency, p.Description })
            .ToListAsync();

        var csv = OTT.API.Reporting.Csv.Build(
            new[] { "Date", "PaymentId", "UserId", "Gateway", "Amount", "Currency", "Description" },
            payments.Select(p => new object?[] { p.CreatedAt, p.GatewayPaymentId, p.UserId, p.Gateway, p.Amount, p.Currency, p.Description }));

        return File(csv, "text/csv", $"revenue_{DateTime.UtcNow:yyyyMMdd}.csv");
    }

    [HttpGet("exports/users.csv")]
    public async Task<IActionResult> ExportUsers()
    {
        var tenantId = HttpContext.GetTenantId();
        var users = await _db.Users.AsNoTracking()
            .Where(u => u.TenantId == tenantId && !u.IsDeleted)
            .OrderByDescending(u => u.CreatedAt)
            .Select(u => new { u.Id, u.Email, u.FirstName, u.LastName, u.Phone, u.Role, u.IsEmailVerified, u.IsBlocked, u.CreatedAt })
            .ToListAsync();

        var csv = OTT.API.Reporting.Csv.Build(
            new[] { "Id", "Email", "FirstName", "LastName", "Phone", "Role", "EmailVerified", "Blocked", "CreatedAt" },
            users.Select(u => new object?[] { u.Id, u.Email, u.FirstName, u.LastName, u.Phone, u.Role, u.IsEmailVerified, u.IsBlocked, u.CreatedAt }));

        return File(csv, "text/csv", $"users_{DateTime.UtcNow:yyyyMMdd}.csv");
    }

    [HttpGet("exports/content-analytics.csv")]
    public async Task<IActionResult> ExportContentAnalytics([FromQuery] string period = "30d")
    {
        var tenantId = HttpContext.GetTenantId();
        var days = period switch { "7d" => 7, "90d" => 90, "365d" => 365, _ => 30 };
        var since = DateTime.UtcNow.Date.AddDays(-days);

        var rows = await _db.ContentAnalytics.AsNoTracking()
            .Where(a => a.Date >= since && a.Content!.TenantId == tenantId)
            .OrderByDescending(a => a.Date)
            .Select(a => new { a.Date, a.ContentId, Title = a.Content!.Title, a.Views, a.UniqueViewers, a.TotalWatchSeconds })
            .ToListAsync();

        var csv = OTT.API.Reporting.Csv.Build(
            new[] { "Date", "ContentId", "Title", "Views", "UniqueViewers", "TotalWatchSeconds" },
            rows.Select(r => new object?[] { r.Date, r.ContentId, r.Title, r.Views, r.UniqueViewers, r.TotalWatchSeconds }));

        return File(csv, "text/csv", $"content-analytics_{DateTime.UtcNow:yyyyMMdd}.csv");
    }

    // ── Content ──
    [HttpGet("contents")]
    public async Task<IActionResult> GetContents([FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        [FromQuery] string? q = null, [FromQuery] string? type = null)
        => Ok(ApiResponse<object>.Ok(await _content.GetAdminContentAsync(HttpContext.GetTenantId(), page, pageSize, q, type)));

    // Single content for the admin editor — includes drafts (the public detail
    // endpoint filters to published only), and returns genre ids for prefill.
    [HttpGet("contents/{id:guid}")]
    public async Task<IActionResult> GetContent(Guid id)
    {
        var tenantId = HttpContext.GetTenantId();
        var c = await _db.Contents.IgnoreQueryFilters()
            .Include(x => x.ContentGenres)
            .FirstOrDefaultAsync(x => x.Id == id && x.TenantId == tenantId && !x.IsDeleted);
        if (c == null) return NotFound(ApiResponse<object>.Fail("Content not found"));
        return Ok(ApiResponse<object>.Ok(new
        {
            c.Id, c.Title, c.Description, c.ShortDescription, c.Type, c.MonetizationModel, c.AgeRating,
            c.ReleaseYear, c.Status, c.ThumbnailUrl, c.PosterUrl, c.BannerUrl, c.TrailerUrl,
            c.YoutubeId, c.VimeoId, c.HlsUrl, c.VideoSourceType, c.IsFeatured, c.IsTrending,
            GenreIds = c.ContentGenres.Select(g => g.GenreId).ToList()
        }));
    }

    // Upload a title image (thumbnail/poster/banner) and persist its URL on the
    // content so it sticks immediately (the create/update DTO carries URLs, not files).
    [HttpPost("contents/{id:guid}/image")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(15 * 1024 * 1024)]
    public async Task<IActionResult> UploadContentImage(Guid id, IFormFile file, [FromForm] string type = "thumbnail")
    {
        var tenantId = HttpContext.GetTenantId();
        var content = await _db.Contents.FirstOrDefaultAsync(c => c.Id == id && c.TenantId == tenantId);
        if (content == null) return NotFound(ApiResponse<object>.Fail("Content not found"));
        if (file == null || file.Length == 0) return BadRequest(ApiResponse<object>.Fail("No file"));

        var kind = type.ToLowerInvariant() switch { "poster" => "poster", "banner" => "banner", _ => "thumbnail" };
        var key = $"content-images/{id}/{kind}{Path.GetExtension(file.FileName)}";
        var url = await _storage.UploadPublicAsync(key, file.OpenReadStream(), file.ContentType);

        switch (kind)
        {
            case "poster": content.PosterUrl = url; break;
            case "banner": content.BannerUrl = url; break;
            default: content.ThumbnailUrl = url; break;
        }
        await _db.SaveChangesAsync();
        return Ok(ApiResponse<object>.Ok(new { url, type = kind }));
    }

    // Upload a source video file for a title, register the asset and kick off HLS
    // transcoding. (YouTube/Vimeo don't need this — they're saved as content fields.)
    [HttpPost("contents/{id:guid}/video")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(2L * 1024 * 1024 * 1024)] // 2 GB
    public async Task<IActionResult> UploadContentVideo(Guid id, [FromForm] SetVideoForm form, CancellationToken ct)
    {
        var tenantId = HttpContext.GetTenantId();
        var content = await _db.Contents.FirstOrDefaultAsync(c => c.Id == id && c.TenantId == tenantId);
        if (content == null) return NotFound(ApiResponse<object>.Fail("Content not found"));
        if (form.File == null || form.File.Length == 0) return BadRequest(ApiResponse<object>.Fail("Video file is required"));

        var key = $"uploads/{tenantId}/videos/{Guid.NewGuid():N}{Path.GetExtension(form.File.FileName)}";
        await using (var stream = form.File.OpenReadStream())
            await _storage.UploadAsync(stream, key, form.File.ContentType, ct);

        var asset = new VideoAsset
        {
            ContentId = id,
            OriginalFileName = form.File.FileName,
            OriginalKey = key,
            StorageProvider = "s3",
            Status = "pending",
            CreatedAt = DateTime.UtcNow
        };
        _db.VideoAssets.Add(asset);
        content.VideoSourceType = "upload";
        await _db.SaveChangesAsync(ct);

        // Enqueue transcoding (drained by the worker host; requires the worker running).
        await _video.StartTranscodingAsync(new TranscodeRequestDto { AssetId = asset.Id, SourceKey = key });
        return Ok(ApiResponse<object>.Ok(new { assetId = asset.Id, status = "transcoding" }));
    }

    [HttpPost("contents")]
    public async Task<IActionResult> CreateContent([FromBody] CreateContentDto dto)
        => Ok(ApiResponse<object>.Ok(await _content.CreateContentAsync(dto, HttpContext.GetTenantId())));

    [HttpPut("contents/{id:guid}")]
    public async Task<IActionResult> UpdateContent(Guid id, [FromBody] CreateContentDto dto)
        => Ok(ApiResponse<object>.Ok(await _content.UpdateContentAsync(id, HttpContext.GetTenantId(), dto)));

    [HttpDelete("contents/{id:guid}")]
    public async Task<IActionResult> DeleteContent(Guid id)
    {
        await _content.DeleteContentAsync(id, HttpContext.GetTenantId());
        return Ok(new { message = "Deleted" });
    }

    [HttpPost("contents/{id:guid}/publish")]
    public async Task<IActionResult> PublishContent(Guid id)
    {
        await _content.PublishContentAsync(id, HttpContext.GetTenantId());
        return Ok(new { message = "Published" });
    }

    [HttpGet("contents/{id:guid}/presigned-upload")]
    public async Task<IActionResult> GetPresignedUpload(Guid id, [FromQuery] string contentType = "video/mp4")
    {
        var result = await _storage.GetPresignedUploadUrlAsync($"raw/{id}/{Guid.NewGuid():N}.mp4", contentType, TimeSpan.FromHours(2));
        return Ok(new { uploadUrl = result.Url, key = result.Key, headers = result.Headers });
    }

    // ── Subtitle & Audio tracks (managed per title) ──
    [HttpGet("contents/{id:guid}/tracks")]
    public async Task<IActionResult> GetTracks(Guid id)
    {
        var tenantId = HttpContext.GetTenantId();
        if (!await _db.Contents.AnyAsync(c => c.Id == id && c.TenantId == tenantId))
            return NotFound(ApiResponse<object>.Fail("Content not found"));

        var subs = await _db.Subtitles.Where(s => s.ContentId == id).ToListAsync();
        var subtitles = subs.Select(s => new { s.Id, s.Language, s.LanguageCode, s.Label, s.Format, Url = _storage.GetPublicUrl(s.FileUrl) }).ToList();
        var audioTracks = await _db.AudioTracks.Where(a => a.ContentId == id).OrderBy(a => a.TrackIndex)
            .Select(a => new { a.Id, a.Language, a.LanguageCode, a.Label, a.TrackIndex, a.IsDefault }).ToListAsync();
        return Ok(ApiResponse<object>.Ok(new { subtitles, audioTracks }));
    }

    [HttpPost("contents/{id:guid}/subtitles")]
    [RequestSizeLimit(20 * 1024 * 1024)] // caption files are tiny; cap at 20 MB
    public async Task<IActionResult> AddSubtitle(Guid id, [FromForm] AddSubtitleForm form, CancellationToken ct)
    {
        var tenantId = HttpContext.GetTenantId();
        if (!await _db.Contents.AnyAsync(c => c.Id == id && c.TenantId == tenantId))
            return NotFound(ApiResponse<object>.Fail("Content not found"));
        if (form.File == null || form.File.Length == 0)
            return BadRequest(ApiResponse<object>.Fail("Subtitle file is required"));

        var ext = (form.Format ?? Path.GetExtension(form.File.FileName).TrimStart('.')).ToLowerInvariant();
        if (string.IsNullOrEmpty(ext)) ext = "vtt";
        if (ext != "vtt" && ext != "srt")
            return BadRequest(ApiResponse<object>.Fail("Only .vtt or .srt subtitle files are supported"));

        var key = $"subtitles/{id}/{Guid.NewGuid():N}.{ext}";
        var contentType = ext == "srt" ? "application/x-subrip" : "text/vtt";
        await using (var stream = form.File.OpenReadStream())
            await _storage.UploadPublicAsync(key, stream, contentType, ct);

        var asset = await _db.VideoAssets.FirstOrDefaultAsync(a => a.ContentId == id && a.EpisodeId == null);
        var sub = new Subtitle
        {
            ContentId = id,
            AssetId = asset?.Id,
            Language = form.Language,
            LanguageCode = form.Language,
            Label = string.IsNullOrWhiteSpace(form.Label) ? form.Language : form.Label!,
            FileUrl = key,
            Format = ext
        };
        _db.Subtitles.Add(sub);
        await _db.SaveChangesAsync(ct);
        return Ok(ApiResponse<object>.Ok(new { sub.Id, sub.Language, sub.Label, sub.Format, Url = _storage.GetPublicUrl(key) }));
    }

    [HttpDelete("contents/{id:guid}/subtitles/{subtitleId:guid}")]
    public async Task<IActionResult> DeleteSubtitle(Guid id, Guid subtitleId)
    {
        var tenantId = HttpContext.GetTenantId();
        if (!await _db.Contents.AnyAsync(c => c.Id == id && c.TenantId == tenantId))
            return NotFound(ApiResponse<object>.Fail("Content not found"));
        var sub = await _db.Subtitles.FirstOrDefaultAsync(s => s.Id == subtitleId && s.ContentId == id);
        if (sub == null) return NotFound(ApiResponse<object>.Fail("Subtitle not found"));
        try { await _storage.DeleteAsync(sub.FileUrl); } catch { /* best-effort; row removal is the source of truth */ }
        _db.Subtitles.Remove(sub);
        await _db.SaveChangesAsync();
        return Ok(new { message = "Deleted" });
    }

    [HttpPost("contents/{id:guid}/audio-tracks")]
    public async Task<IActionResult> AddAudioTrack(Guid id, [FromBody] AddAudioTrackDto dto)
    {
        var tenantId = HttpContext.GetTenantId();
        if (!await _db.Contents.AnyAsync(c => c.Id == id && c.TenantId == tenantId))
            return NotFound(ApiResponse<object>.Fail("Content not found"));
        var asset = await _db.VideoAssets.FirstOrDefaultAsync(a => a.ContentId == id && a.EpisodeId == null);
        var track = new AudioTrack
        {
            ContentId = id,
            AssetId = asset?.Id,
            Language = dto.Language,
            LanguageCode = dto.Language,
            Label = string.IsNullOrWhiteSpace(dto.Label) ? dto.Language : dto.Label!,
            TrackIndex = dto.TrackIndex,
            IsDefault = dto.IsDefault
        };
        _db.AudioTracks.Add(track);
        await _db.SaveChangesAsync();
        return Ok(ApiResponse<object>.Ok(new { track.Id, track.Language, track.Label, track.TrackIndex, track.IsDefault }));
    }

    [HttpDelete("contents/{id:guid}/audio-tracks/{trackId:guid}")]
    public async Task<IActionResult> DeleteAudioTrack(Guid id, Guid trackId)
    {
        var tenantId = HttpContext.GetTenantId();
        if (!await _db.Contents.AnyAsync(c => c.Id == id && c.TenantId == tenantId))
            return NotFound(ApiResponse<object>.Fail("Content not found"));
        var track = await _db.AudioTracks.FirstOrDefaultAsync(a => a.Id == trackId && a.ContentId == id);
        if (track == null) return NotFound(ApiResponse<object>.Fail("Audio track not found"));
        _db.AudioTracks.Remove(track);
        await _db.SaveChangesAsync();
        return Ok(new { message = "Deleted" });
    }

    // ── Branding ──
    [HttpGet("branding")]
    public async Task<IActionResult> GetBranding()
    {
        var tenantId = HttpContext.GetTenantId();
        var b = await _db.BrandingConfigs.FirstOrDefaultAsync(x => x.TenantId == tenantId);
        return Ok(ApiResponse<object?>.Ok(b));
    }

    [HttpPut("branding")]
    public async Task<IActionResult> UpdateBranding([FromBody] UpdateBrandingDto dto)
    {
        var tenantId = HttpContext.GetTenantId();
        var b = await _db.BrandingConfigs.FirstOrDefaultAsync(x => x.TenantId == tenantId);
        if (b == null)
        {
            b = new BrandingConfig { TenantId = tenantId };
            _db.BrandingConfigs.Add(b);
        }
        b.AppName = dto.AppName ?? b.AppName;
        b.LogoUrl = dto.LogoUrl ?? b.LogoUrl;
        b.FaviconUrl = dto.FaviconUrl ?? b.FaviconUrl;
        b.PrimaryColor = dto.PrimaryColor ?? b.PrimaryColor;
        b.SecondaryColor = dto.SecondaryColor ?? b.SecondaryColor;
        b.AccentColor = dto.AccentColor ?? b.AccentColor;
        b.BackgroundColor = dto.BackgroundColor ?? b.BackgroundColor;
        b.TextColor = dto.TextColor ?? b.TextColor;
        b.FontFamily = dto.FontFamily ?? b.FontFamily;
        b.CustomCss = dto.CustomCss ?? b.CustomCss;
        b.SplashScreenUrl = dto.SplashScreenUrl ?? b.SplashScreenUrl;
        b.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return Ok(ApiResponse<object>.Ok(b));
    }

    [HttpPost("branding/upload-logo")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> UploadLogo(IFormFile file, [FromForm] string type = "logo")
    {
        if (file == null || file.Length == 0) return BadRequest(new { error = "No file" });
        var key = $"branding/{HttpContext.GetTenantId()}/{type}{Path.GetExtension(file.FileName)}";
        var url = await _storage.UploadPublicAsync(key, file.OpenReadStream(), file.ContentType);
        return Ok(new { url });
    }

    // ── Banners ──
    [HttpGet("banners")]
    public async Task<IActionResult> GetBanners()
    {
        var tenantId = HttpContext.GetTenantId();
        return Ok(ApiResponse<object>.Ok(await _db.Banners.Where(b => b.TenantId == tenantId).OrderBy(b => b.SortOrder).ToListAsync()));
    }

    [HttpPost("banners")]
    public async Task<IActionResult> CreateBanner([FromBody] SaveBannerDto dto)
    {
        var banner = new Banner { TenantId = HttpContext.GetTenantId() };
        ApplyBanner(banner, dto);
        _db.Banners.Add(banner);
        await _db.SaveChangesAsync();
        return Ok(ApiResponse<object>.Ok(banner));
    }

    [HttpPut("banners/{id:guid}")]
    public async Task<IActionResult> UpdateBanner(Guid id, [FromBody] SaveBannerDto dto)
    {
        var tenantId = HttpContext.GetTenantId();
        var banner = await _db.Banners.FirstOrDefaultAsync(b => b.Id == id && b.TenantId == tenantId);
        if (banner == null) return NotFound();
        ApplyBanner(banner, dto);
        await _db.SaveChangesAsync();
        return Ok(ApiResponse<object>.Ok(banner));
    }

    [HttpDelete("banners/{id:guid}")]
    public async Task<IActionResult> DeleteBanner(Guid id)
    {
        var tenantId = HttpContext.GetTenantId();
        var banner = await _db.Banners.FirstOrDefaultAsync(b => b.Id == id && b.TenantId == tenantId);
        if (banner != null) { _db.Banners.Remove(banner); await _db.SaveChangesAsync(); }
        return Ok(new { message = "Deleted" });
    }

    private static void ApplyBanner(Banner b, SaveBannerDto dto)
    {
        b.Title = dto.Title ?? "";
        b.Subtitle = dto.Subtitle;
        b.Type = dto.Type;
        b.ImageUrl = dto.ImageUrl;
        b.MobileImageUrl = dto.MobileImageUrl;
        b.CtaText = dto.CtaText;
        b.CtaAction = dto.CtaAction;
        b.ContentId = dto.ContentId;
        b.SortOrder = dto.SortOrder;
        b.IsActive = dto.IsActive;
    }

    // ── Content rows ──
    [HttpGet("content-rows")]
    public async Task<IActionResult> GetContentRows()
    {
        var tenantId = HttpContext.GetTenantId();
        return Ok(ApiResponse<object>.Ok(await _db.ContentRows.Where(r => r.TenantId == tenantId).OrderBy(r => r.SortOrder).ToListAsync()));
    }

    [HttpPost("content-rows")]
    public async Task<IActionResult> CreateRow([FromBody] SaveContentRowDto dto)
    {
        var row = new ContentRow { TenantId = HttpContext.GetTenantId() };
        ApplyRow(row, dto);
        _db.ContentRows.Add(row);
        await _db.SaveChangesAsync();
        return Ok(ApiResponse<object>.Ok(row));
    }

    [HttpPut("content-rows/{id:guid}")]
    public async Task<IActionResult> UpdateRow(Guid id, [FromBody] SaveContentRowDto dto)
    {
        var tenantId = HttpContext.GetTenantId();
        var row = await _db.ContentRows.FirstOrDefaultAsync(r => r.Id == id && r.TenantId == tenantId);
        if (row == null) return NotFound();
        ApplyRow(row, dto);
        await _db.SaveChangesAsync();
        return Ok(ApiResponse<object>.Ok(row));
    }

    private static void ApplyRow(ContentRow r, SaveContentRowDto dto)
    {
        r.Title = dto.Title;
        r.RowType = dto.RowType;
        r.SourceValue = dto.SourceValue;
        r.DisplayStyle = dto.DisplayStyle;
        r.SortOrder = dto.SortOrder;
        r.MaxItems = dto.MaxItems;
        r.IsActive = dto.IsActive;
    }

    // ── App config (dynamic settings) ──
    [HttpGet("config")]
    public async Task<IActionResult> GetConfig()
        => Ok(ApiResponse<object>.Ok(await _settings.GetAllAsync(HttpContext.GetTenantId())));

    [HttpPut("config/{key}")]
    public async Task<IActionResult> UpdateConfig(string key, [FromBody] UpdateConfigValueDto dto)
    {
        await _settings.SetAsync(HttpContext.GetTenantId(), key, dto.Value, dto.IsPublic);
        return Ok(new { message = "Config updated" });
    }

    // ── Users ──
    [HttpGet("users")]
    public async Task<IActionResult> GetUsers([FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        [FromQuery] string? q = null, [FromQuery] string? role = null)
    {
        var tenantId = HttpContext.GetTenantId();
        var query = _db.Users.Where(u => u.TenantId == tenantId && !u.IsDeleted);
        if (!string.IsNullOrWhiteSpace(q))
            query = query.Where(u => u.Email.Contains(q) || (u.FirstName != null && u.FirstName.Contains(q)));
        if (!string.IsNullOrWhiteSpace(role))
            query = query.Where(u => u.Role == role);

        var total = await query.CountAsync();
        var users = await query.OrderByDescending(u => u.CreatedAt)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(u => new { u.Id, u.Email, u.FirstName, u.LastName, u.Role, u.IsBlocked, u.IsActive, u.CreatedAt })
            .ToListAsync();

        return Ok(ApiResponse<object>.Ok(new { items = users, totalCount = total, page, pageSize }));
    }

    [HttpPut("users/{id:guid}/status")]
    public async Task<IActionResult> UpdateUserStatus(Guid id, [FromBody] UserStatusDto dto)
    {
        var tenantId = HttpContext.GetTenantId();
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id && u.TenantId == tenantId);
        if (user == null) return NotFound();
        user.IsBlocked = dto.Status == "blocked";
        user.IsActive = dto.Status != "blocked";
        await _db.SaveChangesAsync();
        return Ok(new { message = "Status updated" });
    }

    // ── Live streams ──
    [HttpGet("live")]
    public async Task<IActionResult> GetLiveStreams([FromQuery] string? status = null, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        => Ok(ApiResponse<object>.Ok(await _live.GetStreamsAsync(HttpContext.GetTenantId(), status, page, pageSize)));

    [HttpPost("live")]
    public async Task<IActionResult> CreateLiveStream([FromBody] CreateLiveStreamDto dto)
        => Ok(ApiResponse<object>.Ok(await _live.CreateStreamAsync(dto, HttpContext.GetTenantId(), HttpContext.RequireUserId())));

    [HttpPut("live/{id:guid}")]
    public async Task<IActionResult> UpdateLiveStream(Guid id, [FromBody] CreateLiveStreamDto dto)
    {
        await _live.UpdateStreamAsync(id, dto);
        return Ok(new { message = "Stream updated" });
    }

    [HttpPost("live/{id:guid}/start")]
    public async Task<IActionResult> StartStream(Guid id)
    {
        await _live.StartStreamAsync(id, HttpContext.GetTenantId());
        return Ok(new { message = "Stream started" });
    }

    [HttpPost("live/{id:guid}/end")]
    public async Task<IActionResult> EndStream(Guid id)
    {
        await _live.StopStreamAsync(id, HttpContext.GetTenantId());
        return Ok(new { message = "Stream ended" });
    }

    // ── Community: suggestions & polls ──
    [HttpGet("suggestions")]
    public async Task<IActionResult> GetAdminSuggestions([FromQuery] string? status = null)
        => Ok(ApiResponse<object>.Ok(await _community.GetAdminSuggestionsAsync(HttpContext.GetTenantId(), status)));

    [HttpPut("suggestions/{id:guid}/status")]
    public async Task<IActionResult> UpdateSuggestionStatus(Guid id, [FromBody] UpdateSuggestionStatusDto dto)
        => Ok(ApiResponse<object>.Ok(await _community.UpdateSuggestionStatusAsync(HttpContext.GetTenantId(), id, dto)));

    [HttpDelete("suggestions/{id:guid}")]
    public async Task<IActionResult> DeleteSuggestion(Guid id)
    {
        await _community.DeleteSuggestionAsync(HttpContext.GetTenantId(), id);
        return Ok(new { message = "Deleted" });
    }

    [HttpPost("suggestions/{id:guid}/promote")]
    public async Task<IActionResult> PromoteSuggestion(Guid id, [FromBody] CreatePollDto dto)
        => Ok(ApiResponse<object>.Ok(await _community.PromoteSuggestionAsync(HttpContext.GetTenantId(), HttpContext.RequireUserId(), id, dto)));

    [HttpPost("polls")]
    public async Task<IActionResult> CreatePoll([FromBody] CreatePollDto dto)
        => Ok(ApiResponse<object>.Ok(await _community.CreatePollAsync(HttpContext.GetTenantId(), HttpContext.RequireUserId(), dto)));

    [HttpPut("polls/{id:guid}")]
    public async Task<IActionResult> UpdatePoll(Guid id, [FromBody] UpdatePollDto dto)
        => Ok(ApiResponse<object>.Ok(await _community.UpdatePollAsync(HttpContext.GetTenantId(), id, dto)));

    [HttpDelete("polls/{id:guid}")]
    public async Task<IActionResult> DeletePoll(Guid id)
    {
        await _community.DeletePollAsync(HttpContext.GetTenantId(), id);
        return Ok(new { message = "Deleted" });
    }

    // ── Genres ──
    [HttpGet("genres")]
    public async Task<IActionResult> GetGenres()
    {
        var tenantId = HttpContext.GetTenantId();
        return Ok(ApiResponse<object>.Ok(await _db.Genres.Where(g => g.TenantId == tenantId).OrderBy(g => g.SortOrder).ToListAsync()));
    }

    [HttpPost("genres")]
    public async Task<IActionResult> CreateGenre([FromBody] SaveGenreDto dto)
    {
        var genre = new Genre
        {
            TenantId = HttpContext.GetTenantId(),
            Name = dto.Name,
            Slug = dto.Slug,
            IconUrl = dto.IconUrl,
            SortOrder = dto.SortOrder
        };
        _db.Genres.Add(genre);
        await _db.SaveChangesAsync();
        return Ok(ApiResponse<object>.Ok(genre));
    }

    // ── Subscription plans ──
    [HttpGet("plans")]
    public async Task<IActionResult> GetAllPlans()
    {
        var tenantId = HttpContext.GetTenantId();
        return Ok(ApiResponse<object>.Ok(await _db.SubscriptionPlans.Where(p => p.TenantId == tenantId).OrderBy(p => p.Price).ToListAsync()));
    }

    [HttpPost("plans")]
    public async Task<IActionResult> CreatePlan([FromBody] SubscriptionPlanDto dto)
    {
        var plan = new SubscriptionPlan { TenantId = HttpContext.GetTenantId() };
        ApplyPlan(plan, dto);
        _db.SubscriptionPlans.Add(plan);
        await _db.SaveChangesAsync();
        return Ok(ApiResponse<object>.Ok(plan));
    }

    [HttpPut("plans/{id:guid}")]
    public async Task<IActionResult> UpdatePlan(Guid id, [FromBody] SubscriptionPlanDto dto)
    {
        var tenantId = HttpContext.GetTenantId();
        var plan = await _db.SubscriptionPlans.FirstOrDefaultAsync(p => p.Id == id && p.TenantId == tenantId);
        if (plan == null) return NotFound();
        ApplyPlan(plan, dto);
        await _db.SaveChangesAsync();
        return Ok(new { message = "Plan updated" });
    }

    private static void ApplyPlan(SubscriptionPlan p, SubscriptionPlanDto dto)
    {
        p.Name = dto.Name;
        p.Description = dto.Description;
        p.Price = dto.Price;
        p.Currency = dto.Currency;
        p.BillingCycle = dto.BillingCycle;
        p.MaxProfiles = dto.MaxProfiles;
        p.MaxStreams = dto.MaxStreams;
        p.AllowDownloads = dto.AllowDownloads;
        p.AllowUhd = dto.AllowUhd;
        p.Features = dto.Features.Count > 0 ? JsonSerializer.Serialize(dto.Features) : null;
        p.IsPopular = dto.IsPopular;
        p.IsActive = dto.IsActive;
        p.RazorpayPlanId = dto.RazorpayPlanId;
    }

    // ── Promo codes ──
    [HttpGet("promos")]
    public async Task<IActionResult> GetPromos()
    {
        var tenantId = HttpContext.GetTenantId();
        return Ok(ApiResponse<object>.Ok(await _db.PromoCodes.Where(p => p.TenantId == tenantId).ToListAsync()));
    }

    [HttpPost("promos")]
    public async Task<IActionResult> CreatePromo([FromBody] SavePromoDto dto)
    {
        var promo = new PromoCode
        {
            TenantId = HttpContext.GetTenantId(),
            Code = dto.Code,
            DiscountType = dto.DiscountType,
            DiscountValue = dto.DiscountValue,
            MaxUses = dto.MaxUses,
            ExpiresAt = dto.ExpiresAt,
            IsActive = true
        };
        _db.PromoCodes.Add(promo);
        await _db.SaveChangesAsync();
        return Ok(ApiResponse<object>.Ok(promo));
    }

    // ── Creators ──
    [HttpGet("creators")]
    public async Task<IActionResult> GetCreators([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var tenantId = HttpContext.GetTenantId();
        var query = _db.CreatorApplications.Where(c => c.TenantId == tenantId);
        var total = await query.CountAsync();
        var items = await query.OrderByDescending(c => c.CreatedAt)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        return Ok(ApiResponse<object>.Ok(new { items, totalCount = total, page, pageSize }));
    }

    [HttpPut("creators/{id:guid}/approve")]
    public async Task<IActionResult> ApproveCreator(Guid id)
    {
        var tenantId = HttpContext.GetTenantId();
        var app = await _db.CreatorApplications.FirstOrDefaultAsync(c => c.Id == id && c.TenantId == tenantId);
        if (app == null) return NotFound();
        app.Status = "approved";
        app.ReviewedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return Ok(new { message = "Creator approved" });
    }
}
