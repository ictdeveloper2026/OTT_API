using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using OTT.Application.DTOs.Content;
using OTT.Application.Interfaces;

namespace OTT.API.Controllers;

[ApiController]
[Route("api/contents")]
public class ContentsController : ControllerBase
{
    private readonly IContentService _content;
    private readonly IVideoService _video;
    private readonly IWatchHistoryService _history;
    private readonly IAnalyticsService _analytics;
    private readonly ICacheService _cache;

    public ContentsController(IContentService content, IVideoService video,
        IWatchHistoryService history, IAnalyticsService analytics, ICacheService cache)
    {
        _content = content; _video = video; _history = history;
        _analytics = analytics; _cache = cache;
    }

    [HttpGet("home")]
    [OutputCache(Duration = 120)]
    public async Task<IActionResult> GetHome()
    {
        var profileId = GetProfileId();
        var rows = await _content.GetHomeRowsAsync(profileId);
        return Ok(rows);
    }

    [HttpGet("featured")]
    [OutputCache(Duration = 300)]
    public async Task<IActionResult> GetFeatured()
    {
        var items = await _content.GetFeaturedAsync();
        return Ok(items);
    }

    [HttpGet("trending")]
    [OutputCache(Duration = 600)]
    public async Task<IActionResult> GetTrending([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var result = await _content.GetTrendingAsync(page, pageSize);
        return Ok(result);
    }

    [HttpGet("new-releases")]
    [OutputCache(Duration = 600)]
    public async Task<IActionResult> GetNewReleases([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var result = await _content.GetNewReleasesAsync(page, pageSize);
        return Ok(result);
    }

    [HttpGet("search")]
    public async Task<IActionResult> Search(
        [FromQuery] string q,
        [FromQuery] string? genre,
        [FromQuery] string? language,
        [FromQuery] int? year,
        [FromQuery] string? type,
        [FromQuery] string? sortBy,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var result = await _content.SearchAsync(q, new SearchFilters {
            Genre = genre, Language = language, Year = year, Type = type, SortBy = sortBy
        }, page, pageSize);
        return Ok(result);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetContent(int id)
    {
        var profileId = GetProfileId();
        var content = await _content.GetByIdAsync(id, profileId);
        if (content == null) return NotFound();
        return Ok(content);
    }

    [HttpGet("{id}/stream")]
    [Authorize]
    public async Task<IActionResult> GetStreamUrl(int id, [FromQuery] string quality = "auto")
    {
        var userId = GetUserId();
        var profileId = GetProfileId();
        // Check access (subscription / PPV)
        var hasAccess = await _content.CheckAccessAsync(id, userId);
        if (!hasAccess) return StatusCode(402, new { error = "subscription_required", redirectTo = "/subscribe" });

        var signedUrl = await _video.GetSignedStreamUrlAsync(id, quality, userId);
        if (signedUrl == null) return NotFound(new { error = "stream_not_ready" });

        await _analytics.TrackViewStartAsync(userId, profileId, id);
        return Ok(new { url = signedUrl.Url, playerType = signedUrl.PlayerType, youTubeVideoId = signedUrl.YouTubeVideoId, vimeoVideoId = signedUrl.VimeoVideoId, expiresAt = signedUrl.ExpiresAt });
    }

    [HttpGet("{id}/related")]
    [OutputCache(Duration = 1800)]
    public async Task<IActionResult> GetRelated(int id)
    {
        var items = await _content.GetRelatedAsync(id);
        return Ok(items);
    }

    [HttpPost("{id}/rate")]
    [Authorize]
    public async Task<IActionResult> RateContent(int id, [FromBody] RateRequest req)
    {
        var profileId = GetProfileId();
        await _content.RateContentAsync(id, profileId, req.Rating);
        return Ok(new { message = "Rated" });
    }

    [HttpPost("{id}/review")]
    [Authorize]
    public async Task<IActionResult> ReviewContent(int id, [FromBody] ReviewRequest req)
    {
        var profileId = GetProfileId();
        await _content.ReviewContentAsync(id, profileId, req.StarRating, req.ReviewText);
        return Ok(new { message = "Review submitted for approval" });
    }

    private int? GetUserId() => User.Identity?.IsAuthenticated == true ? int.Parse(User.FindFirst("sub")!.Value) : null;
    private int? GetProfileId() {
        var claim = User.FindFirst("profileId");
        return claim != null ? int.Parse(claim.Value) : null;
    }
}

// ── Series Controller ──
[ApiController]
[Route("api/series")]
public class SeriesController : ControllerBase
{
    private readonly IContentService _content;
    public SeriesController(IContentService content) => _content = content;

    [HttpGet("{id}/episodes")]
    public async Task<IActionResult> GetEpisodes(int id, [FromQuery] int? season)
    {
        var episodes = await _content.GetSeriesEpisodesAsync(id, season);
        return Ok(episodes);
    }
}

// ── Watch History Controller ──
[ApiController]
[Route("api/watch-history")]
[Authorize]
public class WatchHistoryController : ControllerBase
{
    private readonly IWatchHistoryService _history;
    public WatchHistoryController(IWatchHistoryService history) => _history = history;

    [HttpGet]
    public async Task<IActionResult> GetHistory([FromQuery] int page = 1)
    {
        var profileId = int.Parse(User.FindFirst("profileId")!.Value);
        return Ok(await _history.GetHistoryAsync(profileId, page));
    }

    [HttpGet("continue")]
    public async Task<IActionResult> GetContinueWatching()
    {
        var profileId = int.Parse(User.FindFirst("profileId")!.Value);
        return Ok(await _history.GetContinueWatchingAsync(profileId));
    }

    [HttpPost("{contentId}/progress")]
    public async Task<IActionResult> UpdateProgress(int contentId, [FromBody] ProgressRequest req)
    {
        var profileId = int.Parse(User.FindFirst("profileId")!.Value);
        await _history.UpdateProgressAsync(profileId, contentId, req.WatchedSeconds, req.TotalSeconds);
        return Ok();
    }
}

// ── Watchlist Controller ──
[ApiController]
[Route("api/watchlist")]
[Authorize]
public class WatchlistController : ControllerBase
{
    private readonly IContentService _content;
    public WatchlistController(IContentService content) => _content = content;

    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] int page = 1)
    {
        var profileId = int.Parse(User.FindFirst("profileId")!.Value);
        return Ok(await _content.GetWatchlistAsync(profileId, page));
    }

    [HttpPost]
    public async Task<IActionResult> Add([FromBody] WatchlistRequest req)
    {
        var profileId = int.Parse(User.FindFirst("profileId")!.Value);
        await _content.AddToWatchlistAsync(profileId, req.ContentId);
        return Ok(new { message = "Added to watchlist" });
    }

    [HttpDelete("{contentId}")]
    public async Task<IActionResult> Remove(int contentId)
    {
        var profileId = int.Parse(User.FindFirst("profileId")!.Value);
        await _content.RemoveFromWatchlistAsync(profileId, contentId);
        return Ok(new { message = "Removed from watchlist" });
    }
}
