using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using OTT.Domain.Entities;
using BC = BCrypt.Net.BCrypt;

namespace OTT.Infrastructure.Data;

/// <summary>
/// Seeds the minimum rows the app needs to actually function on a fresh database:
/// a default tenant (required by TenantMiddleware), its branding, an admin user,
/// and a sample subscription plan. Idempotent — safe to run on every startup.
/// </summary>
public static class DbSeeder
{
    public static async Task SeedAsync(OttDbContext db, IConfiguration config)
    {
        var slug = config["App:DefaultTenantSlug"] ?? "default";

        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Slug == slug);
        if (tenant == null)
        {
            tenant = new Tenant
            {
                Name = "Default",
                Slug = slug,
                Plan = "pro",
                IsActive = true
            };
            db.Tenants.Add(tenant);
            await db.SaveChangesAsync();
        }

        if (!await db.BrandingConfigs.AnyAsync(b => b.TenantId == tenant.Id))
        {
            db.BrandingConfigs.Add(new BrandingConfig
            {
                TenantId = tenant.Id,
                AppName = "OTT Platform",
                PrimaryColor = "#E50914",
                SecondaryColor = "#141414",
                AccentColor = "#FFFFFF",
                FontFamily = "Poppins"
            });
        }

        if (!await db.Users.AnyAsync(u => u.TenantId == tenant.Id && u.Role == "admin"))
        {
            var adminEmail = config["Seed:AdminEmail"] ?? "admin@ott.local";
            var adminPassword = config["Seed:AdminPassword"] ?? "Admin@123";
            db.Users.Add(new User
            {
                TenantId = tenant.Id,
                Email = adminEmail.ToLower(),
                PasswordHash = BC.HashPassword(adminPassword),
                FirstName = "Admin",
                LastName = "User",
                Role = "admin",
                AuthProvider = "local",
                IsEmailVerified = true,
                IsActive = true
            });
        }

        if (!await db.SubscriptionPlans.AnyAsync(p => p.TenantId == tenant.Id))
        {
            db.SubscriptionPlans.Add(new SubscriptionPlan
            {
                TenantId = tenant.Id,
                Name = "Premium",
                Description = "Full access in HD/UHD on up to 4 profiles",
                Price = 199,
                Currency = "INR",
                BillingCycle = "monthly",
                MaxProfiles = 4,
                MaxStreams = 2,
                AllowDownloads = true,
                AllowUhd = true,
                IsActive = true,
                IsPopular = true
            });
        }

        await db.SaveChangesAsync();

        // Give the admin a default profile (needed for watchlist / continue-watching).
        var admin = await db.Users.FirstOrDefaultAsync(u => u.TenantId == tenant.Id && u.Role == "admin");
        if (admin != null && !await db.UserProfiles.AnyAsync(p => p.UserId == admin.Id))
        {
            db.UserProfiles.Add(new UserProfile
            {
                UserId = admin.Id,
                Name = admin.FirstName ?? "Me",
                IsDefault = true,
                MaturityLevel = "all"
            });
        }

        // A sample live channel so the Live tab has content.
        if (!await db.LiveStreams.AnyAsync(l => l.TenantId == tenant.Id))
        {
            db.LiveStreams.Add(new LiveStream
            {
                TenantId = tenant.Id,
                CreatedByUserId = admin?.Id ?? Guid.Empty,
                Title = "OTT News 24/7",
                Description = "Round-the-clock news channel.",
                ThumbnailUrl = "https://picsum.photos/seed/live1/640/360",
                Category = "News",
                StreamProvider = "youtube",
                Status = "live",
                ViewerCount = 1280,
                StartedAt = DateTime.UtcNow.AddHours(-2),
                PlaybackUrl = "https://flutter.github.io/assets-for-api-docs/assets/videos/bee.mp4"
            });
        }

        await db.SaveChangesAsync();
    }
}
