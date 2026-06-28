using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using OTT.Application.Services;
using OTT.Infrastructure.Data;
using OTT.Infrastructure.Services;
using Xunit;

namespace OTT.Tests;

public class OutboxServiceTests
{
    private static OttDbContext NewDb() =>
        new(new DbContextOptionsBuilder<OttDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    // No SendGrid/Firebase configured → senders treat the message as delivered (drop), which
    // lets us exercise the enqueue → dispatch → mark-sent flow without external services.
    private sealed class NullSettings : IDynamicSettingsService
    {
        public Task<string?> GetAsync(Guid tenantId, string key, string? fallback = null) => Task.FromResult(fallback);
        public Task<bool> GetBoolAsync(Guid tenantId, string key, bool fallback = false) => Task.FromResult(fallback);
        public Task<Dictionary<string, string?>> GetAllAsync(Guid tenantId, bool publicOnly = false) => Task.FromResult(new Dictionary<string, string?>());
        public Task SetAsync(Guid tenantId, string key, string? value, bool isPublic = false) => Task.CompletedTask;
        public Task SetManyAsync(Guid tenantId, IReadOnlyDictionary<string, string?> values, bool isPublic = false) => Task.CompletedTask;
        public void Invalidate(Guid tenantId) { }
    }

    private static OutboxService NewSut(OttDbContext db)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection().Build(); // no SendGrid key
        return new OutboxService(db, config, new NullSettings(), NullLogger<OutboxService>.Instance);
    }

    [Fact]
    public async Task EnqueueEmail_WritesPendingRow()
    {
        var db = NewDb();
        var sut = NewSut(db);

        await sut.EnqueueEmailAsync("a@b.com", "A", "Hi", "<p>hi</p>");

        var row = await db.OutboxMessages.SingleAsync();
        Assert.Equal("email", row.Channel);
        Assert.Equal("pending", row.Status);
        Assert.Equal(0, row.Attempts);
    }

    [Fact]
    public async Task Dispatch_MarksEmailSent_WhenNoProviderConfigured()
    {
        var db = NewDb();
        var sut = NewSut(db);
        await sut.EnqueueEmailAsync("a@b.com", "A", "Hi", "<p>hi</p>");

        var sent = await sut.DispatchPendingAsync();

        Assert.Equal(1, sent);
        var row = await db.OutboxMessages.SingleAsync();
        Assert.Equal("sent", row.Status);
        Assert.NotNull(row.SentAt);
    }

    [Fact]
    public async Task EnqueuePush_WithNoUsers_IsNoOp()
    {
        var db = NewDb();
        var sut = NewSut(db);

        await sut.EnqueuePushAsync(Array.Empty<Guid>(), "T", "B", null);

        Assert.False(await db.OutboxMessages.AnyAsync());
    }

    [Fact]
    public async Task Dispatch_OnlyPicksUpDueMessages()
    {
        var db = NewDb();
        var sut = NewSut(db);
        await sut.EnqueueEmailAsync("a@b.com", "A", "Hi", "<p>hi</p>");

        // A future-scheduled (retrying) message should be skipped this pass.
        db.OutboxMessages.Add(new OTT.Domain.Entities.OutboxMessage
        {
            Channel = "email", Payload = "{}", Status = "pending",
            Attempts = 1, NextAttemptAt = DateTime.UtcNow.AddMinutes(30)
        });
        await db.SaveChangesAsync();

        var sent = await sut.DispatchPendingAsync();

        Assert.Equal(1, sent); // only the due one
        Assert.Equal(1, await db.OutboxMessages.CountAsync(m => m.Status == "pending"));
    }
}
