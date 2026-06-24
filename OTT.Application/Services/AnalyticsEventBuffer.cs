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
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
