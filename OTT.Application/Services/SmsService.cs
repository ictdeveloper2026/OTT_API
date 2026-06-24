using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OTT.Infrastructure.Services;

namespace OTT.Application.Services;

/// <summary>
/// Sends transactional SMS (OTP / 2FA). Provider credentials come from admin-configurable
/// settings first, then appsettings. When unconfigured it no-ops (returns false) exactly like
/// the email path, so local/dev runs don't fail.
/// </summary>
public interface ISmsService
{
    bool IsConfigured { get; }
    /// <summary>Returns true if the message was accepted by the provider.</summary>
    Task<bool> SendAsync(string toPhone, string message);
}

/// <summary>
/// Twilio implementation over the REST API (no SDK dependency). Uses the factory HttpClient, so
/// it inherits the app-wide resilience handler (retry/timeout/circuit breaker).
/// </summary>
public class TwilioSmsService : ISmsService
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly IConfiguration _config;
    private readonly IDynamicSettingsService _settings;
    private readonly ILogger<TwilioSmsService> _logger;

    public TwilioSmsService(IHttpClientFactory httpFactory, IConfiguration config,
        IDynamicSettingsService settings, ILogger<TwilioSmsService> logger)
    {
        _httpFactory = httpFactory;
        _config = config;
        _settings = settings;
        _logger = logger;
    }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_config["Twilio:AccountSid"])
        && !string.IsNullOrWhiteSpace(_config["Twilio:AuthToken"]);

    public async Task<bool> SendAsync(string toPhone, string message)
    {
        // Admin-configurable settings win over appsettings.
        var sid = await _settings.GetAsync(Guid.Empty, SettingKeys.TwilioAccountSid, _config["Twilio:AccountSid"]);
        var token = await _settings.GetAsync(Guid.Empty, SettingKeys.TwilioAuthToken, _config["Twilio:AuthToken"]);
        var from = await _settings.GetAsync(Guid.Empty, SettingKeys.TwilioFromNumber, _config["Twilio:FromNumber"]);

        if (string.IsNullOrWhiteSpace(sid) || string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(from))
        {
            _logger.LogWarning("Twilio not configured; skipping SMS to {Phone}", Mask(toPhone));
            return false;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post,
                $"https://api.twilio.com/2010-04-01/Accounts/{sid}/Messages.json");
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic",
                Convert.ToBase64String(Encoding.ASCII.GetBytes($"{sid}:{token}")));
            request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["To"] = toPhone,
                ["From"] = from,
                ["Body"] = message
            });

            var client = _httpFactory.CreateClient();
            var response = await client.SendAsync(request);
            if (response.IsSuccessStatusCode) return true;

            _logger.LogError("Twilio SMS to {Phone} failed: {Status}", Mask(toPhone), (int)response.StatusCode);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Twilio SMS send to {Phone} failed", Mask(toPhone));
            return false;
        }
    }

    // Never log full phone numbers (PII).
    private static string Mask(string phone) =>
        phone.Length <= 4 ? "****" : new string('*', phone.Length - 4) + phone[^4..];
}
