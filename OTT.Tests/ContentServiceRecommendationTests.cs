using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OTT.Application.Services;
using OTT.Domain.Entities;
using OTT.Infrastructure.Data;
using OTT.Infrastructure.Services;
using Xunit;

namespace OTT.Tests;

public class ContentServiceRecommendationTests
{
    private static OttDbContext NewDb() =>
        new(new DbContextOptionsBuilder<OttDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    // No-op stand-ins: recommendations don't touch the CDN, and the tests exercise the DB path
    // directly rather than the cached homepage shell.
    private sealed class NoCdn : ICloudFrontCdnService
    {
        public string GetSignedUrl(string s3Key, int expiryMinutes = 360) => s3Key;
        public string GetSignedHlsUrl(string contentId, string quality, int expiryMinutes = 360) => contentId;
        public string GetThumbnailUrl(string s3Key) => s3Key;
        public string GetPublicUrl(string s3Key) => s3Key;
    }

    private sealed class NoCache : IRedisCacheService
    {
        public Task<T?> GetAsync<T>(string key) where T : class => Task.FromResult<T?>(null);
        public Task SetAsync<T>(string key, T value, TimeSpan? expiry = null) where T : class => Task.CompletedTask;
        public Task RemoveAsync(string key) => Task.CompletedTask;
        public Task RemoveByPatternAsync(string pattern) => Task.CompletedTask;
        public Task<bool> ExistsAsync(string key) => Task.FromResult(false);
        public Task<long> IncrementAsync(string key, TimeSpan? expiry = null) => Task.FromResult(0L);
        public Task SetStringAsync(string key, string value, TimeSpan? expiry = null) => Task.CompletedTask;
        public Task<string?> GetStringAsync(string key) => Task.FromResult<string?>(null);
        public Task HashIncrementAsync(string key, string field, long value = 1) => Task.CompletedTask;
        public Task HashSetAsync(string key, string field, string value) => Task.CompletedTask;
        public Task<Dictionary<string, string>> HashGetAllAndClearAsync(string key) => Task.FromResult(new Dictionary<string, string>());
        public Task ListPushAsync(string key, string value) => Task.CompletedTask;
        public Task<List<string>> ListDrainAsync(string key, int max) => Task.FromResult(new List<string>());
        public Task<long> TryAcquireStreamSlotAsync(string key, string member, int maxConcurrent, TimeSpan ttl) => Task.FromResult(1L);
        public Task RenewStreamSlotAsync(string key, string member, TimeSpan ttl) => Task.CompletedTask;
        public Task ReleaseStreamSlotAsync(string key, string member) => Task.CompletedTask;
    }

    private static ContentService NewService(OttDbContext db) =>
        new(db, new NoCdn(), new NoCache(), NullLogger<ContentService>.Instance);

    private static Content MakeContent(Guid tenantId, Guid genreId, string title, decimal rating = 5, int views = 0, bool trending = false) => new()
    {
        Id = Guid.NewGuid(), TenantId = tenantId, Title = title, Type = "movie", Status = "published",
        AverageRating = rating, ViewCount = views, IsTrending = trending,
        ContentGenres = new List<ContentGenre> { new() { GenreId = genreId } }
    };

    [Fact]
    public async Task ColdStart_NoHistory_ReturnsTrending()
    {
        var db = NewDb();
        var tenantId = Guid.NewGuid();
        var genreId = Guid.NewGuid();
        var trendingContent = MakeContent(tenantId, genreId, "Trending Movie", views: 100, trending: true);
        db.Contents.Add(trendingContent);
        db.Genres.Add(new Genre { Id = genreId, TenantId = tenantId, Name = "Action" });
        await db.SaveChangesAsync();

        var result = await NewService(db).GetRecommendationsAsync(Guid.NewGuid(), tenantId);

        Assert.Single(result);
        Assert.Equal("Trending Movie", result[0].Title);
    }

    [Fact]
    public async Task CompletedRecentWatch_PrioritizesMatchingGenreOverUnrelatedHighRating()
    {
        var db = NewDb();
        var tenantId = Guid.NewGuid();
        var profileId = Guid.NewGuid();
        var actionGenre = Guid.NewGuid();
        var dramaGenre = Guid.NewGuid();

        var watchedContent = MakeContent(tenantId, actionGenre, "Watched Action Movie");
        var recommendedAction = MakeContent(tenantId, actionGenre, "Another Action Movie", rating: 6);
        var unrelatedDrama = MakeContent(tenantId, dramaGenre, "Unrelated Drama", rating: 9);

        db.Contents.AddRange(watchedContent, recommendedAction, unrelatedDrama);
        db.WatchHistories.Add(new WatchHistory
        {
            ProfileId = profileId, ContentId = watchedContent.Id,
            IsCompleted = true, PositionSeconds = 5400, TotalSeconds = 5400,
            LastWatchedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var result = await NewService(db).GetRecommendationsAsync(profileId, tenantId);

        Assert.Contains(result, c => c.Title == "Another Action Movie");
        Assert.DoesNotContain(result, c => c.Title == "Watched Action Movie"); // already watched, excluded
        Assert.DoesNotContain(result, c => c.Title == "Unrelated Drama"); // higher rated but no genre affinity
    }

    [Fact]
    public async Task LowRating_DampensGenre_FallsBackToColdStartWhenNetNegative()
    {
        var db = NewDb();
        var tenantId = Guid.NewGuid();
        var profileId = Guid.NewGuid();
        var horrorGenre = Guid.NewGuid();

        var dislikedContent = MakeContent(tenantId, horrorGenre, "Disliked Horror Movie");
        var trending = MakeContent(tenantId, Guid.NewGuid(), "Trending Fallback", views: 50, trending: true);
        db.Contents.AddRange(dislikedContent, trending);
        // A brief, uncompleted watch (weak positive signal) combined with a low rating (strong
        // negative signal) should net negative for the genre, so it's excluded from matches.
        db.WatchHistories.Add(new WatchHistory
        {
            ProfileId = profileId, ContentId = dislikedContent.Id,
            IsCompleted = false, PositionSeconds = 30, TotalSeconds = 5400,
            LastWatchedAt = DateTime.UtcNow
        });
        db.UserRatings.Add(new UserRating { ProfileId = profileId, ContentId = dislikedContent.Id, Rating = 1 });
        await db.SaveChangesAsync();

        var result = await NewService(db).GetRecommendationsAsync(profileId, tenantId);

        Assert.Contains(result, c => c.Title == "Trending Fallback");
    }

    [Fact]
    public async Task SparseGenreMatches_PadsOutWithTrending()
    {
        var db = NewDb();
        var tenantId = Guid.NewGuid();
        var profileId = Guid.NewGuid();
        var nicheGenre = Guid.NewGuid();

        var watched = MakeContent(tenantId, nicheGenre, "Watched Niche Film");
        var onlyMatch = MakeContent(tenantId, nicheGenre, "Only Niche Match");
        db.Contents.AddRange(watched, onlyMatch);
        for (int i = 0; i < 5; i++)
            db.Contents.Add(MakeContent(tenantId, Guid.NewGuid(), $"Trending {i}", views: 100 - i, trending: true));

        db.WatchHistories.Add(new WatchHistory
        {
            ProfileId = profileId, ContentId = watched.Id,
            IsCompleted = true, PositionSeconds = 3600, TotalSeconds = 3600,
            LastWatchedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var result = await NewService(db).GetRecommendationsAsync(profileId, tenantId);

        Assert.Contains(result, c => c.Title == "Only Niche Match");
        Assert.True(result.Count(c => c.Title.StartsWith("Trending")) == 5, "should pad remaining slots with trending content");
    }
}
