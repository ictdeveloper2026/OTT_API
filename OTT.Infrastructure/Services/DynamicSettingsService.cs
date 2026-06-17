using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OTT.Domain.Entities;
using OTT.Infrastructure.Data;

namespace OTT.Infrastructure.Services;

/// <summary>
/// Well-known dynamic setting keys. Stored per-tenant in AppConfig; TenantId == Guid.Empty
/// holds app-wide (global) values used as a fallback before appsettings.
/// </summary>
public static class SettingKeys
{
    // Payments
    public const string RazorpayEnabled = "payment.razorpay.enabled";
    public const string RazorpayKeyId = "payment.razorpay.keyId";
    public const string RazorpayKeySecret = "payment.razorpay.keySecret";
    public const string StripeEnabled = "payment.stripe.enabled";
    public const string StripeSecretKey = "payment.stripe.secretKey";
    public const string StripePublishableKey = "payment.stripe.publishableKey";

    // Email
    public const string SendGridApiKey = "email.sendgrid.apiKey";
    public const string EmailFrom = "email.fromEmail";
    public const string EmailFromName = "email.fromName";

    // Social login (client IDs are public)
    public const string GoogleClientId = "social.google.clientId";
    public const string FacebookAppId = "social.facebook.appId";
    public const string AppleBundleId = "social.apple.bundleId";

    // Feature flags (public)
    public const string FeatureDownloads = "features.downloads";
    public const string FeatureLiveTv = "features.liveTv";
    public const string FeatureWatchParty = "features.watchParty";
    public const string FeatureSignupEnabled = "features.signupEnabled";
    public const string FeatureMaintenanceMode = "features.maintenanceMode";
    public const string RequireEmailVerification = "app.requireEmailVerification";
}

public interface IDynamicSettingsService
{
    /// <summary>tenant value → global value → fallback.</summary>
    Task<string?> GetAsync(Guid tenantId, string key, string? fallback = null);
    Task<bool> GetBoolAsync(Guid tenantId, string key, bool fallback = false);
    /// <summary>All settings for a tenant (merged over global). publicOnly limits to client-safe keys.</summary>
    Task<Dictionary<string, string?>> GetAllAsync(Guid tenantId, bool publicOnly = false);
    Task SetAsync(Guid tenantId, string key, string? value, bool isPublic = false);
    Task SetManyAsync(Guid tenantId, IReadOnlyDictionary<string, string?> values, bool isPublic = false);
    void Invalidate(Guid tenantId);
}

public class DynamicSettingsService : IDynamicSettingsService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DynamicSettingsService> _logger;

    // tenantId -> (key -> (value, isPublic))
    private readonly ConcurrentDictionary<Guid, Dictionary<string, (string? Value, bool IsPublic)>> _cache = new();

    public DynamicSettingsService(IServiceScopeFactory scopeFactory, ILogger<DynamicSettingsService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public void Invalidate(Guid tenantId)
    {
        _cache.TryRemove(tenantId, out _);
        _logger.LogInformation("Dynamic settings cache invalidated for tenant {TenantId}", tenantId);
    }

    private Dictionary<string, (string? Value, bool IsPublic)> Load(Guid tenantId)
    {
        return _cache.GetOrAdd(tenantId, tid =>
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<OttDbContext>();
                return db.AppConfigs.AsNoTracking()
                    .Where(c => c.TenantId == tid)
                    .ToDictionary(c => c.Key, c => (c.Value, c.IsPublic));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not load AppConfigs for tenant {TenantId}; using empty set", tid);
                return new Dictionary<string, (string?, bool)>();
            }
        });
    }

    public Task<string?> GetAsync(Guid tenantId, string key, string? fallback = null)
    {
        // tenant-specific
        if (tenantId != Guid.Empty && Load(tenantId).TryGetValue(key, out var t) && t.Value is { Length: > 0 })
            return Task.FromResult<string?>(t.Value);
        // global
        if (Load(Guid.Empty).TryGetValue(key, out var g) && g.Value is { Length: > 0 })
            return Task.FromResult<string?>(g.Value);
        return Task.FromResult(fallback);
    }

    public async Task<bool> GetBoolAsync(Guid tenantId, string key, bool fallback = false)
    {
        var v = await GetAsync(tenantId, key);
        return v == null ? fallback : (v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase));
    }

    public Task<Dictionary<string, string?>> GetAllAsync(Guid tenantId, bool publicOnly = false)
    {
        var merged = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (k, v) in Load(Guid.Empty))
            if (!publicOnly || v.IsPublic) merged[k] = v.Value;
        if (tenantId != Guid.Empty)
            foreach (var (k, v) in Load(tenantId))
                if (!publicOnly || v.IsPublic) merged[k] = v.Value; // tenant overrides global
        return Task.FromResult(merged);
    }

    public async Task SetAsync(Guid tenantId, string key, string? value, bool isPublic = false)
        => await SetManyAsync(tenantId, new Dictionary<string, string?> { [key] = value }, isPublic);

    public async Task SetManyAsync(Guid tenantId, IReadOnlyDictionary<string, string?> values, bool isPublic = false)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OttDbContext>();

        var keys = values.Keys.ToList();
        var existing = await db.AppConfigs
            .Where(c => c.TenantId == tenantId && keys.Contains(c.Key))
            .ToListAsync();

        foreach (var (key, value) in values)
        {
            var row = existing.FirstOrDefault(c => c.Key == key);
            if (row == null)
            {
                db.AppConfigs.Add(new AppConfig
                {
                    TenantId = tenantId,
                    Key = key,
                    Value = value,
                    IsPublic = isPublic,
                    UpdatedAt = DateTime.UtcNow
                });
            }
            else
            {
                row.Value = value;
                row.IsPublic = isPublic;
                row.UpdatedAt = DateTime.UtcNow;
            }
        }

        await db.SaveChangesAsync();
        Invalidate(tenantId);
    }
}
