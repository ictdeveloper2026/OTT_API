using System.Net;
using Polly;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace OTT.API.Http;

/// <summary>
/// Wraps every outbound HttpClient call (payments, social login, IAP verification, live-stream
/// providers) in a retry + per-attempt timeout + circuit breaker. A slow or flaky third-party
/// gateway can no longer pin request threads or cascade failures into the API.
/// Applied to all <see cref="IHttpClientFactory"/> clients via <c>ConfigureHttpClientDefaults</c>.
/// </summary>
public class ResilienceHandler : DelegatingHandler
{
    // The circuit-breaker state must be shared across requests, so the pipeline is a singleton.
    private static readonly ResiliencePipeline<HttpResponseMessage> Pipeline = BuildPipeline();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // Buffer the body once so each retry attempt can resend the same content.
        if (request.Content is not null)
            await request.Content.LoadIntoBufferAsync();

        return await Pipeline.ExecuteAsync(
            async (req, ct) => await base.SendAsync(await CloneAsync(req), ct),
            request,
            cancellationToken);
    }

    private static ResiliencePipeline<HttpResponseMessage> BuildPipeline()
    {
        // Retry/break on connection errors, timeouts, and retryable status codes (5xx, 408, 429).
        var shouldHandle = new PredicateBuilder<HttpResponseMessage>()
            .Handle<HttpRequestException>()
            .Handle<TimeoutRejectedException>()
            .HandleResult(r => (int)r.StatusCode >= 500
                || r.StatusCode == HttpStatusCode.RequestTimeout
                || r.StatusCode == HttpStatusCode.TooManyRequests);

        return new ResiliencePipelineBuilder<HttpResponseMessage>()
            .AddRetry(new Polly.Retry.RetryStrategyOptions<HttpResponseMessage>
            {
                ShouldHandle = shouldHandle,
                MaxRetryAttempts = 3,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                Delay = TimeSpan.FromMilliseconds(300)
            })
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions<HttpResponseMessage>
            {
                ShouldHandle = shouldHandle,
                FailureRatio = 0.5,
                MinimumThroughput = 10,
                SamplingDuration = TimeSpan.FromSeconds(30),
                BreakDuration = TimeSpan.FromSeconds(15)
            })
            // Per-attempt ceiling — the actual protection against a hung gateway.
            .AddTimeout(TimeSpan.FromSeconds(10))
            .Build();
    }

    // A request message can only be sent once, so clone it per attempt.
    private static async Task<HttpRequestMessage> CloneAsync(HttpRequestMessage request)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri) { Version = request.Version };

        if (request.Content is not null)
        {
            var ms = new MemoryStream();
            await request.Content.CopyToAsync(ms);
            ms.Position = 0;
            clone.Content = new StreamContent(ms);
            foreach (var header in request.Content.Headers)
                clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        foreach (var header in request.Headers)
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        foreach (var option in request.Options)
            ((IDictionary<string, object?>)clone.Options)[option.Key] = option.Value;

        return clone;
    }
}
