using FirebaseAdmin.Messaging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OTT.Infrastructure.Data;
using OTT.Infrastructure.Services;
using SendGrid;
using SendGrid.Helpers.Mail;
using Microsoft.EntityFrameworkCore;

namespace OTT.Application.Services;

public interface INotificationService
{
    Task SendEmailVerificationAsync(string email, string name, string token);
    Task SendOtpEmailAsync(string email, string name, string otp);
    Task SendPasswordResetAsync(string email, string name, string token);
    Task SendPushNotificationAsync(Guid userId, string title, string body, Dictionary<string, string>? data = null);
    Task SendBulkPushAsync(List<Guid> userIds, string title, string body, Dictionary<string, string>? data = null);
    Task SendWelcomeEmailAsync(string email, string name);
    Task SendSubscriptionConfirmationAsync(string email, string name, string planName, DateTime expiryDate);
    Task SendNewContentEmailAsync(string email, string name, string contentTitle, string contentUrl);
    Task SendEmailAsync(string toEmail, string toName, string subject, string htmlContent);
}

public class NotificationService : INotificationService
{
    private readonly OttDbContext _db;
    private readonly IConfiguration _config;
    private readonly IDynamicSettingsService _settings;
    private readonly ILogger<NotificationService> _logger;
    private readonly string _sendGridKey;
    private readonly string _fromEmail;
    private readonly string _fromName;
    private readonly string _appBaseUrl;

    public NotificationService(OttDbContext db, IConfiguration config, IDynamicSettingsService settings, ILogger<NotificationService> logger)
    {
        _db = db;
        _config = config;
        _settings = settings;
        _logger = logger;
        _sendGridKey = config["SendGrid:ApiKey"] ?? "";
        _fromEmail = config["SendGrid:FromEmail"] ?? "noreply@ottplatform.com";
        _fromName = config["SendGrid:FromName"] ?? "OTT Platform";
        _appBaseUrl = config["App:BaseUrl"] ?? "https://app.ottplatform.com";
    }

    public async Task SendEmailVerificationAsync(string email, string name, string token)
    {
        var verifyUrl = $"{_appBaseUrl}/verify-email?token={token}";
        var html = $"""
            <div style="font-family:Arial,sans-serif;max-width:600px;margin:0 auto;background:#141414;color:#fff;padding:40px;border-radius:12px">
                <h1 style="color:#E50914;text-align:center">Verify Your Email</h1>
                <p>Hi {name},</p>
                <p>Welcome! Please verify your email address to get started.</p>
                <div style="text-align:center;margin:30px 0">
                    <a href="{verifyUrl}" style="background:#E50914;color:#fff;padding:16px 32px;border-radius:8px;text-decoration:none;font-size:16px;font-weight:bold">
                        Verify Email
                    </a>
                </div>
                <p style="color:#aaa;font-size:12px">Link expires in 24 hours. If you didn't create an account, ignore this email.</p>
            </div>
        """;
        await SendEmailAsync(email, name, "Verify your email address", html);
    }

    public async Task SendOtpEmailAsync(string email, string name, string otp)
    {
        var html = $"""
            <div style="font-family:Arial,sans-serif;max-width:600px;margin:0 auto;background:#141414;color:#fff;padding:40px;border-radius:12px">
                <h1 style="color:#E50914;text-align:center">Your OTP</h1>
                <p>Hi {name},</p>
                <p>Use the following OTP to verify your login. Valid for 10 minutes.</p>
                <div style="text-align:center;margin:30px 0">
                    <span style="background:#222;color:#E50914;padding:20px 40px;border-radius:8px;font-size:36px;font-weight:bold;letter-spacing:8px;border:2px solid #E50914">
                        {otp}
                    </span>
                </div>
                <p style="color:#aaa;font-size:12px">Never share your OTP with anyone.</p>
            </div>
        """;
        await SendEmailAsync(email, name, $"Your OTP: {otp}", html);
    }

