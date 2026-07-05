using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OTT.Application.DTOs;
using OTT.Domain.Entities;
using OTT.Infrastructure.Data;
using OTT.Infrastructure.Services;
using System.Diagnostics;
using System.Text.Json;

namespace OTT.Application.Services;

public interface IVideoService
{
    Task<UploadUrlResponseDto> GetUploadUrlAsync(UploadRequestDto request, Guid tenantId);
    Task<string> StartTranscodingAsync(TranscodeRequestDto request);
    Task<VideoAsset?> GetAssetStatusAsync(Guid assetId);
    Task<bool> ProcessTranscodingJobAsync(Guid assetId, string sourceKey);
    Task<string?> ExtractYouTubeInfoAsync(string urlOrId);
    Task<string?> ExtractVimeoInfoAsync(string urlOrId);
    Task<bool> GenerateThumbnailsAsync(Guid contentId, string videoKey, int count = 5);
}

public class VideoService : IVideoService
{
    private readonly OttDbContext _db;
    private readonly IStorageService _storage;
    private readonly ICloudFrontCdnService _cdn;
    private readonly IConfiguration _config;
    private readonly IHttpClientFactory _httpFactory;
    private readonly IVideoJobQueue _jobQueue;
    private readonly ILogger<VideoService> _logger;

    private static readonly string[] Qualities = ["1080p", "720p", "480p", "360p"];
    private static readonly Dictionary<string, (int Width, int Height, int Bitrate)> QualityMap = new()
    {
        { "1080p", (1920, 1080, 5000) },
        { "720p",  (1280, 720,  2500) },
        { "480p",  (854,  480,  1000) },
        { "360p",  (640,  360,  600)  }
    };

    public VideoService(
        OttDbContext db,
        IStorageService storage,
        ICloudFrontCdnService cdn,
        IConfiguration config,
        IHttpClientFactory httpFactory,
        IVideoJobQueue jobQueue,
        ILogger<VideoService> logger)
    {
        _db = db;
        _storage = storage;
        _cdn = cdn;
        _config = config;
        _httpFactory = httpFactory;
        _jobQueue = jobQueue;
        _logger = logger;
    }

    public async Task<UploadUrlResponseDto> GetUploadUrlAsync(UploadRequestDto request, Guid tenantId)
    {
        var extension = Path.GetExtension(request.FileName).ToLower();
        var assetId = Guid.NewGuid();
        string key;

        switch (request.UploadType)
        {
            case "video":
                key = $"uploads/{tenantId}/videos/{assetId}{extension}";

                // Create pending VideoAsset record
                var asset = new VideoAsset
                {
                    Id = assetId,
                    ContentId = request.ContentId,
                    EpisodeId = request.EpisodeId,
                    OriginalFileName = request.FileName,
                    OriginalKey = key,
                    StorageProvider = "s3",
                    Status = "uploading",
                    CreatedAt = DateTime.UtcNow
                };
                _db.VideoAssets.Add(asset);
                await _db.SaveChangesAsync();
                break;

            case "thumbnail":
                key = $"images/{tenantId}/thumbnails/{Guid.NewGuid()}{extension}";
                assetId = Guid.Empty;
                break;

            case "poster":
                key = $"images/{tenantId}/posters/{Guid.NewGuid()}{extension}";
                assetId = Guid.Empty;
                break;

            case "banner":
                key = $"images/{tenantId}/banners/{Guid.NewGuid()}{extension}";
                assetId = Guid.Empty;
                break;

            case "logo":
                key = $"images/{tenantId}/branding/{Guid.NewGuid()}{extension}";
                assetId = Guid.Empty;
                break;

            case "subtitle":
                key = $"subtitles/{tenantId}/{request.ContentId ?? Guid.NewGuid()}/{Guid.NewGuid()}{extension}";
                assetId = Guid.Empty;
                break;

            default:
                key = $"misc/{tenantId}/{Guid.NewGuid()}{extension}";
                assetId = Guid.Empty;
                break;
        }

        var presigned = await _storage.GetPresignedUploadUrlAsync(key, request.ContentType, TimeSpan.FromHours(1));

        return new UploadUrlResponseDto
        {
            UploadUrl = presigned.Url,
            FileKey = key,
            AssetId = assetId == Guid.Empty ? null : assetId.ToString(),
            Headers = presigned.Headers
        };
    }

    public async Task<string> StartTranscodingAsync(TranscodeRequestDto request)
    {
        var asset = await _db.VideoAssets.FindAsync(request.AssetId)
            ?? throw new KeyNotFoundException("VideoAsset not found");

        asset.Status = "transcoding";
        asset.TranscodingStartedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        // Enqueue onto the dedicated transcoding queue, drained out-of-process by the worker host.
        var sourceKey = !string.IsNullOrWhiteSpace(request.SourceKey) ? request.SourceKey : asset.OriginalKey;
        if (string.IsNullOrWhiteSpace(sourceKey))
            throw new InvalidOperationException("No source key to transcode (asset has no OriginalKey).");

        var jobId = _jobQueue.EnqueueTranscoding(request.AssetId, sourceKey);
        _logger.LogInformation("Transcoding job {JobId} enqueued for asset {AssetId}", jobId, request.AssetId);
        return jobId;
    }

