using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OTT.Application.DTOs;
using OTT.Domain.Entities;
using OTT.Infrastructure.Data;
using OTT.Infrastructure.Services;

namespace OTT.API.Controllers;

/// <summary>
/// Lets a super-admin pick and reconfigure the storage backend at runtime.
/// Changes are persisted to the database and hot-reloaded (no redeploy).
/// </summary>
[ApiController]
[Route("api/admin/storage")]
[Authorize(Roles = "admin")]
public class StorageAdminController : ControllerBase
{
    private readonly OttDbContext _db;
    private readonly IStorageService _storage;

    public StorageAdminController(OttDbContext db, IStorageService storage)
    {
        _db = db;
        _storage = storage;
    }

    // Current effective configuration (secrets never returned).
    [HttpGet]
    public async Task<IActionResult> Get()
    {
        return Ok(await _storage.GetActiveConfigAsync());
    }

    // Create/replace the active configuration and hot-reload the provider.
    [HttpPut]
    public async Task<IActionResult> Update([FromBody] UpdateStorageConfigDto dto)
    {
        var existing = await _db.StorageConfigurations
            .Where(x => x.IsActive)
            .OrderByDescending(x => x.UpdatedAt)
            .FirstOrDefaultAsync();

        // Deactivate previous configs (keep them as history).
        var all = await _db.StorageConfigurations.Where(x => x.IsActive).ToListAsync();
        all.ForEach(x => x.IsActive = false);

        var config = new StorageConfiguration
        {
            Provider = dto.Provider.ToLowerInvariant(),
            BucketName = dto.BucketName,
            Region = dto.Region,
            AccessKey = dto.AccessKey,
            // Keep the previous secret if the caller didn't supply a new one.
            SecretKey = string.IsNullOrWhiteSpace(dto.SecretKey) ? existing?.SecretKey : dto.SecretKey,
            ServiceUrl = dto.ServiceUrl,
            ForcePathStyle = dto.ForcePathStyle,
            PublicBaseUrl = dto.PublicBaseUrl,
            LocalRootPath = dto.LocalRootPath,
            IsActive = true,
            UpdatedAt = DateTime.UtcNow
        };
        _db.StorageConfigurations.Add(config);
        await _db.SaveChangesAsync();

        _storage.Invalidate(); // hot-reload on next use

        return Ok(await _storage.GetActiveConfigAsync());
    }

    // Verify the configured backend is reachable by doing a harmless existence check.
    [HttpPost("test")]
    public async Task<IActionResult> Test()
    {
        try
        {
            await _storage.ExistsAsync($"_healthcheck/{Guid.NewGuid():N}.txt");
            var info = await _storage.GetActiveConfigAsync();
            return Ok(new { success = true, provider = info.Provider, source = info.Source });
        }
        catch (Exception ex)
        {
            return BadRequest(new { success = false, error = ex.Message });
        }
    }
}