    public async Task SendPasswordResetAsync(string email, string name, string token)
    {
        var resetUrl = $"{_appBaseUrl}/reset-password?token={token}";
        var html = $"""
            <div style="font-family:Arial,sans-serif;max-width:600px;margin:0 auto;background:#141414;color:#fff;padding:40px;border-radius:12px">
                <h1 style="color:#E50914;text-align:center">Reset Password</h1>
                <p>Hi {name},</p>
                <p>Click the button below to reset your password. This link expires in 1 hour.</p>
                <div style="text-align:center;margin:30px 0">
                    <a href="{resetUrl}" style="background:#E50914;color:#fff;padding:16px 32px;border-radius:8px;text-decoration:none;font-size:16px;font-weight:bold">
                        Reset Password
                    </a>
                </div>
                <p style="color:#aaa;font-size:12px">If you didn't request this, please ignore this email.</p>
            </div>
        """;
        await SendEmailAsync(email, name, "Reset your password", html);
    }

    public async Task SendWelcomeEmailAsync(string email, string name)
    {
        var html = $"""
            <div style="font-family:Arial,sans-serif;max-width:600px;margin:0 auto;background:#141414;color:#fff;padding:40px;border-radius:12px">
                <h1 style="color:#E50914;text-align:center">Welcome to OTT Platform!</h1>
                <p>Hi {name},</p>
                <p>Your account has been created successfully. Start exploring thousands of movies, shows, and live streams.</p>
                <div style="text-align:center;margin:30px 0">
                    <a href="{_appBaseUrl}" style="background:#E50914;color:#fff;padding:16px 32px;border-radius:8px;text-decoration:none;font-size:16px;font-weight:bold">
                        Start Watching
                    </a>
                </div>
            </div>
        """;
        await SendEmailAsync(email, name, "Welcome to OTT Platform", html);
    }

    public async Task SendSubscriptionConfirmationAsync(string email, string name, string planName, DateTime expiryDate)
    {
        var html = $"""
            <div style="font-family:Arial,sans-serif;max-width:600px;margin:0 auto;background:#141414;color:#fff;padding:40px;border-radius:12px">
                <h1 style="color:#E50914;text-align:center">Subscription Confirmed!</h1>
                <p>Hi {name},</p>
                <p>Your <strong>{planName}</strong> subscription is now active.</p>
                <div style="background:#222;padding:20px;border-radius:8px;margin:20px 0;border-left:4px solid #E50914">
                    <p style="margin:0;color:#aaa">Subscription Plan: <strong style="color:#fff">{planName}</strong></p>
                    <p style="margin:8px 0 0;color:#aaa">Valid Until: <strong style="color:#fff">{expiryDate:MMMM dd, yyyy}</strong></p>
                </div>
                <div style="text-align:center;margin:30px 0">
                    <a href="{_appBaseUrl}" style="background:#E50914;color:#fff;padding:16px 32px;border-radius:8px;text-decoration:none;font-size:16px;font-weight:bold">
                        Start Watching
                    </a>
                </div>
            </div>
        """;
        await SendEmailAsync(email, name, "Subscription Confirmed", html);
    }

    public async Task SendNewContentEmailAsync(string email, string name, string contentTitle, string contentUrl)
    {
        var html = $"""
            <div style="font-family:Arial,sans-serif;max-width:600px;margin:0 auto;background:#141414;color:#fff;padding:40px;border-radius:12px">
                <h1 style="color:#E50914;text-align:center">New on OTT Platform</h1>
                <p>Hi {name},</p>
                <p>A new title has been added: <strong>{contentTitle}</strong></p>
                <div style="text-align:center;margin:30px 0">
                    <a href="{contentUrl}" style="background:#E50914;color:#fff;padding:16px 32px;border-radius:8px;text-decoration:none;font-size:16px;font-weight:bold">
                        Watch Now
                    </a>
                </div>
            </div>
        """;
        await SendEmailAsync(email, name, $"New: {contentTitle}", html);
    }

