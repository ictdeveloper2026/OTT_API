using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OTT.Domain.Entities;
using OTT.Infrastructure.Data;

namespace OTT.Infrastructure.Services;

public interface IIptvSyncService
{
    /// <summary>Fetches the latest iptv-org data and replaces the IptvChannels table. Returns the count imported.</summary>
    Task<int> SyncAsync(int? limit = null);
}

public class IptvSyncService : IIptvSyncService
{
    private const string Base = "https://iptv-org.github.io/api/";
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<IptvSyncService> _logger;

    public IptvSyncService(IServiceScopeFactory scopeFactory, ILogger<IptvSyncService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task<int> SyncAsync(int? limit = null)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
        http.DefaultRequestHeaders.Add("User-Agent", "OTT-Platform");

        _logger.LogInformation("IPTV sync: downloading iptv-org data…");
        var streams = await GetJson<List<StreamDto>>(http, "streams.json");
        var channels = await GetJson<List<ChannelDto>>(http, "channels.json");
        var feeds = await GetJson<List<FeedDto>>(http, "feeds.json");
        var logos = await GetJson<List<LogoDto>>(http, "logos.json");
        var countries = await GetJson<List<CountryDto>>(http, "countries.json");

        var chById = channels.Where(c => !string.IsNullOrEmpty(c.id))
            .GroupBy(c => c.id!).ToDictionary(g => g.Key, g => g.First());
        var langByChannel = feeds.Where(f => !string.IsNullOrEmpty(f.channel))
            .GroupBy(f => f.channel!)
            .ToDictionary(g => g.Key, g => (g.FirstOrDefault(f => f.is_main) ?? g.First()).languages ?? new());
        var logoByChannel = logos.Where(l => !string.IsNullOrEmpty(l.channel) && !string.IsNullOrEmpty(l.url))
            .GroupBy(l => l.channel!).ToDictionary(g => g.Key, g => g.First().url!);
        var countryName = countries.Where(c => !string.IsNullOrEmpty(c.code))
            .ToDictionary(c => c.code!, c => c.name ?? c.code!);

        var list = new List<IptvChannel>();
        var seen = new HashSet<string>();
        foreach (var s in streams)
        {
            if (string.IsNullOrEmpty(s.url) || string.IsNullOrEmpty(s.channel)) continue;
            if (!chById.TryGetValue(s.channel!, out var ch)) continue;
            if (!seen.Add(s.channel!)) continue; // first stream per channel
            list.Add(new IptvChannel
            {
                ChannelId = s.channel!,
                Name = ch.name ?? s.title ?? s.channel!,
                Country = ch.country,
                CountryName = ch.country != null && countryName.TryGetValue(ch.country, out var cn) ? cn : ch.country,
                Languages = langByChannel.TryGetValue(s.channel!, out var langs) && langs.Count > 0 ? string.Join(",", langs) : null,
                Categories = ch.categories is { Count: > 0 } ? string.Join(",", ch.categories) : null,
                LogoUrl = logoByChannel.TryGetValue(s.channel!, out var lu) ? lu : null,
                StreamUrl = s.url!,
                Quality = s.quality,
                IsNsfw = ch.is_nsfw,
            });
            if (limit.HasValue && list.Count >= limit.Value) break;
        }

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OttDbContext>();
        await db.IptvChannels.ExecuteDeleteAsync();
        db.ChangeTracker.AutoDetectChangesEnabled = false;
        db.IptvChannels.AddRange(list);
        await db.SaveChangesAsync();

        _logger.LogInformation("IPTV sync: imported {Count} channels", list.Count);
        return list.Count;
    }

    private static async Task<T> GetJson<T>(HttpClient http, string file)
    {
        var json = await http.GetStringAsync(Base + file);
        return JsonSerializer.Deserialize<T>(json, JsonOpts) ?? throw new InvalidOperationException($"Empty {file}");
    }

    // iptv-org JSON shapes (only the fields we use)
    private class StreamDto { public string? channel { get; set; } public string? title { get; set; } public string? url { get; set; } public string? quality { get; set; } }
    private class ChannelDto { public string? id { get; set; } public string? name { get; set; } public string? country { get; set; } public List<string>? categories { get; set; } public bool is_nsfw { get; set; } }
    private class FeedDto { public string? channel { get; set; } public List<string>? languages { get; set; } public bool is_main { get; set; } }
    private class LogoDto { public string? channel { get; set; } public string? url { get; set; } }
    private class CountryDto { public string? code { get; set; } public string? name { get; set; } }
}
