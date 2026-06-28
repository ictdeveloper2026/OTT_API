using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OTT.Infrastructure.Services;

namespace OTT.Application.Services;

/// <summary>
/// Evaluates feature flags with kill-switch, percentage rollout, and per-user/tenant targeting,
/// layered on top of the existing <see cref="IDynamicSettingsService"/> (flags are still stored
/// as <c>features.*</c> AppConfig rows). A flag value can be:
/// <list type="bullet">
/// <item><c>true</c>/<c>1</c> — on for everyone; <c>false</c>/<c>0</c>/empty — off (kill-switch).</item>
/// <item><c>rollout:N</c> — on for a deterministic N% of users.</item>
/// <item>JSON <c>{"enabled":true,"rollout":25,"allowUsers":[..],"blockUsers":[..],"allowTenants":[..]}</c>.</item>
/// </list>
/// </summary>
public interface IFeatureFlagService
{
    Task<bool> IsEnabledAsync(string key, Guid tenantId, Guid? userId, bool fallback = false);
    /// <summary>Evaluates every known flag for the caller (used by the public config endpoint).</summary>
    Task<Dictionary<string, bool>> EvaluateAllAsync(Guid tenantId, Guid? userId);
}

public class FeatureFlagService : IFeatureFlagService
{
    private readonly IDynamicSettingsService _settings;

    // Flags surfaced to clients, with their default when unset.
    public static readonly IReadOnlyDictionary<string, bool> KnownFlags = new Dictionary<string, bool>
    {
        [SettingKeys.FeatureDownloads] = true,
        [SettingKeys.FeatureLiveTv] = true,
        [SettingKeys.FeatureWatchParty] = true,
        [SettingKeys.FeatureSignupEnabled] = true,
        [SettingKeys.FeatureMaintenanceMode] = false,
    };

    public FeatureFlagService(IDynamicSettingsService settings) => _settings = settings;

    public async Task<bool> IsEnabledAsync(string key, Guid tenantId, Guid? userId, bool fallback = false)
    {
        var raw = await _settings.GetAsync(tenantId, key);
        if (string.IsNullOrWhiteSpace(raw)) return fallback;
        return Evaluate(key, raw.Trim(), tenantId, userId);
    }

    public async Task<Dictionary<string, bool>> EvaluateAllAsync(Guid tenantId, Guid? userId)
    {
        var result = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, def) in KnownFlags)
            result[ShortName(key)] = await IsEnabledAsync(key, tenantId, userId, def);
        return result;
    }

    private static bool Evaluate(string key, string raw, Guid tenantId, Guid? userId)
    {
        // JSON rule form.
        if (raw.StartsWith('{'))
        {
            FlagRule? rule;
            try { rule = JsonSerializer.Deserialize<FlagRule>(raw, JsonOpts); }
            catch { return false; } // malformed rule fails closed
            if (rule is null) return false;

            if (rule.BlockUsers is { Count: > 0 } && userId is { } bu && rule.BlockUsers.Contains(bu)) return false;
            if (!rule.Enabled) return false;
            if (rule.AllowUsers is { Count: > 0 } && userId is { } au && rule.AllowUsers.Contains(au)) return true;
            if (rule.AllowTenants is { Count: > 0 } && rule.AllowTenants.Contains(tenantId)) return true;
            // If explicit allow-lists exist but the caller matched none, only rollout can let them in.
            if (rule.Rollout is { } pct) return InRollout(key, userId, pct);
            // No rollout and no allow-list match → enabled-for-all (allow-lists act as additive overrides).
            return rule.AllowUsers is not { Count: > 0 } && rule.AllowTenants is not { Count: > 0 };
        }

        // rollout:N shorthand.
        if (raw.StartsWith("rollout:", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(raw.AsSpan("rollout:".Length), out var p))
            return InRollout(key, userId, p);

        // Plain boolean / kill-switch.
        return raw is "1" || raw.Equals("true", StringComparison.OrdinalIgnoreCase);
    }

    // Deterministic, stable bucketing: the same user always lands in the same bucket for a flag,
    // so a ramp from 10%→20% only ever adds users (never churns who already had it).
    private static bool InRollout(string key, Guid? userId, int percent)
    {
        if (percent <= 0) return false;
        if (percent >= 100) return true;
        if (userId is null) return false; // anonymous can't be stably bucketed
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{key}:{userId}"));
        var bucket = (BitConverter.ToUInt32(bytes, 0)) % 100; // 0..99
        return bucket < (uint)percent;
    }

    private static string ShortName(string key) =>
        key.StartsWith("features.", StringComparison.OrdinalIgnoreCase) ? key["features.".Length..] : key;

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private sealed class FlagRule
    {
        public bool Enabled { get; set; } = true;
        public int? Rollout { get; set; }
        public List<Guid>? AllowUsers { get; set; }
        public List<Guid>? BlockUsers { get; set; }
        public List<Guid>? AllowTenants { get; set; }
    }
}
