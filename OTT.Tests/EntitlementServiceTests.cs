using Microsoft.EntityFrameworkCore;
using OTT.Application.Services;
using OTT.Domain.Entities;
using OTT.Infrastructure.Data;
using Xunit;

namespace OTT.Tests;

public class EntitlementServiceTests
{
    private static OttDbContext NewDb() =>
        new(new DbContextOptionsBuilder<OttDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static async Task<(OttDbContext db, Guid tenantId, Guid userId, Guid contentId)> SeedFullAsync(
        string monetization, bool activeSub = false, bool ownsContent = false)
    {
        var db = NewDb();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var contentId = Guid.NewGuid();

        db.Contents.Add(new Content
        {
            Id = contentId, TenantId = tenantId, Title = "Test", Type = "movie",
            Status = "published", MonetizationModel = monetization
        });
        if (activeSub)
            db.UserSubscriptions.Add(new UserSubscription
            {
                UserId = userId, PlanId = Guid.NewGuid(), Status = "active",
                StartDate = DateTime.UtcNow.AddDays(-1), EndDate = DateTime.UtcNow.AddDays(30)
            });
        if (ownsContent)
            db.Payments.Add(new Payment
            {
                UserId = userId, TenantId = tenantId, ContentId = contentId,
                Gateway = "razorpay", Amount = 100, Status = "success"
            });
        await db.SaveChangesAsync();
        return (db, tenantId, userId, contentId);
    }

    [Theory]
    [InlineData("free")]
    [InlineData("avod")]
    public async Task FreeAndAvod_AreAlwaysWatchable(string model)
    {
        var (db, tenantId, userId, contentId) = await SeedFullAsync(model);
        var sut = new EntitlementService(db);
        Assert.True(await sut.CanWatchAsync(userId, tenantId, contentId));
    }

    [Fact]
    public async Task Svod_WithoutSubscription_IsDenied()
    {
        var (db, tenantId, userId, contentId) = await SeedFullAsync("svod", activeSub: false);
        var sut = new EntitlementService(db);
        Assert.False(await sut.CanWatchAsync(userId, tenantId, contentId));
    }

    [Fact]
    public async Task Svod_WithActiveSubscription_IsAllowed()
    {
        var (db, tenantId, userId, contentId) = await SeedFullAsync("svod", activeSub: true);
        var sut = new EntitlementService(db);
        Assert.True(await sut.CanWatchAsync(userId, tenantId, contentId));
    }

    [Fact]
    public async Task Tvod_WithPurchase_IsAllowed()
    {
        var (db, tenantId, userId, contentId) = await SeedFullAsync("tvod", ownsContent: true);
        var sut = new EntitlementService(db);
        Assert.True(await sut.CanWatchAsync(userId, tenantId, contentId));
    }

    [Fact]
    public async Task Tvod_WithoutPurchaseOrSub_IsDenied()
    {
        var (db, tenantId, userId, contentId) = await SeedFullAsync("tvod");
        var sut = new EntitlementService(db);
        Assert.False(await sut.CanWatchAsync(userId, tenantId, contentId));
    }

    [Fact]
    public async Task UnknownModel_FailsClosed()
    {
        var (db, tenantId, userId, contentId) = await SeedFullAsync("mystery");
        var sut = new EntitlementService(db);
        Assert.False(await sut.CanWatchAsync(userId, tenantId, contentId));
    }

    [Fact]
    public async Task ContentInAnotherTenant_IsNotFound()
    {
        var (db, _, userId, contentId) = await SeedFullAsync("free");
        var sut = new EntitlementService(db);
        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => sut.CanWatchAsync(userId, Guid.NewGuid(), contentId));
    }
}
