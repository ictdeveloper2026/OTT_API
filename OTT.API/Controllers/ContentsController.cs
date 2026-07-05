using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using OTT.API.Middleware;
using OTT.Application.DTOs;
using OTT.Application.Services;

namespace OTT.API.Controllers;

// Request bodies
public record RateRequestDto([System.ComponentModel.DataAnnotations.Range(0, 10)] decimal Rating);
public record ProgressRequestDto(
    [System.ComponentModel.DataAnnotations.Range(0, int.MaxValue)] int WatchedSeconds,
    [System.ComponentModel.DataAnnotations.Range(0, int.MaxValue)] int TotalSeconds,
    Guid? EpisodeId);
public record WatchlistRequestDto(Guid ContentId);
public record StreamSessionRequestDto([System.ComponentModel.DataAnnotations.Required] Guid StreamSessionId);
public record PlaybackEventRequestDto(
    [System.ComponentModel.DataAnnotations.Required,
     System.ComponentModel.DataAnnotations.RegularExpression("^(play|pause|seek|resume|complete)$",
         ErrorMessage = "Type must be one of: play, pause, seek, resume, complete")] string Type,
    [System.ComponentModel.DataAnnotations.Range(0, int.MaxValue)] int PositionSeconds,
    [System.ComponentModel.DataAnnotations.Range(0, int.MaxValue)] int? SeekToSeconds,
    Guid? EpisodeId);
public record QoeEventRequestDto(
    [System.ComponentModel.DataAnnotations.Required,
     System.ComponentModel.DataAnnotations.RegularExpression("^(startup|rebuffer|playback_error)$",
         ErrorMessage = "Type must be one of: startup, rebuffer, playback_error")] string Type,
    [System.ComponentModel.DataAnnotations.Range(0, int.MaxValue)] int ValueMs,
    [System.ComponentModel.DataAnnotations.Range(0, int.MaxValue)] int PositionSeconds,
    Guid? EpisodeId);

[ApiController]
[Route("api/contents")]
public class ContentsController : ControllerBase
{
    private readonly IContentService _content;
    private readonly IEntitlementService _entitlements;
    private readonly IStreamSessionService _streamSessions;

    public ContentsController(IContentService content, IEntitlementService entitlements, IStreamSessionService streamSessions)
    {
        _content = content;
        _entitlements = entitlements;
        _streamSessions = streamSessions;
    }

    [HttpGet("home")]
    public async Task<IActionResult> GetHome()
    {
        var result = await _content.GetHomePageAsync(HttpContext.GetTenantId(), HttpContext.GetProfileId());
        return Ok(ApiResponse<object>.Ok(result));
    }

    [HttpGet("featured")]
    [OutputCache(PolicyName = "catalog")]
    public async Task<IActionResult> GetFeatured()
    {
        var items = await _content.GetFeaturedAsync(HttpContext.GetTenantId());
        return Ok(ApiResponse<object>.Ok(items));
    }