    public async Task SendPushNotificationAsync(Guid userId, string title, string body, Dictionary<string, string>? data = null)
    {
        var tokens = await _db.DeviceTokens
            .Where(d => d.UserId == userId && d.IsActive)
            .Select(d => d.Token)
            .ToListAsync();

        if (!tokens.Any()) return;
        await SendFcmMulticastAsync(tokens, title, body, data);
    }

    public async Task SendBulkPushAsync(List<Guid> userIds, string title, string body, Dictionary<string, string>? data = null)
    {
        var tokens = await _db.DeviceTokens
            .Where(d => userIds.Contains(d.UserId) && d.IsActive)
            .Select(d => d.Token)
            .ToListAsync();

        if (!tokens.Any()) return;

        // FCM allows max 500 per multicast
        foreach (var batch in tokens.Chunk(500))
            await SendFcmMulticastAsync(batch.ToList(), title, body, data);
    }

    public async Task SendEmailAsync(string toEmail, string toName, string subject, string htmlContent)
    {
        // Resolve from dynamic (admin-configurable) settings first, then appsettings.
        var sendGridKey = await _settings.GetAsync(Guid.Empty, SettingKeys.SendGridApiKey, _sendGridKey);
        var fromEmail = await _settings.GetAsync(Guid.Empty, SettingKeys.EmailFrom, _fromEmail) ?? _fromEmail;
        var fromName = await _settings.GetAsync(Guid.Empty, SettingKeys.EmailFromName, _fromName) ?? _fromName;

        if (string.IsNullOrEmpty(sendGridKey))
        {
            _logger.LogWarning("SendGrid API key not configured. Skipping email to {Email}", toEmail);
            return;
        }

        try
        {
            var client = new SendGridClient(sendGridKey);
            var from = new EmailAddress(fromEmail, fromName);
            var to = new EmailAddress(toEmail, toName);
            var msg = MailHelper.CreateSingleEmail(from, to, subject, null, htmlContent);
            var response = await client.SendEmailAsync(msg);

            if (!response.IsSuccessStatusCode)
                _logger.LogError("SendGrid failed for {Email}: {Status}", toEmail, response.StatusCode);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email to {Email}", toEmail);
        }
    }

    private async Task SendFcmMulticastAsync(List<string> tokens, string title, string body, Dictionary<string, string>? data)
    {
        try
        {
            var message = new MulticastMessage
            {
                Tokens = tokens,
                Notification = new Notification { Title = title, Body = body },
                Data = data ?? new Dictionary<string, string>(),
                Android = new AndroidConfig
                {
                    Priority = Priority.High,
                    Notification = new AndroidNotification
                    {
                        Icon = "notification_icon",
                        Color = "#E50914",
                        Sound = "default"
                    }
                },
                Apns = new ApnsConfig
                {
                    Aps = new Aps
                    {
                        Alert = new ApsAlert { Title = title, Body = body },
                        Sound = "default",
                        Badge = 1
                    }
                }
            };

            var response = await FirebaseMessaging.DefaultInstance.SendEachForMulticastAsync(message);
            _logger.LogInformation("FCM sent: {Success}/{Total}", response.SuccessCount, tokens.Count);

            // Remove invalid tokens
            var invalidTokens = new List<string>();
            for (int i = 0; i < response.Responses.Count; i++)
            {
                if (!response.Responses[i].IsSuccess)
                {
                    var errorCode = response.Responses[i].Exception?.MessagingErrorCode;
                    if (errorCode == MessagingErrorCode.Unregistered || errorCode == MessagingErrorCode.InvalidArgument)
                        invalidTokens.Add(tokens[i]);
                }
            }

            if (invalidTokens.Any())
            {
                var tokensToDeactivate = await _db.DeviceTokens
                    .Where(d => invalidTokens.Contains(d.Token))
                    .ToListAsync();
                tokensToDeactivate.ForEach(t => t.IsActive = false);
                await _db.SaveChangesAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FCM multicast failed");
        }
    }
}
