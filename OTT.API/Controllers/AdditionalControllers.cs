using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OTT.API.Middleware;
using OTT.Application.DTOs;
using OTT.Application.Services;
using OTT.Infrastructure.Data;
using OTT.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace OTT.API.Controllers;

// ── Live Stream Controller ─────────────────────────────────────────────────────

[ApiController]
[Route("api/[controller]")]
public class LiveController : ControllerBase
{
    private readonly ILiveStreamService _liveService;

    public LiveController(ILiveStreamService liveService) => _liveService = liveService;

    [HttpGet]
    public async Task<IActionResult> GetStreams([FromQuery] string? status, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var tenantId = HttpContext.GetTenantId();
        return Ok(ApiResponse<object>.Ok(await _liveService.GetStreamsAsync(tenantId, status, page, pageSize)));
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetStream(Guid id)
    {
        var tenantId = HttpContext.GetTenantId();
        return Ok(ApiResponse<LiveStreamDetailDto>.Ok(await _liveService.GetStreamAsync(id, tenantId)));
    }

    [HttpPost]
    [Authorize(Roles = "admin,creator")]
    public async Task<IActionResult> Create([FromBody] CreateLiveStreamDto dto)
    {
        var tenantId = HttpContext.GetTenantId();
        var userId = HttpContext.RequireUserId();
        var result = await _liveService.CreateStreamAsync(dto, tenantId, userId);
        return Ok(ApiResponse<LiveStreamDetailDto>.Ok(result));
    }

    [HttpPut("{id}")]
    [Authorize(Roles = "admin,creator")]
    public async Task<IActionResult> Update(Guid id, [FromBody] CreateLiveStreamDto dto)
    {
        await _liveService.UpdateStreamAsync(id, dto);
        return Ok(ApiResponse<object>.Ok(null, "Stream updated"));
    }

    [HttpPost("{id}/start")]
    [Authorize(Roles = "admin,creator")]
    public async Task<IActionResult> Start(Guid id)
    {
        var tenantId = HttpContext.GetTenantId();
        await _liveService.StartStreamAsync(id, tenantId);
        return Ok(ApiResponse<object>.Ok(null, "Stream started"));
    }

    [HttpPost("{id}/stop")]
    [Authorize(Roles = "admin,creator")]
    public async Task<IActionResult> Stop(Guid id)
    {
        var tenantId = HttpContext.GetTenantId();
        await _liveService.StopStreamAsync(id, tenantId);
        return Ok(ApiResponse<object>.Ok(null, "Stream stopped"));
    }

    [HttpPost("{id}/regenerate-key")]
    [Authorize(Roles = "admin,creator")]
    public async Task<IActionResult> RegenerateKey(Guid id)
    {
        var key = await _liveService.RegenerateStreamKeyAsync(id);
        return Ok(ApiResponse<StreamKeyDto>.Ok(key));
    }

    [HttpDelete("{id}")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> Delete(Guid id)
    {
        await _liveService.DeleteStreamAsync(id);
        return Ok(ApiResponse<object>.Ok(null, "Stream deleted"));
    }
}

// ── Watch Party Controller ─────────────────────────────────────────────────────

[ApiController]
[Route("api/watch-party")]
[Authorize]
public class WatchPartyController : ControllerBase
{
    private readonly OttDbContext _db;

    public WatchPartyController(OttDbContext db) => _db = db;

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateWatchPartyDto dto)
    {
        var userId = HttpContext.RequireUserId();
        var code = GeneratePartyCode();

        var party = new Domain.Entities.WatchParty
        {
            ContentId = dto.ContentId,
            EpisodeId = dto.EpisodeId,
            HostUserId = userId,
            Code = code,
            IsPrivate = dto.IsPrivate,
            MaxMembers = Math.Clamp(dto.MaxMembers, 2, 50),
            Status = "active",
            CreatedAt = DateTime.UtcNow
        };

        _db.WatchParties.Add(party);
        party.Members.Add(new Domain.Entities.WatchPartyMember
        {
            WatchPartyId = party.Id,
            UserId = userId,
            JoinedAt = DateTime.UtcNow
        });

        await _db.SaveChangesAsync();
        return Ok(ApiResponse<object>.Ok(new { partyId = party.Id, code = party.Code }));
    }

    [HttpGet("{code}")]
    public async Task<IActionResult> GetByCode(string code)
    {
        var party = await _db.WatchParties
            .Include(p => p.Content)
            .Include(p => p.Members)
            .FirstOrDefaultAsync(p => p.Code == code && p.Status == "active");

        if (party == null) return NotFound(ApiResponse<object>.Fail("Party not found or has ended"));

        return Ok(ApiResponse<object>.Ok(new
        {
            id = party.Id,
            code = party.Code,
            contentId = party.ContentId,
            hostUserId = party.HostUserId,
            memberCount = party.Members.Count,
            maxMembers = party.MaxMembers,
            status = party.Status
        }));
    }

    [HttpPost("{partyId}/end")]
    public async Task<IActionResult> EndParty(Guid partyId)
    {
        var userId = HttpContext.RequireUserId();
        var party = await _db.WatchParties.FirstOrDefaultAsync(p => p.Id == partyId && p.HostUserId == userId);
        if (party == null) return NotFound();

        party.Status = "ended";
        party.EndedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return Ok(ApiResponse<object>.Ok(null));
    }

    private static string GeneratePartyCode()
    {
        var chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        var random = new Random();
        return new string(Enumerable.Repeat(chars, 6).Select(s => s[random.Next(s.Length)]).ToArray());
    }
}

// ── Profiles Controller ────────────────────────────────────────────────────────

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ProfilesController : ControllerBase
{
    private readonly OttDbContext _db;

    public ProfilesController(OttDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> GetProfiles()
    {
        var userId = HttpContext.RequireUserId();
        var profiles = await _db.UserProfiles
            .Where(p => p.UserId == userId)
            .Select(p => new ProfileDto
            {
                Id = p.Id,
                Name = p.Name,
                AvatarUrl = p.AvatarUrl,
                IsDefault = p.IsDefault,
                MaturityLevel = p.MaturityLevel,
                Language = p.Language,
                IsPinLocked = p.PinHash != null
            })
            .ToListAsync();

        return Ok(ApiResponse<List<ProfileDto>>.Ok(profiles));
    }

    [HttpPost]
    public async Task<IActionResult> CreateProfile([FromBody] CreateProfileDto dto)
    {
        var userId = HttpContext.RequireUserId();
        var count = await _db.UserProfiles.CountAsync(p => p.UserId == userId);

        // Check plan limit
        var sub = await _db.UserSubscriptions
            .Include(s => s.Plan)
            .FirstOrDefaultAsync(s => s.UserId == userId && s.Status == "active");
        var maxProfiles = sub?.Plan?.MaxProfiles ?? 1;

        if (count >= maxProfiles)
            return BadRequest(ApiResponse<object>.Fail($"Your plan allows a maximum of {maxProfiles} profiles"));

        var profile = new Domain.Entities.UserProfile
        {
            UserId = userId,
            Name = dto.Name,
            AvatarUrl = dto.AvatarUrl,
            MaturityLevel = dto.MaturityLevel ?? "all",
            Language = dto.Language,
            IsDefault = count == 0
        };

        if (!string.IsNullOrEmpty(dto.Pin))
            profile.PinHash = BCrypt.Net.BCrypt.HashPassword(dto.Pin);

        _db.UserProfiles.Add(profile);
        await _db.SaveChangesAsync();
        return Ok(ApiResponse<object>.Ok(new ProfileDto
        {
            Id = profile.Id, Name = profile.Name, AvatarUrl = profile.AvatarUrl,
            IsDefault = profile.IsDefault, MaturityLevel = profile.MaturityLevel
        }));
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateProfile(Guid id, [FromBody] CreateProfileDto dto)
    {
        var userId = HttpContext.RequireUserId();
        var profile = await _db.UserProfiles.FirstOrDefaultAsync(p => p.Id == id && p.UserId == userId);
        if (profile == null) return NotFound();

        profile.Name = dto.Name ?? profile.Name;
        profile.AvatarUrl = dto.AvatarUrl ?? profile.AvatarUrl;
        profile.MaturityLevel = dto.MaturityLevel ?? profile.MaturityLevel;
        profile.Language = dto.Language ?? profile.Language;

        if (!string.IsNullOrEmpty(dto.Pin))
            profile.PinHash = BCrypt.Net.BCrypt.HashPassword(dto.Pin);

        await _db.SaveChangesAsync();
        return Ok(ApiResponse<object>.Ok(null, "Profile updated"));
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteProfile(Guid id)
    {
        var userId = HttpContext.RequireUserId();
        var profile = await _db.UserProfiles.FirstOrDefaultAsync(p => p.Id == id && p.UserId == userId);
        if (profile == null) return NotFound();
        if (profile.IsDefault) return BadRequest(ApiResponse<object>.Fail("Cannot delete default profile"));

        _db.UserProfiles.Remove(profile);
        await _db.SaveChangesAsync();
        return Ok(ApiResponse<object>.Ok(null, "Profile deleted"));
    }

    [HttpPost("{id}/verify-pin")]
    public async Task<IActionResult> VerifyPin(Guid id, [FromBody] VerifyPinDto dto)
    {
        var userId = HttpContext.RequireUserId();
        var profile = await _db.UserProfiles.FirstOrDefaultAsync(p => p.Id == id && p.UserId == userId);
        if (profile == null) return NotFound();

        if (string.IsNullOrEmpty(profile.PinHash))
            return Ok(ApiResponse<object>.Ok(new { valid = true }));

        var valid = BCrypt.Net.BCrypt.Verify(dto.Pin, profile.PinHash);
        return Ok(ApiResponse<object>.Ok(new { valid }));
    }
}

public record CreateProfileDto(string? Name, string? AvatarUrl, string? MaturityLevel, string? Language, string? Pin);
public record VerifyPinDto(string Pin);

// ── Upload Controller ─────────────────────────────────────────────────────────

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class UploadController : ControllerBase
{
    private readonly IVideoService _videoService;

    public UploadController(IVideoService videoService) => _videoService = videoService;

    [HttpPost("presigned-url")]
    public async Task<IActionResult> GetPresignedUrl([FromBody] UploadRequestDto request)
    {
        var tenantId = HttpContext.GetTenantId();
        var result = await _videoService.GetUploadUrlAsync(request, tenantId);
        return Ok(ApiResponse<UploadUrlResponseDto>.Ok(result));
    }

    [HttpPost("transcode")]
    [Authorize(Roles = "admin,creator")]
    public async Task<IActionResult> StartTranscode([FromBody] TranscodeRequestDto request)
    {
        var jobId = await _videoService.StartTranscodingAsync(request);
        return Ok(ApiResponse<object>.Ok(new { jobId }));
    }

    [HttpGet("asset/{assetId}")]
    public async Task<IActionResult> GetAssetStatus(Guid assetId)
    {
        var asset = await _videoService.GetAssetStatusAsync(assetId);
        if (asset == null) return NotFound();
        return Ok(ApiResponse<object>.Ok(new
        {
            id = asset.Id,
            status = asset.Status,
            hlsPath = asset.HlsPath,
            durationSeconds = asset.DurationSeconds,
            errorMessage = asset.ErrorMessage
        }));
    }

    [HttpPost("validate-youtube")]
    public async Task<IActionResult> ValidateYoutube([FromBody] UrlValidateDto dto)
    {
        var id = await _videoService.ExtractYouTubeInfoAsync(dto.Url);
        return Ok(ApiResponse<object>.Ok(new { valid = id != null, videoId = id }));
    }

    [HttpPost("validate-vimeo")]
    public async Task<IActionResult> ValidateVimeo([FromBody] UrlValidateDto dto)
    {
        var id = await _videoService.ExtractVimeoInfoAsync(dto.Url);
        return Ok(ApiResponse<object>.Ok(new { valid = id != null, videoId = id }));
    }
}

public record UrlValidateDto(string Url);

// ── Config / App Settings Controller ─────────────────────────────────────────

[ApiController]
[Route("api/[controller]")]
public class ConfigController : ControllerBase
{
    private readonly OttDbContext _db;
    private readonly IDynamicSettingsService _settings;
    private readonly IConfiguration _config;
    private readonly OTT.Application.Services.IFeatureFlagService _flags;

    public ConfigController(OttDbContext db, IDynamicSettingsService settings, IConfiguration config,
        OTT.Application.Services.IFeatureFlagService flags)
    {
        _db = db;
        _settings = settings;
        _config = config;
        _flags = flags;
    }

    // Feature flags evaluated for the current caller (honours %-rollout + targeting). The client
    // should read these per-user rather than the raw values in /public.
    [HttpGet("features")]
    public async Task<IActionResult> GetFeatures()
    {
        var flags = await _flags.EvaluateAllAsync(HttpContext.GetTenantId(), HttpContext.GetUserId());
        return Ok(ApiResponse<object>.Ok(flags));
    }

    // One-call bootstrap for the client app: branding + feature flags + enabled
    // payment gateways + social login IDs + public app settings. No secrets.
    [HttpGet("public")]
    public async Task<IActionResult> GetPublicConfig()
    {
        var tenantId = HttpContext.GetTenantId();
        var branding = await _db.BrandingConfigs.FirstOrDefaultAsync(b => b.TenantId == tenantId);
        var publicSettings = await _settings.GetAllAsync(tenantId, publicOnly: true);

        var payload = new
        {
            branding = new BrandingDto
            {
                AppName = branding?.AppName ?? _config["App:AppName"] ?? "OTT Platform",
                LogoUrl = branding?.LogoUrl,
                FaviconUrl = branding?.FaviconUrl,
                PrimaryColor = branding?.PrimaryColor ?? "#E50914",
                SecondaryColor = branding?.SecondaryColor ?? "#141414",
                AccentColor = branding?.AccentColor ?? "#FFFFFF",
                BackgroundColor = branding?.BackgroundColor,
                TextColor = branding?.TextColor,
                FontFamily = branding?.FontFamily,
                CustomCss = branding?.CustomCss,
                SplashScreenUrl = branding?.SplashScreenUrl
            },
            features = new
            {
                downloads = await _settings.GetBoolAsync(tenantId, SettingKeys.FeatureDownloads, true),
                liveTv = await _settings.GetBoolAsync(tenantId, SettingKeys.FeatureLiveTv, true),
                watchParty = await _settings.GetBoolAsync(tenantId, SettingKeys.FeatureWatchParty, true),
                signupEnabled = await _settings.GetBoolAsync(tenantId, SettingKeys.FeatureSignupEnabled, true),
                maintenanceMode = await _settings.GetBoolAsync(tenantId, SettingKeys.FeatureMaintenanceMode, false),
                requireEmailVerification = await _settings.GetBoolAsync(tenantId, SettingKeys.RequireEmailVerification,
                    string.Equals(_config["App:RequireEmailVerification"], "true", StringComparison.OrdinalIgnoreCase))
            },
            payments = new
            {
                razorpay = new
                {
                    enabled = await _settings.GetBoolAsync(tenantId, SettingKeys.RazorpayEnabled, !string.IsNullOrWhiteSpace(_config["Razorpay:KeyId"])),
                    keyId = await _settings.GetAsync(tenantId, SettingKeys.RazorpayKeyId, _config["Razorpay:KeyId"])
                },
                stripe = new
                {
                    enabled = await _settings.GetBoolAsync(tenantId, SettingKeys.StripeEnabled, !string.IsNullOrWhiteSpace(_config["Stripe:PublishableKey"])),
                    publishableKey = await _settings.GetAsync(tenantId, SettingKeys.StripePublishableKey, _config["Stripe:PublishableKey"])
                }
            },
            social = new
            {
                googleClientId = await _settings.GetAsync(tenantId, SettingKeys.GoogleClientId, _config["Social:Google:ClientId"]),
                facebookAppId = await _settings.GetAsync(tenantId, SettingKeys.FacebookAppId, _config["Social:Facebook:AppId"]),
                appleBundleId = await _settings.GetAsync(tenantId, SettingKeys.AppleBundleId, _config["Apple:BundleId"])
            },
            settings = publicSettings,
            app = new
            {
                baseUrl = _config["App:BaseUrl"],
                tenantId
            }
        };

        return Ok(ApiResponse<object>.Ok(payload));
    }

    [HttpGet("branding")]
    public async Task<IActionResult> GetBranding()
    {
        var tenantId = HttpContext.GetTenantId();
        var branding = await _db.BrandingConfigs.FirstOrDefaultAsync(b => b.TenantId == tenantId);

        return Ok(ApiResponse<BrandingDto>.Ok(new BrandingDto
        {
            AppName = branding?.AppName ?? "OTT Platform",
            LogoUrl = branding?.LogoUrl,
            FaviconUrl = branding?.FaviconUrl,
            PrimaryColor = branding?.PrimaryColor ?? "#E50914",
            SecondaryColor = branding?.SecondaryColor ?? "#141414",
            AccentColor = branding?.AccentColor ?? "#FFFFFF",
            BackgroundColor = branding?.BackgroundColor,
            TextColor = branding?.TextColor,
            FontFamily = branding?.FontFamily,
            CustomCss = branding?.CustomCss,
            SplashScreenUrl = branding?.SplashScreenUrl
        }));
    }

    [HttpGet("genres")]
    public async Task<IActionResult> GetGenres()
    {
        var tenantId = HttpContext.GetTenantId();
        var genres = await _db.Genres
            .Where(g => g.TenantId == tenantId)
            .OrderBy(g => g.Name)
            .Select(g => new { g.Id, g.Name, g.Slug, g.IconUrl })
            .ToListAsync();
        return Ok(ApiResponse<object>.Ok(genres));
    }

    [HttpGet("app-settings")]
    public async Task<IActionResult> GetAppSettings()
    {
        var tenantId = HttpContext.GetTenantId();
        var configs = await _db.AppConfigs
            .Where(c => c.TenantId == tenantId && c.IsPublic)
            .ToDictionaryAsync(c => c.Key, c => c.Value);
        return Ok(ApiResponse<object>.Ok(configs));
    }
}

// ── Notification Controller ────────────────────────────────────────────────────

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class NotificationsController : ControllerBase
{
    private readonly OttDbContext _db;

    public NotificationsController(OttDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> GetNotifications([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var userId = HttpContext.RequireUserId();
        var total = await _db.Notifications.CountAsync(n => n.UserId == userId);
        var items = await _db.Notifications
            .Where(n => n.UserId == userId)
            .OrderByDescending(n => n.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(n => new { n.Id, n.Title, n.Body, n.Type, n.IsRead, n.CreatedAt, n.ActionUrl })
            .ToListAsync();

        return Ok(ApiResponse<object>.Ok(new { items, total, page, pageSize, unreadCount = await _db.Notifications.CountAsync(n => n.UserId == userId && !n.IsRead) }));
    }

    [HttpPost("{id}/read")]
    public async Task<IActionResult> MarkRead(Guid id)
    {
        var userId = HttpContext.RequireUserId();
        var n = await _db.Notifications.FirstOrDefaultAsync(n => n.Id == id && n.UserId == userId);
        if (n == null) return NotFound();
        n.IsRead = true;
        n.ReadAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return Ok(ApiResponse<object>.Ok(null));
    }

    [HttpPost("read-all")]
    public async Task<IActionResult> MarkAllRead()
    {
        var userId = HttpContext.RequireUserId();
        var now = DateTime.UtcNow;
        await _db.Notifications
            .Where(n => n.UserId == userId && !n.IsRead)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.IsRead, true).SetProperty(n => n.ReadAt, now));
        return Ok(ApiResponse<object>.Ok(null));
    }

    [HttpPost("device-token")]
    public async Task<IActionResult> RegisterDeviceToken([FromBody] DeviceTokenDto dto)
    {
        var userId = HttpContext.RequireUserId();
        var existing = await _db.DeviceTokens.FirstOrDefaultAsync(d => d.Token == dto.Token);

        if (existing != null)
        {
            existing.UserId = userId;
            existing.Platform = dto.Platform;
            existing.IsActive = true;
            existing.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            _db.DeviceTokens.Add(new Domain.Entities.DeviceToken
            {
                UserId = userId,
                Token = dto.Token,
                Platform = dto.Platform,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            });
        }

        await _db.SaveChangesAsync();
        return Ok(ApiResponse<object>.Ok(null));
    }
}

public record DeviceTokenDto(string Token, string Platform);