    [HttpGet("trending")]
    [OutputCache(PolicyName = "catalog")]
    public async Task<IActionResult> GetTrending([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var result = await _content.GetTrendingAsync(HttpContext.GetTenantId(), page, pageSize);
        return Ok(ApiResponse<object>.Ok(result));
    }

    [HttpGet("recommendations")]
    public async Task<IActionResult> GetRecommendations()
    {
        var items = await _content.GetRecommendationsAsync(HttpContext.RequireProfileId(), HttpContext.GetTenantId());
        return Ok(ApiResponse<object>.Ok(items));
    }

    [HttpGet("new-releases")]
    [OutputCache(PolicyName = "catalog")]
    public async Task<IActionResult> GetNewReleases([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var result = await _content.GetNewReleasesAsync(HttpContext.GetTenantId(), page, pageSize);
        return Ok(ApiResponse<object>.Ok(result));
    }

    [HttpGet("search")]
    public async Task<IActionResult> Search(
        [FromQuery] string q,
        [FromQuery] string? type,
        [FromQuery] string? language,
        [FromQuery] int? year,
        [FromQuery] string? sortBy,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var request = new SearchRequestDto
        {
            Query = q ?? "",
            Type = type,
            Language = language,
            ReleaseYear = year,
            SortBy = sortBy ?? "relevance",
            Page = page,
            PageSize = pageSize
        };
        var result = await _content.SearchAsync(request, HttpContext.GetTenantId());
        return Ok(ApiResponse<object>.Ok(result));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetContent(Guid id)
    {
        var content = await _content.GetContentDetailAsync(id, HttpContext.GetTenantId(), HttpContext.GetProfileId(), HttpContext.GetClientContext());
        return Ok(ApiResponse<object>.Ok(content));
    }

    [HttpGet("{id:guid}/stream")]
    [Authorize]
    public async Task<IActionResult> GetStreamUrl(Guid id, [FromQuery] Guid? episodeId = null)
    {
        var tenantId = HttpContext.GetTenantId();
        var userId = HttpContext.RequireUserId();

        // Paywall: never issue signed stream URLs to a user who isn't entitled to this title.
        if (!await _entitlements.CanWatchAsync(userId, tenantId, id))
            return StatusCode(StatusCodes.Status402PaymentRequired,
                ApiResponse<object>.Fail("A subscription or purchase is required to watch this title."));

        // Per-plan concurrent-stream limit: reserve a slot before handing out playable URLs.
        var slot = await _streamSessions.StartAsync(userId);
        if (!slot.Allowed)
            return StatusCode(StatusCodes.Status409Conflict,
                ApiResponse<object>.Fail($"Your plan allows {slot.MaxStreams} concurrent stream(s). Stop playback on another device to continue."));

        var urls = await _content.GetStreamUrlsAsync(id, tenantId, episodeId);
        // streamSessionId is additive: clients heartbeat it (POST /api/streams/heartbeat) and stop it on exit.
        return Ok(ApiResponse<object>.Ok(new { stream = urls, streamSessionId = slot.SessionId, slot.ActiveStreams, slot.MaxStreams }));
    }

    /// <summary>Keeps a concurrent-stream slot alive. Send every progress sync (~15s).</summary>
    [HttpPost("/api/streams/heartbeat")]
    [Authorize]
    public async Task<IActionResult> StreamHeartbeat([FromBody] StreamSessionRequestDto req)
    {
        await _streamSessions.HeartbeatAsync(HttpContext.RequireUserId(), req.StreamSessionId);
        return Ok();
    }

    /// <summary>Releases a concurrent-stream slot when playback stops.</summary>
    [HttpPost("/api/streams/stop")]
    [Authorize]
    public async Task<IActionResult> StreamStop([FromBody] StreamSessionRequestDto req)
    {
        await _streamSessions.StopAsync(HttpContext.RequireUserId(), req.StreamSessionId);
        return Ok();
    }

    [HttpGet("{id:guid}/related")]
    public async Task<IActionResult> GetRelated(Guid id)
    {
        var items = await _content.GetRelatedAsync(id, HttpContext.GetTenantId());
        return Ok(ApiResponse<object>.Ok(items));
    }

    [HttpGet("genre/{genreId:guid}")]
    [OutputCache(PolicyName = "catalog")]
    public async Task<IActionResult> GetByGenre(Guid genreId, [FromQuery] int page = 1, [FromQuery] int pageSize = 30)
    {
        var result = await _content.GetByGenreAsync(genreId, HttpContext.GetTenantId(), page, pageSize);
        return Ok(ApiResponse<object>.Ok(result));
    }

    [HttpPost("{id:guid}/rate")]
    [Authorize]
    public async Task<IActionResult> RateContent(Guid id, [FromBody] RateRequestDto req)
    {
        await _content.RateContentAsync(HttpContext.RequireProfileId(), id, req.Rating);
        return Ok(new { message = "Rated" });
    }

    /// <summary>
    /// Records a granular playback event (play/pause/seek/resume/complete) for first-party analytics.
    /// Fire-and-forget from the player; buffered in Redis and flushed to AnalyticsEvents in batches.
    /// </summary>
    [HttpPost("{id:guid}/events")]
    [Authorize]
    public async Task<IActionResult> RecordEvent(Guid id, [FromBody] PlaybackEventRequestDto req)
    {
        await _content.RecordPlaybackEventAsync(
            HttpContext.RequireProfileId(), id, req.Type, req.PositionSeconds, req.SeekToSeconds, req.EpisodeId,
            HttpContext.GetClientContext());
        return Ok();
    }

    /// <summary>
    /// Records a Quality-of-Experience metric (startup time / rebuffer stall / fatal error) for the
    /// player. Fire-and-forget; feeds the studio QoE dashboard (the strongest churn predictor).
    /// </summary>
    [HttpPost("{id:guid}/qoe")]
    [Authorize]
    public async Task<IActionResult> RecordQoe(Guid id, [FromBody] QoeEventRequestDto req)
    {
        await _content.RecordQoeEventAsync(
            HttpContext.RequireProfileId(), id, req.Type, req.ValueMs, req.PositionSeconds, req.EpisodeId,
            HttpContext.GetClientContext());
        return Ok();
    }
}

// ── Series ──
[ApiController]
[Route("api/series")]
public class SeriesController : ControllerBase
{
    private readonly IContentService _content;
    public SeriesController(IContentService content) => _content = content;

    [HttpGet("{id:guid}/episodes")]
    public async Task<IActionResult> GetEpisodes(Guid id, [FromQuery] int? season)
    {
        var seasons = await _content.GetSeriesEpisodesAsync(id, HttpContext.GetTenantId(), season);
        return Ok(ApiResponse<object>.Ok(seasons));
    }
}

// ── Watch History ──
[ApiController]
[Route("api/watch-history")]
[Authorize]
public class WatchHistoryController : ControllerBase
{
    private readonly IContentService _content;
    public WatchHistoryController(IContentService content) => _content = content;

    [HttpGet]
    public async Task<IActionResult> GetHistory()
    {
        var items = await _content.GetWatchHistoryAsync(HttpContext.RequireProfileId());
        return Ok(ApiResponse<object>.Ok(items));
    }

    [HttpGet("continue")]
    public async Task<IActionResult> GetContinueWatching()
    {
        var items = await _content.GetContinueWatchingAsync(HttpContext.RequireProfileId());
        return Ok(ApiResponse<object>.Ok(items));
    }

    [HttpPost("{contentId:guid}/progress")]
    public async Task<IActionResult> UpdateProgress(Guid contentId, [FromBody] ProgressRequestDto req)
    {
        await _content.UpdateWatchProgressAsync(HttpContext.RequireProfileId(), contentId, req.WatchedSeconds, req.TotalSeconds, req.EpisodeId, HttpContext.GetClientContext());
        return Ok();
    }
}

// ── Watchlist ──
[ApiController]
[Route("api/watchlist")]
[Authorize]
public class WatchlistController : ControllerBase
{
    private readonly IContentService _content;
    public WatchlistController(IContentService content) => _content = content;

    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var items = await _content.GetWatchlistAsync(HttpContext.RequireProfileId(), HttpContext.GetTenantId());
        return Ok(ApiResponse<object>.Ok(items));
    }

    [HttpPost]
    public async Task<IActionResult> Add([FromBody] WatchlistRequestDto req)
    {
        await _content.AddToWatchlistAsync(HttpContext.RequireProfileId(), req.ContentId);
        return Ok(new { message = "Added to watchlist" });
    }

    [HttpDelete("{contentId:guid}")]
    public async Task<IActionResult> Remove(Guid contentId)
    {
        await _content.RemoveFromWatchlistAsync(HttpContext.RequireProfileId(), contentId);
        return Ok(new { message = "Removed from watchlist" });
    }
}
