using Hangfire;

namespace OTT.Application.Services;

/// <summary>
/// Hangfire-backed <see cref="IVideoJobQueue"/>. The job method carries a
/// <c>[Queue("transcoding")]</c> attribute, so it is enqueued onto the transcoding queue that the
/// out-of-process worker host drains. Lives in Application (Hangfire.Core only — no server) so both
/// the API and the Worker can register it.
/// </summary>
public class HangfireVideoJobQueue : IVideoJobQueue
{
    private readonly IBackgroundJobClient _jobs;

    public HangfireVideoJobQueue(IBackgroundJobClient jobs) => _jobs = jobs;

    public string EnqueueTranscoding(Guid assetId, string sourceKey)
        => _jobs.Enqueue<TranscodingJob>(job => job.ProcessAsync(assetId, sourceKey));
}
