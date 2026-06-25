using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OTT.Application.Services;
using OTT.Domain.Entities;
using OTT.Infrastructure.Data;
using Xunit;

namespace OTT.Tests;

public class GdprExportTests
{
    private static OttDbContext NewDb() =>
        new(new DbContextOptionsBuilder<OttDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    [Fact]
    public async Task Export_IncludesAccountProfilesAndPayments()
    {
        var db = NewDb();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var profileId = Guid.NewGuid();

        db.Users.Add(new User { Id = userId, TenantId = tenantId, Email = "jane@example.com", FirstName = "Jane" });
        db.UserProfiles.Add(new UserProfile { Id = profileId, UserId = userId, Name = "Jane P" });
        db.Payments.Add(new Payment { UserId = userId, TenantId = tenantId, Gateway = "razorpay", Amount = 499, Status = "success" });
        db.UserRatings.Add(new UserRating { ProfileId = profileId, ContentId = Guid.NewGuid(), Rating = 8 });
        await db.SaveChangesAsync();

        var export = await new GdprService(db).ExportAsync(userId, tenantId);
        var json = JsonSerializer.Serialize(export);

        Assert.Contains("jane@example.com", json);
        Assert.Contains("Jane P", json);   // profile
        Assert.Contains("razorpay", json); // payment
    }

    [Fact]
    public async Task Export_OtherTenantUser_IsNotFound()
    {
        var db = NewDb();
        var userId = Guid.NewGuid();
        db.Users.Add(new User { Id = userId, TenantId = Guid.NewGuid(), Email = "x@y.com" });
        await db.SaveChangesAsync();

        var sut = new GdprService(db);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => sut.ExportAsync(userId, Guid.NewGuid()));
    }
}
