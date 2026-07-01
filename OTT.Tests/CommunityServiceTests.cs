using Microsoft.EntityFrameworkCore;
using OTT.Application.DTOs;
using OTT.Application.Services;
using OTT.Domain.Entities;
using OTT.Infrastructure.Data;
using Xunit;

namespace OTT.Tests;

public class CommunityServiceTests
{
    private static OttDbContext NewDb() =>
        new(new DbContextOptionsBuilder<OttDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    [Fact]
    public async Task Upvote_TogglesAndUpdatesCount()
    {
        var db = NewDb();
        var (tenant, user) = (Guid.NewGuid(), Guid.NewGuid());
        var sut = new CommunityService(db);
        var s = await sut.CreateSuggestionAsync(tenant, user, new CreateSuggestionDto { Title = "Add show X" });

        var afterUp = await sut.ToggleUpvoteAsync(tenant, user, s.Id);
        Assert.True(afterUp.HasVoted);
        Assert.Equal(1, afterUp.UpvoteCount);

        var afterDown = await sut.ToggleUpvoteAsync(tenant, user, s.Id);
        Assert.False(afterDown.HasVoted);
        Assert.Equal(0, afterDown.UpvoteCount);
    }

    [Fact]
    public async Task Poll_OneVotePerUser_ChangingOptionDoesNotDoubleCount()
    {
        var db = NewDb();
        var (tenant, admin, user) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var sut = new CommunityService(db);
        var poll = await sut.CreatePollAsync(tenant, admin, new CreatePollDto
        {
            Question = "Which next?",
            Options = new List<string> { "A", "B" }
        });
        var optA = poll.Options[0].Id;
        var optB = poll.Options[1].Id;

        await sut.VoteAsync(tenant, user, poll.Id, optA);
        var afterChange = await sut.VoteAsync(tenant, user, poll.Id, optB);

        Assert.Equal(1, afterChange.TotalVotes);               // still one vote total
        Assert.Equal(optB, afterChange.MyOptionId);
        Assert.Equal(0, afterChange.Options.First(o => o.Id == optA).VoteCount);
        Assert.Equal(1, afterChange.Options.First(o => o.Id == optB).VoteCount);
    }

    [Fact]
    public async Task Vote_OnClosedPoll_IsRejected()
    {
        var db = NewDb();
        var (tenant, admin, user) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var sut = new CommunityService(db);
        var poll = await sut.CreatePollAsync(tenant, admin, new CreatePollDto
        {
            Question = "Closed?",
            Options = new List<string> { "A", "B" },
            EndsAt = DateTime.UtcNow.AddMinutes(-1) // already ended
        });
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => sut.VoteAsync(tenant, user, poll.Id, poll.Options[0].Id));
    }

    [Fact]
    public async Task PromoteSuggestion_CreatesPoll_AndMarksSuggestionPromoted()
    {
        var db = NewDb();
        var (tenant, admin) = (Guid.NewGuid(), Guid.NewGuid());
        var sut = new CommunityService(db);
        var s = await sut.CreateSuggestionAsync(tenant, admin, new CreateSuggestionDto { Title = "Add anime Y" });

        var poll = await sut.PromoteSuggestionAsync(tenant, admin, s.Id, new CreatePollDto { Question = "", Options = new List<string>() });

        Assert.True(poll.Options.Count >= 2);                  // defaulted options
        var reloaded = await db.Suggestions.FindAsync(s.Id);
        Assert.Equal("promoted", reloaded!.Status);
    }

    [Fact]
    public async Task Upvote_OnAnotherTenantsSuggestion_IsNotFound()
    {
        var db = NewDb();
        var sut = new CommunityService(db);
        var s = await sut.CreateSuggestionAsync(Guid.NewGuid(), Guid.NewGuid(), new CreateSuggestionDto { Title = "X" });
        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => sut.ToggleUpvoteAsync(Guid.NewGuid(), Guid.NewGuid(), s.Id));
    }
}
