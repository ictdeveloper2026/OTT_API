using Hangfire;
using Microsoft.Extensions.Logging;

namespace OTT.Application.Services;

/// <summary>
/// CPU-bound FFmpeg transcoding, routed to the dedicated <see cref="JobQueues.Transcoding"/> queue
/// so it runs in the out-of-process worker host rather than on the API. Lives in the Application
/// layer so both the API (which enqueues it) and the Worker (which executes it) share the type.
/// </summary>
public class TranscodingJob
{
    private readonly IVideoService _videoService;
    private readonly ILogger<TranscodingJob> _logger;

    public TranscodingJob(IVideoService videoService, ILogger<TranscodingJob> logger)
    {
        _videoService = videoService;
        _logger = logger;
    }

    [Queue(JobQueues.Transcoding)]
    [AutomaticRetry(Attempts = 2, DelaysInSeconds = new[] { 60, 300 })]
    public async Task ProcessAsync(Guid assetId, string sourceKey)
    {
        _logger.LogInformation("Processing transcoding job for asset {AssetId}", assetId);
        var success = await _videoService.ProcessTranscodingJobAsync(assetId, sourceKey);
        if (!success)
            throw new InvalidOperationException($"Transcoding failed for asset {assetId}");
    }
}
