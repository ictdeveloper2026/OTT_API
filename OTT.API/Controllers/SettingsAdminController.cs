using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OTT.API.Middleware;
using OTT.Application.DTOs;
using OTT.Infrastructure.Services;

namespace OTT.API.Controllers;

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
}
