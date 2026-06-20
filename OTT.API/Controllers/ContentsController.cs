using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OTT.API.Middleware;
using OTT.Application.DTOs;
using OTT.Application.Services;

namespace OTT.API.Controllers;

// Request bodies
public record RateRequestDto(decimal Rating);
public record ProgressRequestDto(int WatchedSeconds, int TotalSeconds, Guid? EpisodeId);
public record WatchlistRequestDto(Guid ContentId);

[ApiController]
[Route("api/contents")]
public class ContentsController : ControllerBase
{
    private readonly IContentService _content;

    public ContentsController(IContentService content) => _content = content;

    [HttpGet("home")]
    public async Task<IActionResult> GetHome()
    {
        var result = await _content.GetHomePageAsync(HttpContext.GetTenantId(), HttpContext.GetProfileId());
        return Ok(ApiResponse<object>.Ok(result));
    }

    [HttpGet("featured")]
    public async Task<IActionResult> GetFeatured()
    {
        var items = await _content.GetFeaturedAsync(HttpContext.GetTenantId());
        return Ok(ApiResponse<object>.Ok(items));
    }

    [HttpGet("trending")]
    public async Task<IActionResult> GetTrending([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var result = await _content.GetTrendingAsync(HttpContext.GetTenantId(), page, pageSize);
        return Ok(ApiResponse<object>.Ok(result));
    }

    [HttpGet("new-releases")]
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
        var content = await _content.GetContentDetailAsync(id, HttpContext.GetTenantId(), HttpContext.GetProfileId());
        return Ok(ApiResponse<object>.Ok(content));
    }

    [HttpGet("{id:guid}/stream")]
    [Authorize]
    public async Task<IActionResult> GetStreamUrl(Guid id, [FromQuery] Guid? episodeId = null)
    {
        var urls = await _content.GetStreamUrlsAsync(id, HttpContext.GetTenantId(), episodeId);
        return Ok(ApiResponse<object>.Ok(urls));
    }

    [HttpGet("{id:guid}/related")]
    public async Task<IActionResult> GetRelated(Guid id)
    {
        var items = await _content.GetRelatedAsync(id, HttpContext.GetTenantId());
        return Ok(ApiResponse<object>.Ok(items));
    }

    [HttpGet("genre/{genreId:guid}")]
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
        await _content.UpdateWatchProgressAsync(HttpContext.RequireProfileId(), contentId, req.WatchedSeconds, req.EpisodeId);
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
