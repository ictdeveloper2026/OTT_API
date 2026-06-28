using Microsoft.Extensions.Configuration;

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

// Composes notification content and hands it to the outbox; the actual SendGrid/FCM
// delivery happens off the request thread in OutboxService (with retry + DLQ).
public class NotificationService : INotificationService
{
    private readonly IOutboxService _outbox;
    private readonly string _appBaseUrl;

    public NotificationService(IOutboxService outbox, IConfiguration config)
    {
        _outbox = outbox;
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

    public Task SendPushNotificationAsync(Guid userId, string title, string body, Dictionary<string, string>? data = null)
        => _outbox.EnqueuePushAsync(new[] { userId }, title, body, data);

    public Task SendBulkPushAsync(List<Guid> userIds, string title, string body, Dictionary<string, string>? data = null)
        => _outbox.EnqueuePushAsync(userIds, title, body, data);

    // Enqueue to the outbox and return immediately — never blocks the request on SendGrid.
    public Task SendEmailAsync(string toEmail, string toName, string subject, string htmlContent)
        => _outbox.EnqueueEmailAsync(toEmail, toName, subject, htmlContent);
}
