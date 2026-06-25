using OTT.Application.Services;
using OTT.Domain.Entities;
using Xunit;

namespace OTT.IntegrationTests;

/// <summary>
/// Exercises the entitlement query against a real SQL Server — proving the migration-created schema
/// and the LINQ→T-SQL translation actually work (the InMemory provider can hide both).
/// </summary>
[Collection("containers")]
public class EntitlementIntegrationTests
{
    private readonly ContainerFixture _fx;
    public EntitlementIntegrationTests(ContainerFixture fx) => _fx = fx;

    [Fact]
    public async Task Svod_RequiresActiveSubscription_OnRealSqlServer()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var contentId = Guid.NewGuid();

        await using (var seed = _fx.NewDbContext())
        {
            seed.Contents.Add(new Content
            {
                Id = contentId, TenantId = tenantId, Title = "SVOD Title", Type = "movie",
                Status = "published", MonetizationModel = "svod"
            });
            await seed.SaveChangesAsync();
        }

        // No subscription yet → denied.
        await using (var db = _fx.NewDbContext())
        {
            var sut = new EntitlementService(db);
            Assert.False(await sut.CanWatchAsync(userId, tenantId, contentId));
        }

        // Grant an active subscription → allowed.
        await using (var grant = _fx.NewDbContext())
        {
            var planId = Guid.NewGuid();
            grant.SubscriptionPlans.Add(new SubscriptionPlan { Id = planId, TenantId = tenantId, Name = "Premium", MaxStreams = 2, IsActive = true });
            grant.UserSubscriptions.Add(new UserSubscription
            {
                UserId = userId, PlanId = planId, Status = "active",
                StartDate = DateTime.UtcNow.AddDays(-1), EndDate = DateTime.UtcNow.AddDays(30)
            });
            await grant.SaveChangesAsync();
        }

        await using (var db = _fx.NewDbContext())
        {
            var sut = new EntitlementService(db);
            Assert.True(await sut.CanWatchAsync(userId, tenantId, contentId));
        }
    }
}
