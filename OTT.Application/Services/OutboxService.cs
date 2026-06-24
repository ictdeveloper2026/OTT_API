using System.Text.Json;
using FirebaseAdmin.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OTT.Domain.Entities;
using OTT.Infrastructure.Data;
using OTT.Infrastructure.Services;
using SendGrid;
using SendGrid.Helpers.Mail;
using FcmNotification = FirebaseAdmin.Messaging.Notification;

namespace OTT.Application.Services;

/// <summary>
/// Transactional outbox for outbound email/push. Producers enqueue a row (a cheap local
/// INSERT) and return immediately; a background job drains the table and performs the
/// actual SendGrid/FCM call with retry + exponential backoff. After <c>MaxAttempts</c> a
/// message is parked as <c>failed</c> (dead-letter) instead of being retried forever.
/// </summary>
public interface IOutboxService
{
    Task EnqueueEmailAsync(string toEmail, string toName, string subject, string htmlContent);
    Task EnqueuePushAsync(IEnumerable<Guid> userIds, string title, string body, Dictionary<string, string>? data);
    /// <summary>Drains up to <paramref name="batchSize"/> due messages. Returns how many were sent.</summary>
    Task<int> DispatchPendingAsync(int batchSize = 50);
}

public class OutboxService : IOutboxService
{
    private readonly OttDbContext _db;
    private readonly IConfiguration _config;
    private readonly IDynamicSettingsService _settings;
    private readonly ILogger<OutboxService> _logger;

    private static readonly JsonSerializerOptions _json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public OutboxService(OttDbContext db, IConfiguration config, IDynamicSettingsService settings, ILogger<OutboxService> logger)
    {
        _db = db;
        _config = config;
        _settings = settings;
        _logger = logger;
    }

    private sealed record EmailPayload(string ToEmail, string ToName, string Subject, string Html);
    private sealed record PushPayload(List<Guid> UserIds, string Title, string Body, Dictionary<string, string>? Data);

    public async Task EnqueueEmailAsync(string toEmail, string toName, string subject, string htmlContent)
    {
        _db.OutboxMessages.Add(new OutboxMessage
        {
            Channel = "email",
            Payload = JsonSerializer.Serialize(new EmailPayload(toEmail, toName, subject, htmlContent), _json)
        });
        await _db.SaveChangesAsync();
    }

    public async Task EnqueuePushAsync(IEnumerable<Guid> userIds, string title, string body, Dictionary<string, string>? data)
    {
        var ids = userIds.Distinct().ToList();
        if (ids.Count == 0) return;
        _db.OutboxMessages.Add(new OutboxMessage
        {
            Channel = "push",
            Payload = JsonSerializer.Serialize(new PushPayload(ids, title, body, data), _json)
        });
        await _db.SaveChangesAsync();
    }

