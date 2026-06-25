using Hangfire;
using Hangfire.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OTT.Application.DTOs;
using OTT.Infrastructure.Data;

namespace OTT.API.Controllers;

/// <summary>
/// Operational visibility for the async machinery: the email/push outbox (incl. dead-letters)
/// and the Hangfire job server. Lets an admin see and recover from stuck work without a console.
/// </summary>
[ApiController]
[Route("api/admin/ops")]
[Authorize(Roles = "admin")]
public class OpsController : ControllerBase
{
    private readonly OttDbContext _db;

    public OpsController(OttDbContext db) => _db = db;

    // Outbox health: counts by status + a sample of the most recent rows for a given status.
    [HttpGet("outbox")]
    public async Task<IActionResult> Outbox([FromQuery] string status = "failed", [FromQuery] int take = 50)
    {
        take = Math.Clamp(take, 1, 200);

        var counts = await _db.OutboxMessages.AsNoTracking()
            .GroupBy(m => m.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync();

        var items = await _db.OutboxMessages.AsNoTracking()
            .Where(m => m.Status == status)
            .OrderByDescending(m => m.CreatedAt)
            .Take(take)
            .Select(m => new { m.Id, m.Channel, m.Status, m.Attempts, m.MaxAttempts, m.LastError, m.CreatedAt, m.NextAttemptAt, m.SentAt })
            .ToListAsync();

        return Ok(ApiResponse<object>.Ok(new
        {
            counts = counts.ToDictionary(c => c.Status, c => c.Count),
            status,
            items
        }));
    }

    // Requeue a single dead-lettered/failed message for another delivery attempt.
    [HttpPost("outbox/{id:guid}/retry")]
    public async Task<IActionResult> RetryOutbox(Guid id)
    {
        var msg = await _db.OutboxMessages.FirstOrDefaultAsync(m => m.Id == id);
        if (msg is null) return NotFound(ApiResponse<object>.Fail("Outbox message not found"));

        Requeue(msg);
        await _db.SaveChangesAsync();
        return Ok(new { message = "Requeued", id });
    }

    // Bulk-requeue every dead-lettered message (e.g. after fixing provider credentials).
    [HttpPost("outbox/retry-failed")]
    public async Task<IActionResult> RetryAllFailed()
    {
        var failed = await _db.OutboxMessages.Where(m => m.Status == "failed").Take(1000).ToListAsync();
        foreach (var m in failed) Requeue(m);
        await _db.SaveChangesAsync();
        return Ok(new { message = "Requeued failed messages", count = failed.Count });
    }

    // Hangfire server + queue snapshot (servers, processing/enqueued/failed/succeeded counts).
    [HttpGet("jobs")]
    public IActionResult Jobs()
    {
        var api = JobStorage.Current.GetMonitoringApi();
        var queues = api.Queues().Select(q => new { q.Name, q.Length, q.Fetched }).ToList();

        return Ok(ApiResponse<object>.Ok(new
        {
            servers = api.Servers().Count,
            processing = api.ProcessingCount(),
            enqueued = api.EnqueuedCount("default"),
            scheduled = api.ScheduledCount(),
            failed = api.FailedCount(),
            succeeded = api.SucceededListCount(),
            recurring = JobStorage.Current.GetConnection().GetRecurringJobs().Count,
            queues
        }));
    }

    private static void Requeue(Domain.Entities.OutboxMessage msg)
    {
        msg.Status = "pending";
        msg.Attempts = 0;
        msg.LastError = null;
        msg.NextAttemptAt = DateTime.UtcNow;
    }
}
