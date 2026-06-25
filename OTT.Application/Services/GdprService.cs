using Microsoft.EntityFrameworkCore;
using OTT.Domain.Entities;
using OTT.Infrastructure.Data;

namespace OTT.Application.Services;

/// <summary>
/// GDPR / DPDP self-service: the right to data portability (export) and the right to erasure
/// (delete). Erasure anonymizes the user and removes behavioural data while retaining financial
/// records (payments/subscriptions) that law typically requires keeping; the request is written to
/// the audit log for a compliance trail.
/// </summary>
public interface IGdprService
{
    Task<object> ExportAsync(Guid userId, Guid tenantId);
    /// <summary>Anonymizes PII, deletes behavioural data, revokes tokens, soft-deletes the account.</summary>
    Task<bool> DeleteAccountAsync(Guid userId, Guid tenantId, string? ipAddress);
}

public class GdprService : IGdprService
{
    private readonly OttDbContext _db;
    public GdprService(OttDbContext db) => _db = db;

    public async Task<object> ExportAsync(Guid userId, Guid tenantId)
    {
        var user = await _db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId && u.TenantId == tenantId)
            ?? throw new KeyNotFoundException("User not found");

        var profiles = await _db.UserProfiles.AsNoTracking()
            .Where(p => p.UserId == userId)
            .Select(p => new { p.Id, p.Name, p.AvatarUrl, p.IsDefault, p.MaturityLevel, p.Language })
            .ToListAsync();
        var profileIds = profiles.Select(p => p.Id).ToList();

        var subscriptions = await _db.UserSubscriptions.AsNoTracking()
            .Where(s => s.UserId == userId)
            .Select(s => new { s.Id, s.PlanId, s.Status, s.StartDate, s.EndDate })
            .ToListAsync();

        var payments = await _db.Payments.AsNoTracking()
            .Where(p => p.UserId == userId)
            .Select(p => new { p.Id, p.Gateway, p.Amount, p.Currency, p.Status, p.Description, p.CreatedAt })
            .ToListAsync();

        var watchHistory = await _db.WatchHistories.AsNoTracking()
            .Where(w => profileIds.Contains(w.ProfileId))
            .Select(w => new { w.ProfileId, w.ContentId, w.EpisodeId, w.PositionSeconds, w.IsCompleted, w.LastWatchedAt })
            .ToListAsync();

        var ratings = await _db.UserRatings.AsNoTracking()
            .Where(r => profileIds.Contains(r.ProfileId))
            .Select(r => new { r.ProfileId, r.ContentId, r.Rating, r.Review, r.CreatedAt })
            .ToListAsync();

        var watchlist = await _db.Watchlists.AsNoTracking()
            .Where(w => profileIds.Contains(w.ProfileId))
            .Select(w => new { w.ProfileId, w.ContentId, w.AddedAt })
            .ToListAsync();

        var devices = await _db.DeviceTokens.AsNoTracking()
            .Where(d => d.UserId == userId)
            .Select(d => new { d.Platform, d.IsActive, d.CreatedAt })
            .ToListAsync();

        return new
        {
            exportedAt = DateTime.UtcNow,
            account = new { user.Id, user.Email, user.FirstName, user.LastName, user.Phone, user.Role, user.AuthProvider, user.CreatedAt },
            profiles,
            subscriptions,
            payments,
            watchHistory,
            ratings,
            watchlist,
            devices
        };
    }

    public async Task<bool> DeleteAccountAsync(Guid userId, Guid tenantId, string? ipAddress)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId && u.TenantId == tenantId);
        if (user is null) return false;

        var profileIds = await _db.UserProfiles.Where(p => p.UserId == userId).Select(p => p.Id).ToListAsync();

        // Remove behavioural data (keyed by the user's profiles).
        if (profileIds.Count > 0)
        {
            await _db.WatchHistories.Where(w => profileIds.Contains(w.ProfileId)).ExecuteDeleteAsync();
            await _db.UserRatings.Where(r => profileIds.Contains(r.ProfileId)).ExecuteDeleteAsync();
            await _db.Watchlists.Where(w => profileIds.Contains(w.ProfileId)).ExecuteDeleteAsync();
        }
        await _db.DeviceTokens.Where(d => d.UserId == userId).ExecuteDeleteAsync();
        await _db.RefreshTokens.Where(t => t.UserId == userId).ExecuteDeleteAsync();
        await _db.UserProfiles.Where(p => p.UserId == userId).ExecuteDeleteAsync();

        // Anonymize PII but keep the row so financial records (payments/subscriptions) stay valid.
        user.Email = $"deleted+{userId:N}@anonymized.invalid";
        user.FirstName = null;
        user.LastName = null;
        user.Phone = null;
        user.AvatarUrl = null;
        user.PasswordHash = null;
        user.IsActive = false;
        user.IsDeleted = true;

        // Compliance trail of the erasure request.
        _db.AuditLogs.Add(new AuditLog
        {
            TenantId = tenantId,
            ActorUserId = userId,
            ActorEmail = "[erased]",
            Action = "DELETE",
            Path = "/api/account",
            StatusCode = 200,
            IpAddress = ipAddress
        });

        await _db.SaveChangesAsync();
        return true;
    }
}
