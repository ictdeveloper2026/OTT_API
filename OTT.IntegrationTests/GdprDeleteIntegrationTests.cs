using Microsoft.EntityFrameworkCore;
using OTT.Application.Services;
using OTT.Domain.Entities;
using Xunit;

namespace OTT.IntegrationTests;

/// <summary>
/// GDPR erasure uses ExecuteDeleteAsync, which the InMemory provider doesn't support — so it's
/// verified here against real SQL Server.
/// </summary>
[Collection("containers")]
public class GdprDeleteIntegrationTests
{
    private readonly ContainerFixture _fx;
    public GdprDeleteIntegrationTests(ContainerFixture fx) => _fx = fx;

    [Fact]
    public async Task DeleteAccount_AnonymizesUser_AndRemovesBehaviouralData()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var profileId = Guid.NewGuid();

        await using (var seed = _fx.NewDbContext())
        {
            seed.Users.Add(new User { Id = userId, TenantId = tenantId, Email = "erase-me@example.com", FirstName = "Erase", Phone = "+15550001111" });
            seed.UserProfiles.Add(new UserProfile { Id = profileId, UserId = userId, Name = "P" });
            seed.Watchlists.Add(new Watchlist { ProfileId = profileId, ContentId = Guid.NewGuid() });
            seed.DeviceTokens.Add(new DeviceToken { UserId = userId, Token = "tok", Platform = "android" });
            await seed.SaveChangesAsync();
        }

        await using (var act = _fx.NewDbContext())
        {
            var ok = await new GdprService(act).DeleteAccountAsync(userId, tenantId, "127.0.0.1");
            Assert.True(ok);
        }

        await using (var verify = _fx.NewDbContext())
        {
            var user = await verify.Users.IgnoreQueryFilters().SingleAsync(u => u.Id == userId);
            Assert.True(user.IsDeleted);
            Assert.Null(user.Phone);
            Assert.Null(user.FirstName);
            Assert.DoesNotContain("erase-me@example.com", user.Email);
            Assert.StartsWith("deleted+", user.Email);

            Assert.False(await verify.UserProfiles.AnyAsync(p => p.UserId == userId));
            Assert.False(await verify.Watchlists.AnyAsync(w => w.ProfileId == profileId));
            Assert.False(await verify.DeviceTokens.AnyAsync(d => d.UserId == userId));
            Assert.True(await verify.AuditLogs.AnyAsync(a => a.ActorUserId == userId && a.Action == "DELETE"));
        }
    }
}