    public async Task<VideoAsset?> GetAssetStatusAsync(Guid assetId)
    {
        return await _db.VideoAssets
            .Include(a => a.Subtitles)
            .FirstOrDefaultAsync(a => a.Id == assetId);
    }

    public async Task<bool> ProcessTranscodingJobAsync(Guid assetId, string sourceKey)
    {
        var asset = await _db.VideoAssets.FindAsync(assetId);
        if (asset == null) return false;

        var tempDir = Path.Combine(Path.GetTempPath(), "ott_transcode", assetId.ToString());
        Directory.CreateDirectory(tempDir);

        try
        {
            _logger.LogInformation("Starting FFmpeg transcoding for asset {AssetId}", assetId);

            // Download source from the configured storage backend (S3, S3-compatible, or local disk)
            var sourceStream = await _storage.DownloadAsync(sourceKey);
            var localSource = Path.Combine(tempDir, "source" + Path.GetExtension(sourceKey));
            await using (var fs = File.Create(localSource))
                await sourceStream.CopyToAsync(fs);

            // Get video info
            var (duration, width, height) = await GetVideoInfoAsync(localSource);
            asset.DurationSeconds = (int)duration;
            asset.OriginalWidth = width;
            asset.OriginalHeight = height;

            // Transcode each quality
            var contentId = asset.ContentId ?? assetId;
            var outputPaths = new Dictionary<string, string>();

            foreach (var quality in Qualities)
            {
                var (qWidth, qHeight, bitrate) = QualityMap[quality];
                if (width > 0 && width < qWidth) continue; // Skip qualities higher than source

                var qualityDir = Path.Combine(tempDir, quality);
                Directory.CreateDirectory(qualityDir);
                var playlistPath = Path.Combine(qualityDir, "playlist.m3u8");

                var success = await RunFfmpegAsync(localSource, qualityDir, qWidth, qHeight, bitrate);
                if (success)
                {
                    // Upload HLS segments (public — these are streamed directly by the player)
                    var keyPrefix = $"transcoded/{contentId}/{quality}/";
                    foreach (var file in Directory.GetFiles(qualityDir))
                    {
                        var fileName = Path.GetFileName(file);
                        var outKey = keyPrefix + fileName;
                        var contentType = fileName.EndsWith(".m3u8") ? "application/x-mpegURL" : "video/MP2T";
                        await using var fileStream = File.OpenRead(file);
                        await _storage.UploadPublicAsync(outKey, fileStream, contentType);
                    }
                    outputPaths[quality] = $"transcoded/{contentId}/{quality}/playlist.m3u8";
                    _logger.LogInformation("Transcoded {Quality} for asset {AssetId}", quality, assetId);
                }
            }

            // Generate master playlist
            var masterPlaylist = GenerateMasterPlaylist(outputPaths, contentId.ToString());
            var masterKey = $"transcoded/{contentId}/master.m3u8";
            await using (var ms = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(masterPlaylist)))
                await _storage.UploadPublicAsync(masterKey, ms, "application/x-mpegURL");

            // Generate thumbnails
            await GenerateThumbnailsFromLocalAsync(localSource, contentId, tempDir, 5);

            asset.Status = "ready";
            asset.HlsPath = masterKey;
            asset.TranscodedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            _logger.LogInformation("Transcoding complete for asset {AssetId}", assetId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Transcoding failed for asset {AssetId}", assetId);
            asset.Status = "failed";
            asset.ErrorMessage = ex.Message;
            await _db.SaveChangesAsync();
            return false;
        }
        finally
        {
            // Cleanup temp files
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    public async Task<string?> ExtractYouTubeInfoAsync(string urlOrId)
    {
        // Extract YouTube ID from URL or return as-is
        var id = urlOrId;
        if (urlOrId.Contains("youtube.com") || urlOrId.Contains("youtu.be"))
        {
            var uri = new Uri(urlOrId);
            if (urlOrId.Contains("youtu.be"))
                id = uri.AbsolutePath.TrimStart('/');
            else
            {
                var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
                id = query["v"] ?? "";
            }
        }

        if (string.IsNullOrEmpty(id)) return null;

        // Validate with YouTube oEmbed
        try
        {
            var client = _httpFactory.CreateClient();
            var response = await client.GetAsync($"https://www.youtube.com/oembed?url=https://www.youtube.com/watch?v={id}&format=json");
            if (response.IsSuccessStatusCode) return id;
        }
        catch { }

        return null;
    }

    public async Task<string?> ExtractVimeoInfoAsync(string urlOrId)
    {
        var id = urlOrId;
        if (urlOrId.Contains("vimeo.com"))
        {
            var uri = new Uri(urlOrId);
            id = uri.AbsolutePath.TrimStart('/').Split('/')[0];
        }

        if (string.IsNullOrEmpty(id) || !long.TryParse(id, out _)) return null;

        try
        {
            var client = _httpFactory.CreateClient();
            var response = await client.GetAsync($"https://vimeo.com/api/oembed.json?url=https://vimeo.com/{id}");
            if (response.IsSuccessStatusCode) return id;
        }
        catch { }

        return null;
    }

    public async Task<bool> GenerateThumbnailsAsync(Guid contentId, string videoKey, int count = 5)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "thumbnails", contentId.ToString());
        Directory.CreateDirectory(tempDir);

        try
        {
            var sourceStream = await _storage.DownloadAsync(videoKey);
            var localSource = Path.Combine(tempDir, "source.mp4");
            await using (var fs = File.Create(localSource))
                await sourceStream.CopyToAsync(fs);

            return await GenerateThumbnailsFromLocalAsync(localSource, contentId, tempDir, count);
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    // ── Private FFmpeg Helpers ────────────────────────────────────────────────

    private async Task<bool> RunFfmpegAsync(string input, string outputDir, int width, int height, int bitrate)
    {
        var ffmpegPath = _config["FFmpeg:Path"] ?? "ffmpeg";
        var args = $"-i \"{input}\" -vf scale={width}:{height} -c:v libx264 -preset fast -b:v {bitrate}k " +
                   $"-c:a aac -b:a 128k -hls_time 6 -hls_playlist_type vod " +
                   $"-hls_segment_filename \"{outputDir}/segment_%03d.ts\" \"{outputDir}/playlist.m3u8\"";

        return await RunProcessAsync(ffmpegPath, args);
    }

    private async Task<bool> GenerateThumbnailsFromLocalAsync(string source, Guid contentId, string tempDir, int count)
    {
        var ffmpegPath = _config["FFmpeg:Path"] ?? "ffmpeg";
        var (duration, _, _) = await GetVideoInfoAsync(source);
        var interval = duration / (count + 1);

        for (int i = 1; i <= count; i++)
        {
            var timestamp = interval * i;
            var thumbPath = Path.Combine(tempDir, $"thumb_{i:D2}.jpg");
            var args = $"-i \"{source}\" -ss {timestamp:F0} -vframes 1 -vf scale=1280:720 -q:v 2 \"{thumbPath}\"";

            if (await RunProcessAsync(ffmpegPath, args))
            {
                var thumbKey = $"thumbnails/{contentId}/thumb_{i:D2}.jpg";
                await using var fs = File.OpenRead(thumbPath);
                await _storage.UploadPublicAsync(thumbKey, fs, "image/jpeg");
            }
        }

        return true;
    }

    private async Task<(double duration, int width, int height)> GetVideoInfoAsync(string filePath)
    {
        var ffprobePath = _config["FFmpeg:ProbePath"] ?? "ffprobe";
        var args = $"-v quiet -print_format json -show_streams -show_format \"{filePath}\"";

        var output = await RunProcessOutputAsync(ffprobePath, args);
        if (string.IsNullOrEmpty(output)) return (0, 0, 0);

        try
        {
            var json = JsonSerializer.Deserialize<JsonElement>(output);
            var format = json.GetProperty("format");
            var duration = double.Parse(format.GetProperty("duration").GetString() ?? "0");

            var streams = json.GetProperty("streams");
            int width = 0, height = 0;
            for (int i = 0; i < streams.GetArrayLength(); i++)
            {
                var stream = streams[i];
                if (stream.TryGetProperty("codec_type", out var type) && type.GetString() == "video")
                {
                    width = stream.TryGetProperty("width", out var w) ? w.GetInt32() : 0;
                    height = stream.TryGetProperty("height", out var h) ? h.GetInt32() : 0;
                    break;
                }
            }
            return (duration, width, height);
        }
        catch
        {
            return (0, 0, 0);
        }
    }

    private static async Task<bool> RunProcessAsync(string executable, string args)
    {
        var psi = new ProcessStartInfo(executable, args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = psi };
        process.Start();
        await process.WaitForExitAsync();
        return process.ExitCode == 0;
    }

    private static async Task<string> RunProcessOutputAsync(string executable, string args)
    {
        var psi = new ProcessStartInfo(executable, args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = psi };
        process.Start();
        var output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        return output;
    }

    private static string GenerateMasterPlaylist(Dictionary<string, string> outputs, string contentId)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("#EXTM3U");
        sb.AppendLine("#EXT-X-VERSION:3");

        foreach (var (quality, path) in outputs.OrderByDescending(o => o.Key))
        {
            var (width, height, bitrate) = QualityMap[quality];
            sb.AppendLine($"#EXT-X-STREAM-INF:BANDWIDTH={bitrate * 1000},RESOLUTION={width}x{height},NAME=\"{quality}\"");
            sb.AppendLine(path);
        }

        return sb.ToString();
    }
}