    public async Task<int> DispatchPendingAsync(int batchSize = 50)
    {
        var now = DateTime.UtcNow;
        var due = await _db.OutboxMessages
            .Where(m => m.Status == "pending" && m.NextAttemptAt <= now)
            .OrderBy(m => m.NextAttemptAt)
            .Take(batchSize)
            .ToListAsync();

        if (due.Count == 0) return 0;

        var sent = 0;
        foreach (var msg in due)
        {
            try
            {
                var ok = msg.Channel switch
                {
                    "email" => await DispatchEmailAsync(msg.Payload),
                    "push" => await DispatchPushAsync(msg.Payload),
                    _ => throw new InvalidOperationException($"Unknown outbox channel '{msg.Channel}'")
                };

                if (ok)
                {
                    msg.Status = "sent";
                    msg.SentAt = DateTime.UtcNow;
                    msg.LastError = null;
                    sent++;
                }
                else
                {
                    Backoff(msg, "Transport reported a non-success result");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Outbox dispatch error for message {Id} ({Channel})", msg.Id, msg.Channel);
                Backoff(msg, ex.Message);
            }
        }

        await _db.SaveChangesAsync();
        return sent;
    }

    // Records a failed attempt; schedules an exponential-backoff retry or dead-letters the
    // message once MaxAttempts is exhausted so a permanently-bad recipient can't loop forever.
    private void Backoff(OutboxMessage msg, string error)
    {
        msg.Attempts++;
        msg.LastError = error.Length > 2000 ? error[..2000] : error;
        if (msg.Attempts >= msg.MaxAttempts)
        {
            msg.Status = "failed"; // dead-letter
            _logger.LogError("Outbox message {Id} dead-lettered after {Attempts} attempts: {Error}",
                msg.Id, msg.Attempts, error);
        }
        else
        {
            // 2^attempts minutes, capped at 1 hour.
            var delay = TimeSpan.FromMinutes(Math.Min(Math.Pow(2, msg.Attempts), 60));
            msg.NextAttemptAt = DateTime.UtcNow.Add(delay);
        }
    }

    private async Task<bool> DispatchEmailAsync(string payload)
    {
        var p = JsonSerializer.Deserialize<EmailPayload>(payload, _json)!;

        // Admin-configurable settings win over appsettings.
        var sendGridKey = await _settings.GetAsync(Guid.Empty, SettingKeys.SendGridApiKey, _config["SendGrid:ApiKey"]);
        var fromEmail = await _settings.GetAsync(Guid.Empty, SettingKeys.EmailFrom, _config["SendGrid:FromEmail"] ?? "noreply@ottplatform.com");
        var fromName = await _settings.GetAsync(Guid.Empty, SettingKeys.EmailFromName, _config["SendGrid:FromName"] ?? "OTT Platform");

        if (string.IsNullOrEmpty(sendGridKey))
        {
            // No provider configured (e.g. local dev). Treat as delivered so it doesn't pile up in the outbox.
            _logger.LogWarning("SendGrid API key not configured; dropping queued email to {Email}", p.ToEmail);
            return true;
        }

        var client = new SendGridClient(sendGridKey);
        var msg = MailHelper.CreateSingleEmail(
            new EmailAddress(fromEmail, fromName), new EmailAddress(p.ToEmail, p.ToName), p.Subject, null, p.Html);
        var response = await client.SendEmailAsync(msg);

        if (response.IsSuccessStatusCode) return true;
        throw new InvalidOperationException($"SendGrid returned {(int)response.StatusCode}");
    }

    private async Task<bool> DispatchPushAsync(string payload)
    {
        var p = JsonSerializer.Deserialize<PushPayload>(payload, _json)!;

        // Resolve device tokens at send time so deactivated tokens are excluded.
        var tokens = await _db.DeviceTokens
            .Where(d => p.UserIds.Contains(d.UserId) && d.IsActive)
            .Select(d => d.Token)
            .ToListAsync();

        if (tokens.Count == 0) return true; // nothing to deliver to — consider it done

        if (FirebaseMessaging.DefaultInstance == null)
        {
            _logger.LogWarning("Firebase not initialized; dropping queued push for {Count} user(s)", p.UserIds.Count);
            return true;
        }

        var invalidTokens = new List<string>();
        foreach (var batch in tokens.Chunk(500)) // FCM multicast cap
        {
            var message = new MulticastMessage
            {
                Tokens = batch.ToList(),
                Notification = new FcmNotification { Title = p.Title, Body = p.Body },
                Data = p.Data ?? new Dictionary<string, string>(),
                Android = new AndroidConfig
                {
                    Priority = Priority.High,
                    Notification = new AndroidNotification { Icon = "notification_icon", Color = "#E50914", Sound = "default" }
                },
                Apns = new ApnsConfig
                {
                    Aps = new Aps { Alert = new ApsAlert { Title = p.Title, Body = p.Body }, Sound = "default", Badge = 1 }
                }
            };

            var response = await FirebaseMessaging.DefaultInstance.SendEachForMulticastAsync(message);
            var batchTokens = batch.ToList();
            for (int i = 0; i < response.Responses.Count; i++)
            {
                if (response.Responses[i].IsSuccess) continue;
                var code = response.Responses[i].Exception?.MessagingErrorCode;
                if (code is MessagingErrorCode.Unregistered or MessagingErrorCode.InvalidArgument)
                    invalidTokens.Add(batchTokens[i]);
            }
        }

        if (invalidTokens.Count > 0)
        {
            await _db.DeviceTokens
                .Where(d => invalidTokens.Contains(d.Token))
                .ExecuteUpdateAsync(s => s.SetProperty(d => d.IsActive, false));
        }

        return true;
    }
}
