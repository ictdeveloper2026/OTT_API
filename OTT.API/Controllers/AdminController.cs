using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OTT.API.Middleware;
using OTT.Application.DTOs;
using OTT.Application.Services;
using OTT.Domain.Entities;
using OTT.Infrastructure.Data;
using OTT.Infrastructure.Services;
using System.Text.Json;

namespace OTT.API.Controllers;

// ── Admin request bodies ────────────────────────────────────────────────────────
public record SaveBannerDto(string? Title, string? Subtitle, string Type, string? ImageUrl,
    string? MobileImageUrl, string? CtaText, string? CtaAction, Guid? ContentId, int SortOrder, bool IsActive);
public record SaveContentRowDto(string Title, string RowType, string? SourceValue, string DisplayStyle, int SortOrder, int MaxItems, bool IsActive);
public record SaveGenreDto(string Name, string Slug, string? IconUrl, int SortOrder);
public record SavePromoDto(string Code, string DiscountType, decimal DiscountValue, int? MaxUses, DateTime? ExpiresAt);
public record UserStatusDto(string Status); // active | blocked
public record UpdateConfigValueDto(string? Value, bool IsPublic);

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

    public AdminController(OttDbContext db, IContentService content, ILiveStreamService live,
        IStorageService storage, IDynamicSettingsService settings)
    {
        _db = db; _content = content; _live = live; _storage = storage; _settings = settings;
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

    // ── Content ──
    [HttpGet("contents")]
    public async Task<IActionResult> GetContents([FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        [FromQuery] string? q = null, [FromQuery] string? type = null)
        => Ok(ApiResponse<object>.Ok(await _content.GetAdminContentAsync(HttpContext.GetTenantId(), page, pageSize, q, type)));

    [HttpPost("contents")]
    public async Task<IActionResult> CreateContent([FromBody] CreateContentDto dto)
        => Ok(ApiResponse<object>.Ok(await _content.CreateContentAsync(dto, HttpContext.GetTenantId())));

    [HttpPut("contents/{id:guid}")]
    public async Task<IActionResult> UpdateContent(Guid id, [FromBody] CreateContentDto dto)
        => Ok(ApiResponse<object>.Ok(await _content.UpdateContentAsync(id, dto)));

    [HttpDelete("contents/{id:guid}")]
    public async Task<IActionResult> DeleteContent(Guid id)
    {
        await _content.DeleteContentAsync(id);
        return Ok(new { message = "Deleted" });
    }

    [HttpPost("contents/{id:guid}/publish")]
    public async Task<IActionResult> PublishContent(Guid id)
    {
        await _content.PublishContentAsync(id);
        return Ok(new { message = "Published" });
    }

    [HttpGet("contents/{id:guid}/presigned-upload")]
    public async Task<IActionResult> GetPresignedUpload(Guid id, [FromQuery] string contentType = "video/mp4")
    {
        var result = await _storage.GetPresignedUploadUrlAsync($"raw/{id}/{Guid.NewGuid():N}.mp4", contentType, TimeSpan.FromHours(2));
        return Ok(new { uploadUrl = result.Url, key = result.Key, headers = result.Headers });
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
        var banner = await _db.Banners.FindAsync(id);
        if (banner == null) return NotFound();
        ApplyBanner(banner, dto);
        await _db.SaveChangesAsync();
        return Ok(ApiResponse<object>.Ok(banner));
    }

    [HttpDelete("banners/{id:guid}")]
    public async Task<IActionResult> DeleteBanner(Guid id)
    {
        var banner = await _db.Banners.FindAsync(id);
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
        var row = await _db.ContentRows.FindAsync(id);
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
        var user = await _db.Users.FindAsync(id);
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
        var plan = await _db.SubscriptionPlans.FindAsync(id);
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
        var app = await _db.CreatorApplications.FindAsync(id);
        if (app == null) return NotFound();
        app.Status = "approved";
        app.ReviewedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return Ok(new { message = "Creator approved" });
    }
}
