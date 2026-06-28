using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OTT.API.Middleware;
using OTT.Application.DTOs;
using OTT.Application.Services;
using OTT.Infrastructure.Services;

namespace OTT.API.Controllers;

// Targeting rule for a feature flag. Stored as JSON in the underlying setting.
public record FeatureFlagRuleDto(bool Enabled, int? Rollout, List<Guid>? AllowUsers, List<Guid>? BlockUsers, List<Guid>? AllowTenants, bool Global = false);

/// <summary>
/// Admin management of dynamic, hot-reloadable settings: payment keys, email sender,
/// social login IDs and feature flags. Per-tenant by default; set Global=true for app-wide.
/// </summary>
[ApiController]
[Route("api/admin/settings")]
[Authorize(Roles = "admin")]
public class SettingsAdminController : ControllerBase
{
    private static readonly string[] SecretFragments =
        { "secret", "apikey", "password", "credentials", "token", "privatekey" };

    private readonly IDynamicSettingsService _settings;

    public SettingsAdminController(IDynamicSettingsService settings) => _settings = settings;

    // All settings (tenant merged over global), secrets masked.
    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] bool global = false)
    {
        var tenantId = global ? Guid.Empty : HttpContext.GetTenantId();
        var all = await _settings.GetAllAsync(tenantId);
        var masked = all.ToDictionary(kv => kv.Key, kv => Mask(kv.Key, kv.Value));
        return Ok(masked);
    }

    // Bulk upsert + hot-reload.
    [HttpPut]
    public async Task<IActionResult> Update([FromBody] UpdateSettingsDto dto)
    {
        var tenantId = dto.Global ? Guid.Empty : HttpContext.GetTenantId();

        // Ignore masked values the client echoed back unchanged.
        var toSave = dto.Settings
            .Where(kv => kv.Value != "••••••")
            .ToDictionary(kv => kv.Key, kv => kv.Value);

        await _settings.SetManyAsync(tenantId, toSave, dto.IsPublic);
        return Ok(new { message = "Settings updated", count = toSave.Count });
    }

    private static string? Mask(string key, string? value)
    {
        if (string.IsNullOrEmpty(value)) return value;
        var lower = key.ToLowerInvariant();
        return SecretFragments.Any(f => lower.Contains(f)) ? "••••••" : value;
    }

    // ── Feature-flag governance ──
    // All known flags with their raw stored value (e.g. "true", "rollout:25", or a JSON rule).
    // Changes here are recorded by AuditMiddleware since they POST/PUT under /api/admin.
    [HttpGet("feature-flags")]
    public async Task<IActionResult> GetFeatureFlags([FromQuery] bool global = false)
    {
        var tenantId = global ? Guid.Empty : HttpContext.GetTenantId();
        var flags = new List<object>();
        foreach (var (key, def) in FeatureFlagService.KnownFlags)
            flags.Add(new { key, raw = await _settings.GetAsync(tenantId, key), @default = def });
        return Ok(ApiResponse<object>.Ok(flags));
    }

    // Set a flag's targeting rule (kill-switch via Enabled=false, %-rollout, allow/block lists).
    [HttpPut("feature-flags/{key}")]
    public async Task<IActionResult> SetFeatureFlag(string key, [FromBody] FeatureFlagRuleDto rule)
    {
        if (!FeatureFlagService.KnownFlags.ContainsKey(key))
            return BadRequest(ApiResponse<object>.Fail($"Unknown feature flag '{key}'"));
        if (rule.Rollout is < 0 or > 100)
            return BadRequest(ApiResponse<object>.Fail("Rollout must be between 0 and 100"));

        var tenantId = rule.Global ? Guid.Empty : HttpContext.GetTenantId();
        var json = JsonSerializer.Serialize(new
        {
            enabled = rule.Enabled,
            rollout = rule.Rollout,
            allowUsers = rule.AllowUsers,
            blockUsers = rule.BlockUsers,
            allowTenants = rule.AllowTenants
        }, new JsonSerializerOptions { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull });

        await _settings.SetAsync(tenantId, key, json, isPublic: true);
        return Ok(new { message = "Feature flag updated", key, rule = json });
    }
}
