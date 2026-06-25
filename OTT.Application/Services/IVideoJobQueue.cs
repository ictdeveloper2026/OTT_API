namespace OTT.Application.Services;

/// <summary>
/// Abstraction over the background-job system for video work, so the Application layer can enqueue
/// transcoding without depending on Hangfire. Implemented in the host (API/Worker). Transcoding is
/// routed to a dedicated queue that a separate worker process drains, keeping CPU-bursty FFmpeg
/// off the API's request/worker threads.
/// </summary>
public interface IVideoJobQueue
{
    /// <summary>Enqueues a transcoding job. Returns the background job id.</summary>
    string EnqueueTranscoding(Guid assetId, string sourceKey);
}

/// <summary>Hangfire queue names shared by the API and the worker host.</summary>
public static class JobQueues
{
    public const string Default = "default";
    public const string Transcoding = "transcoding";
}
