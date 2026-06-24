using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OTT.Application.DTOs;
using OTT.Domain.Entities;
using OTT.Infrastructure.Data;
using OTT.Infrastructure.Services;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace OTT.Application.Services;

public interface ILiveStreamService
{
    Task<LiveStreamDetailDto> CreateStreamAsync(CreateLiveStreamDto dto, Guid tenantId, Guid userId);
    Task<LiveStreamDetailDto> GetStreamAsync(Guid streamId, Guid tenantId);
    Task<PagedResultDto<LiveStreamListDto>> GetStreamsAsync(Guid tenantId, string? status = null, int page = 1, int pageSize = 20);
    Task<bool> StartStreamAsync(Guid streamId, Guid tenantId);
    Task<bool> StopStreamAsync(Guid streamId, Guid tenantId);
    Task<bool> DeleteStreamAsync(Guid streamId);
    Task<StreamKeyDto> RegenerateStreamKeyAsync(Guid streamId);
    Task SendChatMessageAsync(Guid streamId, Guid userId, string message);
    Task UpdateViewerCountAsync(Guid streamId, int delta);
    Task<bool> UpdateStreamAsync(Guid streamId, CreateLiveStreamDto dto);
}

public class LiveStreamService : ILiveStreamService
{
    private readonly OttDbContext _db;
    private readonly IRedisCacheService _cache;
    private readonly IConfiguration _config;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<LiveStreamService> _logger;

    private readonly string _antMediaUrl;
    private readonly string _antMediaApp;

    public LiveStreamService(
        OttDbContext db,
        IRedisCacheService cache,
        IConfiguration config,
        IHttpClientFactory httpFactory,
        ILogger<LiveStreamService> logger)
    {
        _db = db;
        _cache = cache;
        _config = config;
        _httpFactory = httpFactory;
        _logger = logger;
        _antMediaUrl = config["AntMedia:ServerUrl"] ?? "https://live.yourdomain.com:5443";
        _antMediaApp = config["AntMedia:AppName"] ?? "live";
    }

    public async Task<LiveStreamDetailDto> CreateStreamAsync(CreateLiveStreamDto dto, Guid tenantId, Guid userId)
    {
        string streamKey = GenerateStreamKey();
        string? playbackUrl = null;
        string? antMediaId = null;

        if (dto.StreamProvider == "antmedia")
        {
            // Register stream in Ant Media Server
            (antMediaId, playbackUrl) = await CreateAntMediaStreamAsync(streamKey);
        }
        else if (dto.StreamProvider == "youtube" && !string.IsNullOrEmpty(dto.YoutubeStreamId))
        {
            playbackUrl = $"https://www.youtube.com/embed/{dto.YoutubeStreamId}?autoplay=1";
        }
        else if (dto.StreamProvider == "vimeo" && !string.IsNullOrEmpty(dto.VimeoStreamId))
        {
            playbackUrl = $"https://player.vimeo.com/video/{dto.VimeoStreamId}?autoplay=1";
        }

        var stream = new LiveStream
        {
            TenantId = tenantId,
            CreatedByUserId = userId,
            Title = dto.Title,
            Description = dto.Description,
            ThumbnailUrl = dto.ThumbnailUrl,
            StreamProvider = dto.StreamProvider,
            StreamKey = streamKey,
            AntMediaStreamId = antMediaId,
            PlaybackUrl = playbackUrl,
            YoutubeStreamId = dto.YoutubeStreamId,
            VimeoStreamId = dto.VimeoStreamId,
            Category = dto.Category,
            ChatEnabled = dto.ChatEnabled,
            IsScheduled = dto.IsScheduled,
            ScheduledAt = dto.ScheduledAt,
            MonetizationModel = dto.MonetizationModel,
            Status = "offline",
            ViewerCount = 0,
            CreatedAt = DateTime.UtcNow
        };

        _db.LiveStreams.Add(stream);
        await _db.SaveChangesAsync();

        return MapDetailDto(stream, []);
    }

    public async Task<LiveStreamDetailDto> GetStreamAsync(Guid streamId, Guid tenantId)
    {
        var stream = await _db.LiveStreams
            .FirstOrDefaultAsync(s => s.Id == streamId && s.TenantId == tenantId)
            ?? throw new KeyNotFoundException("Stream not found");

        var recentChat = await _db.LiveChatMessages
            .Where(m => m.StreamId == streamId)
            .OrderByDescending(m => m.CreatedAt)
            .Take(50)
            .Join(_db.Users, m => m.UserId, u => u.Id, (m, u) => new LiveChatMessageDto
            {
                Id = m.Id,
                UserName = $"{u.FirstName} {u.LastName}".Trim(),
                AvatarUrl = u.AvatarUrl,
                Message = m.Message,
                CreatedAt = m.CreatedAt
            })
            .OrderBy(m => m.CreatedAt)
            .ToListAsync();

        // Get live viewer count from Redis
        var viewerCountStr = await _cache.GetStringAsync($"live_viewers:{streamId}");
        if (int.TryParse(viewerCountStr, out var liveViewers))
            stream.ViewerCount = liveViewers;

        return MapDetailDto(stream, recentChat);
    }

