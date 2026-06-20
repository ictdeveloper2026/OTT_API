using Hangfire;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OTT.Application.DTOs;
using OTT.Infrastructure.Data;
using OTT.Infrastructure.Services;

namespace OTT.API.Controllers;

[ApiController]
[Route("api/livetv")]
public class LiveTvController : ControllerBase
{
    private readonly OttDbContext _db;
    public LiveTvController(OttDbContext db) => _db = db;

    // Filtered, paged list of channels.
    [HttpGet("channels")]
    public async Task<IActionResult> Channels(
        [FromQuery] string? country, [FromQuery] string? language, [FromQuery] string? category,
        [FromQuery] string? q, [FromQuery] int page = 1, [FromQuery] int pageSize = 40)
    {
        var query = _db.IptvChannels.Where(c => !c.IsNsfw);
        if (!string.IsNullOrWhiteSpace(country)) query = query.Where(c => c.Country == country);
        if (!string.IsNullOrWhiteSpace(language)) query = query.Where(c => c.Languages != null && c.Languages.Contains(language));
        if (!string.IsNullOrWhiteSpace(category)) query = query.Where(c => c.Categories != null && c.Categories.Contains(category));
        if (!string.IsNullOrWhiteSpace(q)) query = query.Where(c => c.Name.Contains(q));

        var total = await query.CountAsync();
        var items = await query
            .OrderBy(c => c.Name)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(c => new
            {
                id = c.Id, name = c.Name, country = c.Country, countryName = c.CountryName,
                languages = c.Languages, categories = c.Categories,
                logoUrl = c.LogoUrl, streamUrl = c.StreamUrl, quality = c.Quality
            })
            .ToListAsync();

        return Ok(ApiResponse<object>.Ok(new { items, totalCount = total, page, pageSize }));
    }

    // Available countries + languages (with channel counts) for filter UI.
    [HttpGet("filters")]
    public async Task<IActionResult> Filters()
    {
        var total = await _db.IptvChannels.CountAsync();
        var countries = await _db.IptvChannels.Where(c => c.Country != null)
            .GroupBy(c => new { c.Country, c.CountryName })
            .Select(g => new { code = g.Key.Country, name = g.Key.CountryName, count = g.Count() })
            .OrderByDescending(x => x.count)
            .ToListAsync();

        var langCsvs = await _db.IptvChannels.Where(c => c.Languages != null).Select(c => c.Languages!).ToListAsync();
        var languages = langCsvs
            .SelectMany(s => s.Split(',', StringSplitOptions.RemoveEmptyEntries))
            .GroupBy(s => s)
            .Select(g => new { code = g.Key, count = g.Count() })
            .OrderByDescending(x => x.count)
            .Take(60)
            .ToList();

        return Ok(ApiResponse<object>.Ok(new { total, countries, languages }));
    }

    // Admin: pull the latest data from iptv-org (runs in the background via Hangfire).
    [HttpPost("/api/admin/livetv/sync")]
    [Authorize(Roles = "admin")]
    public IActionResult Sync()
    {
        BackgroundJob.Enqueue<IIptvSyncService>(s => s.SyncAsync(null));
        return Ok(new { message = "Channel sync started. It runs in the background and may take a minute." });
    }
}
