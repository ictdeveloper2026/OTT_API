using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OTT.Application.DTOs.Admin;
using OTT.Application.DTOs.Content;
using OTT.Application.Interfaces;

namespace OTT.API.Controllers;

[ApiController]
[Route("api/admin")]
[Authorize(Policy = "Admin")]
public class AdminController : ControllerBase
{
    private readonly IAdminService _admin;
    private readonly IContentService _content;
    private readonly IVideoService _video;
    private readonly IBrandingService _branding;
    private readonly ILiveStreamService _live;
    private readonly IAnalyticsService _analytics;
    private readonly IStorageService _storage;
    private readonly ISubscriptionService _sub;

    public AdminController(IAdminService admin, IContentService content, IVideoService video,
        IBrandingService branding, ILiveStreamService live, IAnalyticsService analytics,
        IStorageService storage, ISubscriptionService sub)
    {
        _admin = admin; _content = content; _video = video; _branding = branding;
        _live = live; _analytics = analytics; _storage = storage; _sub = sub;
    }

    // ── Dashboard Stats ──
    [HttpGet("stats")]
    public async Task<IActionResult> GetStats()
    {
        return Ok(await _admin.GetDashboardStatsAsync());
    }

    [HttpGet("revenue")]
    public async Task<IActionResult> GetRevenue([FromQuery] string period = "30d")
    {
        return Ok(await _analytics.GetRevenueReportAsync(period));
    }

    // ── Content Management ──
    [Authorize(Policy = "ContentManager")]
    [HttpGet("contents")]
    public async Task<IActionResult> GetContents([FromQuery] int page = 1, [FromQuery] string? status = null, [FromQuery] string? q = null)
    {
        return Ok(await _admin.GetContentsAsync(page, status, q));
    }

    [Authorize(Policy = "ContentManager")]
    [HttpPost("contents")]
    public async Task<IActionResult> CreateContent([FromBody] CreateContentRequest req)
    {
        var result = await _content.CreateContentAsync(req);
        return CreatedAtAction(nameof(GetContents), new { id = result.Id }, result);
    }

    [Authorize(Policy = "ContentManager")]
    [HttpPut("contents/{id}")]
    public async Task<IActionResult> UpdateContent(int id, [FromBody] UpdateContentRequest req)
    {
        var result = await _content.UpdateContentAsync(id, req);
        return Ok(result);
    }

    [Authorize(Policy = "ContentManager")]
    [HttpDelete("contents/{id}")]
    public async Task<IActionResult> DeleteContent(int id)
    {
        await _content.DeleteContentAsync(id);
        return Ok(new { message = "Deleted" });
    }

    // ── Video Upload (HLS or link YouTube/Vimeo) ──
    [Authorize(Policy = "ContentManager")]
    [HttpPost("contents/{id}/upload")]
    [RequestSizeLimit(10_000_000_000)] // 10GB
    public async Task<IActionResult> UploadVideo(int id, IFormFile file)
    {
        if (file == null || file.Length == 0) return BadRequest(new { error = "No file" });
        var jobId = await _video.UploadAndTranscodeAsync(id, file);
        return Accepted(new { message = "Upload queued for transcoding", jobId });
    }

    [Authorize(Policy = "ContentManager")]
    [HttpPost("contents/{id}/video-link")]
    public async Task<IActionResult> LinkVideo(int id, [FromBody] LinkVideoRequest req)
    {
        await _video.LinkExternalVideoAsync(id, req.PlayerType, req.VideoId!);
        return Ok(new { message = $"{req.PlayerType} video linked" });
    }

    // ── Subtitles ──
    [Authorize(Policy = "ContentManager")]
    [HttpPost("contents/{id}/subtitles")]
    public async Task<IActionResult> UploadSubtitle(int id, IFormFile file, [FromForm] int languageId, [FromForm] string format = "VTT")
    {
        var url = await _video.UploadSubtitleAsync(id, languageId, format, file);
        return Ok(new { url });
    }

    // ── Presigned S3 Upload URL (direct-to-S3) ──
    [Authorize(Policy = "ContentManager")]
    [HttpGet("contents/{id}/presigned-upload")]
    public async Task<IActionResult> GetPresignedUpload(int id, [FromQuery] string contentType = "video/mp4")
    {
        var result = await _storage.GetPresignedUploadUrlAsync($"raw/{id}/{Guid.NewGuid()}.mp4", contentType, TimeSpan.FromHours(2));
        return Ok(new { uploadUrl = result.Url, s3Key = result.Key });
    }

    // ── Branding ──
    [HttpGet("branding")]
    public async Task<IActionResult> GetBranding()
    {
        return Ok(await _branding.GetAsync());
    }

    [HttpPut("branding")]
    public async Task<IActionResult> UpdateBranding([FromBody] UpdateBrandingRequest req)
    {
        var result = await _branding.UpdateAsync(req);
        return Ok(result);
    }