    public async Task<PagedResultDto<LiveStreamListDto>> GetStreamsAsync(Guid tenantId, string? status = null, int page = 1, int pageSize = 20)
    {
        var query = _db.LiveStreams.Where(s => s.TenantId == tenantId && !s.IsDeleted);

        if (!string.IsNullOrEmpty(status))
            query = query.Where(s => s.Status == status);

        var total = await query.CountAsync();
        var items = await query
            .OrderByDescending(s => s.Status == "live")
            .ThenByDescending(s => s.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return new PagedResultDto<LiveStreamListDto>
        {
            Items = items.Select(MapListDto).ToList(),
            TotalCount = total,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task<bool> StartStreamAsync(Guid streamId, Guid tenantId)
    {
        var stream = await _db.LiveStreams.FirstOrDefaultAsync(s => s.Id == streamId && s.TenantId == tenantId);
        if (stream == null) return false;

        stream.Status = "live";
        stream.StartedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        // Initialize viewer count in Redis
        await _cache.SetStringAsync($"live_viewers:{streamId}", "0", TimeSpan.FromHours(24));

        // Invalidate homepage cache
        await _cache.RemoveByPatternAsync($"homepage:{tenantId}:*");

        _logger.LogInformation("Stream {StreamId} started", streamId);
        return true;
    }

    public async Task<bool> StopStreamAsync(Guid streamId, Guid tenantId)
    {
        var stream = await _db.LiveStreams.FirstOrDefaultAsync(s => s.Id == streamId && s.TenantId == tenantId);
        if (stream == null) return false;

        stream.Status = "ended";
        stream.EndedAt = DateTime.UtcNow;

        // Get final viewer count
        var viewerCountStr = await _cache.GetStringAsync($"live_viewers:{streamId}");
        if (int.TryParse(viewerCountStr, out var finalViewers))
            stream.PeakViewerCount = Math.Max(stream.PeakViewerCount ?? 0, finalViewers);

        await _db.SaveChangesAsync();
        await _cache.RemoveAsync($"live_viewers:{streamId}");
        await _cache.RemoveByPatternAsync($"homepage:{tenantId}:*");

        // Stop in Ant Media if applicable
        if (stream.StreamProvider == "antmedia" && !string.IsNullOrEmpty(stream.AntMediaStreamId))
            await StopAntMediaStreamAsync(stream.AntMediaStreamId);

        return true;
    }

    public async Task<bool> DeleteStreamAsync(Guid streamId)
    {
        var stream = await _db.LiveStreams.FindAsync(streamId);
        if (stream == null) return false;
        stream.IsDeleted = true;
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<StreamKeyDto> RegenerateStreamKeyAsync(Guid streamId)
    {
        var stream = await _db.LiveStreams.FindAsync(streamId)
            ?? throw new KeyNotFoundException("Stream not found");

        stream.StreamKey = GenerateStreamKey();
        await _db.SaveChangesAsync();

        return new StreamKeyDto
        {
            StreamKey = stream.StreamKey,
            RtmpUrl = $"rtmp://{_config["AntMedia:RtmpHost"] ?? "live.yourdomain.com"}/live",
            StreamUrl = $"{_antMediaUrl}/{_antMediaApp}/{stream.StreamKey}.m3u8"
        };
    }

    public async Task SendChatMessageAsync(Guid streamId, Guid userId, string message)
    {
        var msg = new LiveChatMessage
        {
            StreamId = streamId,
            UserId = userId,
            Message = message.Length > 500 ? message[..500] : message,
            CreatedAt = DateTime.UtcNow
        };
        _db.LiveChatMessages.Add(msg);
        await _db.SaveChangesAsync();
    }

    public async Task UpdateViewerCountAsync(Guid streamId, int delta)
    {
        var key = $"live_viewers:{streamId}";
        if (delta > 0)
            await _cache.IncrementAsync(key, TimeSpan.FromHours(24));
        else
        {
            var current = int.Parse(await _cache.GetStringAsync(key) ?? "0");
            var newCount = Math.Max(0, current + delta);
            await _cache.SetStringAsync(key, newCount.ToString(), TimeSpan.FromHours(24));
        }

        // Update peak if needed
        var currentCount = int.Parse(await _cache.GetStringAsync(key) ?? "0");
        var stream = await _db.LiveStreams.FindAsync(streamId);
        if (stream != null && currentCount > (stream.PeakViewerCount ?? 0))
        {
            stream.PeakViewerCount = currentCount;
            stream.ViewerCount = currentCount;
            await _db.SaveChangesAsync();
        }
    }

    public async Task<bool> UpdateStreamAsync(Guid streamId, CreateLiveStreamDto dto)
    {
        var stream = await _db.LiveStreams.FindAsync(streamId);
        if (stream == null) return false;

        stream.Title = dto.Title;
        stream.Description = dto.Description ?? stream.Description;
        stream.ThumbnailUrl = dto.ThumbnailUrl ?? stream.ThumbnailUrl;
        stream.Category = dto.Category ?? stream.Category;
        stream.ChatEnabled = dto.ChatEnabled;
        stream.IsScheduled = dto.IsScheduled;
        stream.ScheduledAt = dto.ScheduledAt ?? stream.ScheduledAt;
        stream.MonetizationModel = dto.MonetizationModel;

        await _db.SaveChangesAsync();
        return true;
    }

    // ── Private Helpers ───────────────────────────────────────────────────────

    private async Task<(string id, string playbackUrl)> CreateAntMediaStreamAsync(string streamKey)
    {
        try
        {
            var client = _httpFactory.CreateClient();
            var body = JsonSerializer.Serialize(new
            {
                streamId = streamKey,
                name = streamKey,
                type = "liveStream"
            });
            var content = new StringContent(body, Encoding.UTF8, "application/json");
            var response = await client.PostAsync(
                $"{_antMediaUrl}/{_antMediaApp}/rest/v2/broadcasts/create", content);

            if (response.IsSuccessStatusCode)
            {
                var json = JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync());
                var id = json.GetProperty("streamId").GetString() ?? streamKey;
                var playbackUrl = $"{_antMediaUrl}/{_antMediaApp}/{id}.m3u8";
                return (id, playbackUrl);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to create Ant Media stream, using local stream key");
        }

        // Fallback: use stream key directly
        var rtmpBase = _config["AntMedia:RtmpHost"] ?? "live.yourdomain.com";
        return (streamKey, $"https://{rtmpBase}/{_antMediaApp}/{streamKey}.m3u8");
    }

    private async Task StopAntMediaStreamAsync(string streamId)
    {
        try
        {
            var client = _httpFactory.CreateClient();
            await client.DeleteAsync($"{_antMediaUrl}/{_antMediaApp}/rest/v2/broadcasts/{streamId}");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to stop Ant Media stream {StreamId}", streamId);
        }
    }

    private static string GenerateStreamKey()
    {
        var bytes = new byte[16];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        return Convert.ToHexString(bytes).ToLower();
    }

    private static LiveStreamListDto MapListDto(LiveStream s) => new()
    {
        Id = s.Id,
        Title = s.Title,
        Description = s.Description,
        ThumbnailUrl = s.ThumbnailUrl,
        Status = s.Status,
        ViewerCount = s.ViewerCount,
        StartedAt = s.StartedAt,
        Category = s.Category,
        IsScheduled = s.IsScheduled,
        ScheduledAt = s.ScheduledAt
    };

    private LiveStreamDetailDto MapDetailDto(LiveStream s, List<LiveChatMessageDto> chat)
    {
        var rtmpHost = _config["AntMedia:RtmpHost"] ?? "live.yourdomain.com";
        return new LiveStreamDetailDto
        {
            Id = s.Id,
            Title = s.Title,
            Description = s.Description,
            ThumbnailUrl = s.ThumbnailUrl,
            Status = s.Status,
            ViewerCount = s.ViewerCount,
            StartedAt = s.StartedAt,
            Category = s.Category,
            IsScheduled = s.IsScheduled,
            ScheduledAt = s.ScheduledAt,
            PlaybackUrl = s.PlaybackUrl,
            YoutubeStreamId = s.YoutubeStreamId,
            VimeoStreamId = s.VimeoStreamId,
            StreamProvider = s.StreamProvider,
            ChatEnabled = s.ChatEnabled,
            RecentChat = chat
        };
    }
}

public class StreamKeyDto
{
    public string StreamKey { get; set; } = "";
    public string RtmpUrl { get; set; } = "";
    public string StreamUrl { get; set; } = "";
}
