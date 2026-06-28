using Microsoft.EntityFrameworkCore;
using OTT.Infrastructure.Data;

namespace OTT.Application.Services;

/// <summary>
/// Decides whether a user is allowed to obtain playable stream URLs for a title.
/// This is the paywall: it MUST run before any signed stream URL is issued.
/// </summary>
public interface IEntitlementService
{
    /// <summary>
    /// Returns true if <paramref name="userId"/> may watch <paramref name="contentId"/> within
    /// <paramref name="tenantId"/>. Throws <see cref="KeyNotFoundException"/> if the content does
    /// not exist in that tenant (also closes the cross-tenant content-access hole).
    /// </summary>
    Task<bool> CanWatchAsync(Guid userId, Guid tenantId, Guid contentId);
}

public class EntitlementService : IEntitlementService
{
    private readonly OttDbContext _db;

    public EntitlementService(OttDbContext db) => _db = db;

    public async Task<bool> CanWatchAsync(Guid userId, Guid tenantId, Guid contentId)
    {
        // Tenant-scoped lookup: a title from another tenant is treated as "not found".
        var content = await _db.Contents
            .AsNoTracking()
            .Where(c => c.Id == contentId && c.TenantId == tenantId && c.Status == "published")
            .Select(c => new { c.MonetizationModel })
            .FirstOrDefaultAsync()
            ?? throw new KeyNotFoundException("Content not found");

        switch (content.MonetizationModel?.ToLowerInvariant())
        {
            case "free":
            case "avod":
                return true;

            case "svod":
                return await HasActiveSubscriptionAsync(userId);

            case "tvod":
                // Per-title purchase, or an active subscription (subscribers see TVOD titles too).
                return await HasActiveSubscriptionAsync(userId)
                    || await _db.Payments.AsNoTracking().AnyAsync(p =>
                        p.UserId == userId
                        && p.ContentId == contentId
                        && p.Status == "success");

            default:
                // Unknown model → deny by default (fail closed).
                return false;
        }
    }

    private Task<bool> HasActiveSubscriptionAsync(Guid userId) =>
        _db.UserSubscriptions.AsNoTracking().AnyAsync(s =>
            s.UserId == userId
            && s.Status == "active"
            && s.EndDate > DateTime.UtcNow);
}