    [HttpPost("branding/upload-logo")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> UploadLogo(IFormFile file, [FromForm] string type = "logo")
    {
        var ext = Path.GetExtension(file.FileName);
        var key = $"branding/{type}{ext}";
        var url = await _storage.UploadPublicAsync(key, file.OpenReadStream(), file.ContentType);
        return Ok(new { url });
    }

    // ── Banners ──
    [HttpGet("banners")]
    public async Task<IActionResult> GetBanners() => Ok(await _admin.GetBannersAsync());

    [HttpPost("banners")]
    public async Task<IActionResult> CreateBanner([FromBody] SaveBannerRequest req) => Ok(await _admin.SaveBannerAsync(null, req));

    [HttpPut("banners/{id}")]
    public async Task<IActionResult> UpdateBanner(int id, [FromBody] SaveBannerRequest req) => Ok(await _admin.SaveBannerAsync(id, req));

    [HttpDelete("banners/{id}")]
    public async Task<IActionResult> DeleteBanner(int id) { await _admin.DeleteBannerAsync(id); return Ok(); }

    // ── Content Rows ──
    [HttpGet("content-rows")]
    public async Task<IActionResult> GetContentRows() => Ok(await _admin.GetContentRowsAsync());

    [HttpPost("content-rows")]
    public async Task<IActionResult> CreateRow([FromBody] SaveContentRowRequest req) => Ok(await _admin.SaveContentRowAsync(null, req));

    [HttpPut("content-rows/{id}")]
    public async Task<IActionResult> UpdateRow(int id, [FromBody] SaveContentRowRequest req) => Ok(await _admin.SaveContentRowAsync(id, req));

    // ── Navigation ──
    [HttpGet("navigation")]
    public async Task<IActionResult> GetNavigation() => Ok(await _admin.GetNavigationAsync());

    [HttpPut("navigation")]
    public async Task<IActionResult> SaveNavigation([FromBody] SaveNavigationRequest req)
    {
        await _admin.SaveNavigationAsync(req.Items);
        return Ok(new { message = "Navigation updated" });
    }

    // ── App Config ──
    [HttpGet("config")]
    public async Task<IActionResult> GetConfig() => Ok(await _admin.GetConfigsAsync());

    [HttpPut("config/{key}")]
    public async Task<IActionResult> UpdateConfig(string key, [FromBody] UpdateConfigRequest req)
    {
        await _admin.UpdateConfigAsync(key, req.Value);
        return Ok(new { message = "Config updated" });
    }

    // ── Users ──
    [HttpGet("users")]
    public async Task<IActionResult> GetUsers([FromQuery] int page = 1, [FromQuery] string? q = null, [FromQuery] string? role = null)
    {
        return Ok(await _admin.GetUsersAsync(page, q, role));
    }

    [HttpPut("users/{id}/status")]
    public async Task<IActionResult> UpdateUserStatus(int id, [FromBody] UpdateUserStatusRequest req)
    {
        await _admin.UpdateUserStatusAsync(id, req.Status);
        return Ok(new { message = "Status updated" });
    }

    // ── Live Streams ──
    [HttpGet("live")]
    public async Task<IActionResult> GetLiveStreams() => Ok(await _live.GetAllAsync());

    [HttpPost("live")]
    public async Task<IActionResult> CreateLiveStream([FromBody] CreateLiveStreamRequest req)
    {
        var result = await _live.CreateAsync(req);
        return Ok(result);
    }

    [HttpPut("live/{id}")]
    public async Task<IActionResult> UpdateLiveStream(int id, [FromBody] UpdateLiveStreamRequest req)
    {
        return Ok(await _live.UpdateAsync(id, req));
    }

    [HttpPost("live/{id}/start")]
    public async Task<IActionResult> StartStream(int id)
    {
        await _live.StartStreamAsync(id);
        return Ok(new { message = "Stream started" });
    }

    [HttpPost("live/{id}/end")]
    public async Task<IActionResult> EndStream(int id)
    {
        await _live.EndStreamAsync(id);
        return Ok(new { message = "Stream ended and archived" });
    }

    // ── Genres ──
    [HttpGet("genres")]
    public async Task<IActionResult> GetGenres() => Ok(await _content.GetGenresAsync());

    [HttpPost("genres")]
    public async Task<IActionResult> CreateGenre([FromBody] CreateGenreRequest req) => Ok(await _content.CreateGenreAsync(req));

    // ── Subscription Plans ──
    [HttpGet("plans")]
    public async Task<IActionResult> GetAllPlans() => Ok(await _sub.GetAllPlansAsync());

    [HttpPost("plans")]
    public async Task<IActionResult> CreatePlan([FromBody] CreatePlanRequest req) => Ok(await _sub.CreatePlanAsync(req));

    [HttpPut("plans/{id}")]
    public async Task<IActionResult> UpdatePlan(int id, [FromBody] UpdatePlanRequest req)
    {
        await _sub.UpdatePlanAsync(id, req);
        return Ok(new { message = "Plan updated" });
    }

    // ── Promo Codes ──
    [HttpGet("promos")]
    public async Task<IActionResult> GetPromos() => Ok(await _sub.GetPromoCodesAsync());

    [HttpPost("promos")]
    public async Task<IActionResult> CreatePromo([FromBody] CreatePromoRequest req) => Ok(await _sub.CreatePromoCodeAsync(req));

    // ── Analytics Export ──
    [HttpGet("analytics/export")]
    public async Task<IActionResult> ExportAnalytics([FromQuery] string period = "30d", [FromQuery] string format = "json")
    {
        var data = await _analytics.ExportAsync(period, format);
        if (format == "excel")
        {
            Response.Headers.Add("Content-Disposition", "attachment; filename=analytics.xlsx");
            return File(data, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        }
        return Ok(data);
    }

    // ── Creators ──
    [HttpGet("creators")]
    public async Task<IActionResult> GetCreators([FromQuery] int page = 1) => Ok(await _admin.GetCreatorsAsync(page));

    [HttpPut("creators/{id}/approve")]
    public async Task<IActionResult> ApproveCreator(int id) { await _admin.ApproveCreatorAsync(id); return Ok(); }
}
