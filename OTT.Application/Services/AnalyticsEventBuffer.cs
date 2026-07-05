namespace OTT.Application.Services;

/// <summary>
/// Wire format for a buffered analytics event. Producers serialize one of these onto the Redis
/// <c>analytics:pending</c> list; <c>WriteBehindFlushJob</c> drains and batch-inserts them as
/// <c>AnalyticsEvent</c> rows. Kept small/flat so high-frequency serialization stays cheap.
/// </summary>
public class AnalyticsEventBuffer
{
    public Guid TenantId { get; set; }
    public Guid? ViewerId { get; set; }   // profile id — used for distinct-viewer counting
    public Guid ContentId { get; set; }
    public Guid? EpisodeId { get; set; }
    public string EventType { get; set; } = "content_view";
    public int? WatchDurationSeconds { get; set; }

    // First-party viewer context — populated from request headers (see ClientContext). These
    // feed the studio dashboards: regions, device/platform mix and the time-of-day heatmap.
    public string? Platform { get; set; }     // android | ios | web | tv (X-Platform header)
    public string? DeviceType { get; set; }   // mobile | tablet | tv | web (X-Device-Type header)
    public string? Country { get; set; }      // ISO-3166 alpha-2 (CF-IPCountry / X-Country header)

    /// <summary>
    /// Small JSON blob carrying event-specific detail the flat columns don't cover — e.g. the
    /// playhead position for a pause/seek (<c>{"pos":123}</c>) or the seek target
    /// (<c>{"pos":100,"to":250}</c>). Parsed in-memory by the engagement-hotspot report only.
    /// </summary>
    public string? ExtraData { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Playback event type constants shared by producers (ContentService) and the studio reports.
/// Kept as strings to match the <c>AnalyticsEvent.EventType</c> column and stay query-friendly.
/// </summary>
public static class AnalyticsEventTypes
{
    public const string ContentView = "content_view";   // opened the detail/watch page
    public const string WatchProgress = "watch_progress"; // periodic watched-seconds delta
    public const string Play = "play";
    public const string Pause = "pause";
    public const string Seek = "seek";
    public const string Resume = "resume";
    public const string Complete = "complete";

    // Quality-of-Experience metrics. For these events WatchDurationSeconds carries the metric value
    // in MILLISECONDS (startup time / stall duration) rather than watched seconds — reports always
    // filter by EventType so the unit never crosses over. Errors carry no value (null).
    public const string Startup = "startup";             // time-to-first-frame (ms)
    public const string Rebuffer = "rebuffer";           // mid-playback stall duration (ms)
    public const string PlaybackError = "playback_error"; // fatal player error
}

/// <summary>
/// Per-request viewer context resolved from headers, threaded into services so buffered analytics
/// events can be attributed to a tenant/platform/device/country without the Application layer
/// depending on <c>HttpContext</c>. Every field except <see cref="TenantId"/> is best-effort.
/// </summary>
public readonly record struct ClientContext(Guid TenantId, string? Platform, string? DeviceType, string? Country);
