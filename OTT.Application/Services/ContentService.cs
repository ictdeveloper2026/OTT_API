using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OTT.Application.DTOs;
using OTT.Domain.Entities;
using OTT.Infrastructure.Data;
using OTT.Infrastructure.Services;

namespace OTT.Application.Services;

public interface IContentService
{
    Task<HomePageDto> GetHomePageAsync(Guid tenantId, Guid? profileId = null);
    Task<ContentDetailDto> GetContentDetailAsync(Guid contentId, Guid tenantId, Guid? profileId = null);
    Task<PagedResultDto<ContentListItemDto>> SearchAsync(SearchRequestDto request, Guid tenantId);
    Task<PagedResultDto<ContentListItemDto>> GetByGenreAsync(Guid genreId, Guid tenantId, int page, int pageSize);
    Task<StreamUrlsDto> GetStreamUrlsAsync(Guid contentId, Guid tenantId, Guid? episodeId = null);
    Task UpdateWatchProgressAsync(Guid profileId, Guid contentId, int positionSeconds, int durationSeconds, Guid? episodeId = null);
    Task<bool> AddToWatchlistAsync(Guid profileId, Guid contentId);
    Task<bool> RemoveFromWatchlistAsync(Guid profileId, Guid contentId);
    Task<bool> RateContentAsync(Guid profileId, Guid contentId, decimal rating);
    Task<List<ContentListItemDto>> GetContinueWatchingAsync(Guid profileId);
    Task<List<ContentListItemDto>> GetWatchlistAsync(Guid profileId, Guid tenantId);
    Task<List<ContentListItemDto>> GetRecommendationsAsync(Guid profileId, Guid tenantId);
    Task<List<ContentListItemDto>> GetFeaturedAsync(Guid tenantId);
    Task<PagedResultDto<ContentListItemDto>> GetTrendingAsync(Guid tenantId, int page, int pageSize);
    Task<PagedResultDto<ContentListItemDto>> GetNewReleasesAsync(Guid tenantId, int page, int pageSize);
    Task<List<ContentListItemDto>> GetRelatedAsync(Guid contentId, Guid tenantId);
    Task<List<SeasonDto>> GetSeriesEpisodesAsync(Guid contentId, Guid tenantId, int? season);
    Task<List<ContentListItemDto>> GetWatchHistoryAsync(Guid profileId);
    // Admin
    Task<ContentDetailDto> CreateContentAsync(CreateContentDto dto, Guid tenantId);
    Task<ContentDetailDto> UpdateContentAsync(Guid contentId, Guid tenantId, CreateContentDto dto);
    Task<bool> DeleteContentAsync(Guid contentId, Guid tenantId);
    Task<bool> PublishContentAsync(Guid contentId, Guid tenantId);
    Task<PagedResultDto<ContentListItemDto>> GetAdminContentAsync(Guid tenantId, int page, int pageSize, string? search = null, string? type = null);
}

public class ContentService : IContentService
{
    private readonly OttDbContext _db;
    private readonly ICloudFrontCdnService _cdn;
    private readonly IRedisCacheService _cache;
    private readonly ILogger<ContentService> _logger;

    public ContentService(OttDbContext db, ICloudFrontCdnService cdn, IRedisCacheService cache, ILogger<ContentService> logger)
    {
        _db = db;
        _cdn = cdn;
        _cache = cache;
        _logger = logger;
    }

    public async Task<HomePageDto> GetHomePageAsync(Guid tenantId, Guid? profileId = null)
    {
        // Cache the tenant-shared SHELL under a versioned key (one entry per tenant, not per
        // profile — avoids 150k-entry key explosion). Continue-watching is per-profile and must
        // be live, so it's fetched separately below and never cached.
        // Default "0" so it never collides with the first INCR (which yields 1) on invalidation.
        var version = await _cache.GetStringAsync(HomepageVersionKey(tenantId)) ?? "0";
        var cacheKey = $"homepage:{tenantId}:v{version}";

        var shell = await _cache.GetAsync<HomePageDto>(cacheKey);
        if (shell != null)
        {
            shell.ContinueWatching = await GetContinueWatchingShellAsync(profileId);
            return shell;
        }

        var branding = await _db.BrandingConfigs.FirstOrDefaultAsync(b => b.TenantId == tenantId);
        var banners = await _db.Banners
            .Where(b => b.TenantId == tenantId && b.IsActive)
            .OrderBy(b => b.SortOrder)
            .ToListAsync();

        var rows = await _db.ContentRows
            .AsNoTracking()
            .Include(r => r.Items)
            .Where(r => r.TenantId == tenantId && r.IsActive)
            .OrderBy(r => r.SortOrder)
            .ToListAsync();

        var liveStreams = await _db.LiveStreams
            .Where(l => l.TenantId == tenantId && l.Status == "live")
            .Take(5)
            .ToListAsync();

        var contentIds = rows.SelectMany(r => r.Items.Select(i => i.ContentId))
            .Concat(banners.Where(b => b.ContentId.HasValue).Select(b => b.ContentId!.Value))
            .Distinct().ToList();

        var contents = await _db.Contents
            .AsNoTracking()
            .Include(c => c.ContentGenres).ThenInclude(cg => cg.Genre)
            .Where(c => contentIds.Contains(c.Id) && c.Status == "published")
            .ToDictionaryAsync(c => c.Id);

        var dto = new HomePageDto
        {
            Branding = MapBranding(branding),
            Banners = banners.Select(b => MapBanner(b, contents)).ToList(),
            Rows = rows.Select(r => MapContentRow(r, contents)).ToList(),
            ContinueWatching = [], // shared shell holds none; filled per-profile after caching
            LiveNow = liveStreams.Select(MapLiveStreamList).ToList()
        };

        await _cache.SetAsync(cacheKey, dto, TimeSpan.FromMinutes(5));

        // Attach the live, per-profile continue-watching to the response (not to the cached shell).
        dto.ContinueWatching = await GetContinueWatchingShellAsync(profileId);
        return dto;
    }

    // Per-profile continue-watching used by the homepage. Kept out of the shared homepage cache.
    private async Task<List<ContentListItemDto>> GetContinueWatchingShellAsync(Guid? profileId)
    {
        if (!profileId.HasValue) return [];
        var history = await _db.WatchHistories
            .AsNoTracking()
            .Include(w => w.Content).ThenInclude(c => c.ContentGenres).ThenInclude(cg => cg.Genre)
            .Where(w => w.ProfileId == profileId && !w.IsCompleted && w.PositionSeconds > 30)
            .OrderByDescending(w => w.LastWatchedAt)
            .Take(10)
            .ToListAsync();
        return history.Select(h => MapContentListItem(h.Content)).ToList();
    }

    // Versioned-key invalidation: bump the tenant's homepage version so the next request rebuilds
    // the shell. Old version keys fall out via their TTL — no full-keyspace Keys() scan.
    private static string HomepageVersionKey(Guid tenantId) => $"homepage:ver:{tenantId}";
    private Task InvalidateHomepageAsync(Guid tenantId) => _cache.IncrementAsync(HomepageVersionKey(tenantId));

    public async Task<ContentDetailDto> GetContentDetailAsync(Guid contentId, Guid tenantId, Guid? profileId = null)
    {
        // AsSplitQuery: four collection includes in one query would otherwise produce a
        // cartesian row explosion (seasons×episodes×genres×casts×tags).
        var content = await _db.Contents
            .AsNoTracking()
            .Include(c => c.Seasons).ThenInclude(s => s.Episodes)
            .Include(c => c.ContentGenres).ThenInclude(cg => cg.Genre)
            .Include(c => c.ContentCasts)
            .Include(c => c.ContentTags).ThenInclude(ct => ct.Tag)
            .AsSplitQuery()
            .FirstOrDefaultAsync(c => c.Id == contentId && c.TenantId == tenantId && c.Status == "published")
            ?? throw new KeyNotFoundException("Content not found");

        var dto = new ContentDetailDto
        {
            Id = content.Id,
            Title = content.Title,
            Description = content.Description,
            Type = content.Type,
            ThumbnailUrl = content.ThumbnailUrl,
            PosterUrl = content.PosterUrl,
            BannerUrl = content.BannerUrl,
            TrailerUrl = content.TrailerUrl,
            ReleaseYear = content.ReleaseYear,
            AgeRating = content.AgeRating,
            AverageRating = content.AverageRating,
            DurationSeconds = content.DurationSeconds,
            MonetizationModel = content.MonetizationModel,
            Price = content.Price,
            DirectorName = content.DirectorName,
            IsFeatured = content.IsFeatured,
            IsTrending = content.IsTrending,
            Genres = content.ContentGenres.Select(cg => cg.Genre.Name).ToList(),
            Tags = content.ContentTags.Select(ct => ct.Tag.Name).ToList(),
            Cast = content.ContentCasts.Select(c => new CastDto
            {
                Name = c.ActorName,
                Character = c.CharacterName,
                PhotoUrl = c.PhotoUrl,
                Role = c.Role
            }).ToList(),
            Seasons = content.Seasons.OrderBy(s => s.SeasonNumber).Select(s => new SeasonDto
            {
                Id = s.Id,
                SeasonNumber = s.SeasonNumber,
                Title = s.Title,
                Episodes = s.Episodes.OrderBy(e => e.EpisodeNumber).Select(e => new EpisodeDto
                {
                    Id = e.Id,
                    EpisodeNumber = e.EpisodeNumber,
                    Title = e.Title,
                    Description = e.Description,
                    ThumbnailUrl = e.ThumbnailUrl,
                    DurationSeconds = e.DurationSeconds
                }).ToList()
            }).ToList()
        };

        if (content.Type == "movie" || content.Type == "short")
            dto.StreamUrls = await GetStreamUrlsInternalAsync(content);

        if (profileId.HasValue)
        {
            var progress = await _db.WatchHistories
                .FirstOrDefaultAsync(w => w.ProfileId == profileId && w.ContentId == contentId);
            if (progress != null)
                dto.WatchProgress = MapWatchProgress(progress, content.DurationSeconds ?? 0);

            dto.IsInWatchlist = await _db.Watchlists
                .AnyAsync(w => w.ProfileId == profileId && w.ContentId == contentId);

            var rating = await _db.UserRatings
                .FirstOrDefaultAsync(r => r.ProfileId == profileId && r.ContentId == contentId);
            dto.UserRating = rating?.Rating;
        }

        // Related content
        var genreIds = content.ContentGenres.Select(cg => cg.GenreId).ToList();
        dto.Related = await _db.Contents
            .Where(c => c.TenantId == tenantId && c.Id != contentId && c.Status == "published"
                && c.ContentGenres.Any(cg => genreIds.Contains(cg.GenreId)))
            .OrderByDescending(c => c.ViewCount)
            .Take(12)
            .Select(ListItemProjection)
            .ToListAsync();

        // Increment view count via a write-behind buffer — no SQL write on this hot read path.
        // A recurring job (WriteBehindFlushJob) folds these into Contents.ViewCount.
        await _cache.HashIncrementAsync("views:pending", contentId.ToString());

        // Emit a granular content_view event into the append-only analytics buffer. The flush
        // job batch-inserts these into AnalyticsEvents, which AnalyticsJob turns into UniqueViewers.
        await _cache.ListPushAsync("analytics:pending", System.Text.Json.JsonSerializer.Serialize(new AnalyticsEventBuffer
        {
            TenantId = tenantId,
            ViewerId = profileId,
            ContentId = contentId,
            EventType = "content_view",
            CreatedAt = DateTime.UtcNow
        }));

        return dto;
    }

    public async Task<PagedResultDto<ContentListItemDto>> SearchAsync(SearchRequestDto request, Guid tenantId)
    {
        var query = _db.Contents
            .AsNoTracking()
            .Include(c => c.ContentGenres).ThenInclude(cg => cg.Genre)
            .Where(c => c.TenantId == tenantId && c.Status == "published");

        if (!string.IsNullOrWhiteSpace(request.Query))
        {
            var q = request.Query.ToLower();
            query = query.Where(c =>
                c.Title.ToLower().Contains(q) ||
                (c.Description != null && c.Description.ToLower().Contains(q)) ||
                (c.DirectorName != null && c.DirectorName.ToLower().Contains(q)));
        }

        if (!string.IsNullOrEmpty(request.Type))
            query = query.Where(c => c.Type == request.Type);

        if (request.Genres?.Any() == true)
            query = query.Where(c => c.ContentGenres.Any(cg => request.Genres.Contains(cg.Genre.Name)));

        if (!string.IsNullOrEmpty(request.Language))
            query = query.Where(c => c.Language == request.Language);

        if (!string.IsNullOrEmpty(request.AgeRating))
            query = query.Where(c => c.AgeRating == request.AgeRating);

        if (request.ReleaseYear.HasValue)
            query = query.Where(c => c.ReleaseYear == request.ReleaseYear);

        query = request.SortBy switch
        {
            "newest" => query.OrderByDescending(c => c.ReleaseYear).ThenByDescending(c => c.CreatedAt),
            "rating" => query.OrderByDescending(c => c.AverageRating),
            "views" => query.OrderByDescending(c => c.ViewCount),
            "az" => query.OrderBy(c => c.Title),
            _ => query.OrderByDescending(c => c.ViewCount)
        };

        var total = await query.CountAsync();
        var items = await query
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(ListItemProjection)
            .ToListAsync();

        return new PagedResultDto<ContentListItemDto>
        {
            Items = items,
            TotalCount = total,
            Page = request.Page,
            PageSize = request.PageSize
        };
    }

    public async Task<PagedResultDto<ContentListItemDto>> GetByGenreAsync(Guid genreId, Guid tenantId, int page, int pageSize)
    {
        var query = _db.Contents
            .AsNoTracking()
            .Where(c => c.TenantId == tenantId && c.Status == "published"
                && c.ContentGenres.Any(cg => cg.GenreId == genreId));

        var total = await query.CountAsync();
        var items = await query
            .OrderByDescending(c => c.IsFeatured)
            .ThenByDescending(c => c.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(ListItemProjection)
            .ToListAsync();

        return new PagedResultDto<ContentListItemDto> { Items = items, TotalCount = total, Page = page, PageSize = pageSize };
    }

    public async Task<List<ContentListItemDto>> GetFeaturedAsync(Guid tenantId)
    {
        return await _db.Contents
            .AsNoTracking()
            .Where(c => c.TenantId == tenantId && c.Status == "published" && c.IsFeatured)
            .OrderByDescending(c => c.CreatedAt)
            .Take(20)
            .Select(ListItemProjection)
            .ToListAsync();
    }

    public async Task<PagedResultDto<ContentListItemDto>> GetTrendingAsync(Guid tenantId, int page, int pageSize)
    {
        var query = _db.Contents
            .AsNoTracking()
            .Where(c => c.TenantId == tenantId && c.Status == "published");

        var total = await query.CountAsync();
        var items = await query
            .OrderByDescending(c => c.IsTrending).ThenByDescending(c => c.ViewCount)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(ListItemProjection)
            .ToListAsync();

        return new PagedResultDto<ContentListItemDto>
        {
            Items = items,
            TotalCount = total, Page = page, PageSize = pageSize
        };
    }

    public async Task<PagedResultDto<ContentListItemDto>> GetNewReleasesAsync(Guid tenantId, int page, int pageSize)
    {
        var query = _db.Contents
            .AsNoTracking()
            .Where(c => c.TenantId == tenantId && c.Status == "published");

        var total = await query.CountAsync();
        var items = await query
            .OrderByDescending(c => c.PublishedAt).ThenByDescending(c => c.CreatedAt)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(ListItemProjection)
            .ToListAsync();

        return new PagedResultDto<ContentListItemDto>
        {
            Items = items,
            TotalCount = total, Page = page, PageSize = pageSize
        };
    }

    public async Task<List<ContentListItemDto>> GetRelatedAsync(Guid contentId, Guid tenantId)
    {
        var genreIds = await _db.ContentGenres
            .Where(cg => cg.ContentId == contentId)
            .Select(cg => cg.GenreId)
            .ToListAsync();

        return await _db.Contents
            .AsNoTracking()
            .Where(c => c.TenantId == tenantId && c.Status == "published" && c.Id != contentId
                && c.ContentGenres.Any(cg => genreIds.Contains(cg.GenreId)))
            .OrderByDescending(c => c.AverageRating)
            .Take(12)
            .Select(ListItemProjection)
            .ToListAsync();
    }

    public async Task<List<SeasonDto>> GetSeriesEpisodesAsync(Guid contentId, Guid tenantId, int? season)
    {
        var detail = await GetContentDetailAsync(contentId, tenantId);
        var seasons = detail.Seasons;
        return season.HasValue
            ? seasons.Where(s => s.SeasonNumber == season.Value).ToList()
            : seasons;
    }

    public async Task<List<ContentListItemDto>> GetWatchHistoryAsync(Guid profileId)
    {
        var contents = await _db.WatchHistories
            .AsNoTracking()
            .Include(w => w.Content).ThenInclude(c => c.ContentGenres).ThenInclude(cg => cg.Genre)
            .Where(w => w.ProfileId == profileId)
            .OrderByDescending(w => w.LastWatchedAt)
            .Select(w => w.Content)
            .ToListAsync();
        return contents.Select(MapContentListItem).ToList();
    }

    public async Task<StreamUrlsDto> GetStreamUrlsAsync(Guid contentId, Guid tenantId, Guid? episodeId = null)
    {
        // Tenant-scoped: never resolve content/episodes belonging to another tenant.
        var content = await _db.Contents
            .FirstOrDefaultAsync(c => c.Id == contentId && c.TenantId == tenantId)
            ?? throw new KeyNotFoundException("Content not found");

        if (episodeId.HasValue)
        {
            var episode = await _db.Episodes
                .FirstOrDefaultAsync(e => e.Id == episodeId && e.ContentId == contentId)
                ?? throw new KeyNotFoundException("Episode not found");

            var asset = await _db.VideoAssets
                .FirstOrDefaultAsync(a => a.EpisodeId == episodeId && a.Status == "ready");

            var epDto = BuildStreamUrls(asset, episode.ExternalVideoUrl, episode.YoutubeId, episode.VimeoId);
            await PopulateTracksAsync(epDto, contentId);
            return epDto;
        }

        return await GetStreamUrlsInternalAsync(content);
    }

    public async Task UpdateWatchProgressAsync(Guid profileId, Guid contentId, int positionSeconds, int durationSeconds, Guid? episodeId = null)
    {
        // Write-behind: at ~3k writes/sec under load this must NOT hit SQL on every call.
        // Buffer the latest position per (profile, content, episode) in Redis; WriteBehindFlushJob
        // persists it to WatchHistories every minute. Duration comes from the client payload, so
        // there is no DB read on this path either.
        var field = $"{profileId}|{contentId}|{episodeId?.ToString() ?? "none"}";
        var value = $"{positionSeconds}:{durationSeconds}:{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
        await _cache.HashSetAsync("wh:pending", field, value);
    }

    public async Task<bool> AddToWatchlistAsync(Guid profileId, Guid contentId)
    {
        var exists = await _db.Watchlists.AnyAsync(w => w.ProfileId == profileId && w.ContentId == contentId);
        if (exists) return true;

        _db.Watchlists.Add(new Watchlist { ProfileId = profileId, ContentId = contentId, AddedAt = DateTime.UtcNow });
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> RemoveFromWatchlistAsync(Guid profileId, Guid contentId)
    {
        var item = await _db.Watchlists.FindAsync(profileId, contentId);
        if (item == null) return false;
        _db.Watchlists.Remove(item);
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> RateContentAsync(Guid profileId, Guid contentId, decimal rating)
    {
        var existing = await _db.UserRatings.FindAsync(profileId, contentId);
        if (existing == null)
            _db.UserRatings.Add(new UserRating { ProfileId = profileId, ContentId = contentId, Rating = rating, CreatedAt = DateTime.UtcNow });
        else
            existing.Rating = rating;
        // Persist the rating first so the AVG() below reflects this change.
        await _db.SaveChangesAsync();

        // Recompute the average in SQL (AVG) instead of materializing every rating row —
        // a single aggregate query that stays cheap on popular titles.
        var average = await _db.UserRatings
            .Where(r => r.ContentId == contentId)
            .AverageAsync(r => (decimal?)r.Rating) ?? rating;

        var content = await _db.Contents.FindAsync(contentId);
        if (content != null)
        {
            content.AverageRating = average;
            await _db.SaveChangesAsync();
        }
        return true;
    }

    public async Task<List<ContentListItemDto>> GetContinueWatchingAsync(Guid profileId)
    {
        return await _db.WatchHistories
            .AsNoTracking()
            .Include(w => w.Content).ThenInclude(c => c.ContentGenres).ThenInclude(cg => cg.Genre)
            .Where(w => w.ProfileId == profileId && !w.IsCompleted && w.PositionSeconds > 30)
            .OrderByDescending(w => w.LastWatchedAt)
            .Take(10)
            .Select(w => MapContentListItem(w.Content))
            .ToListAsync();
    }

    public async Task<List<ContentListItemDto>> GetWatchlistAsync(Guid profileId, Guid tenantId)
    {
        return await _db.Watchlists
            .AsNoTracking()
            .Include(w => w.Content).ThenInclude(c => c.ContentGenres).ThenInclude(cg => cg.Genre)
            .Where(w => w.ProfileId == profileId && w.Content.TenantId == tenantId)
            .OrderByDescending(w => w.AddedAt)
            .Select(w => MapContentListItem(w.Content))
            .ToListAsync();
    }

    public async Task<List<ContentListItemDto>> GetRecommendationsAsync(Guid profileId, Guid tenantId)
    {
        // Simple collaborative filtering: find genres user watches most
        var watchedGenres = await _db.WatchHistories
            .Where(w => w.ProfileId == profileId)
            .Join(_db.ContentGenres, w => w.ContentId, cg => cg.ContentId, (w, cg) => cg.GenreId)
            .GroupBy(g => g)
            .OrderByDescending(g => g.Count())
            .Select(g => g.Key)
            .Take(3)
            .ToListAsync();

        var watchedIds = await _db.WatchHistories.Where(w => w.ProfileId == profileId).Select(w => w.ContentId).ToListAsync();

        if (!watchedGenres.Any())
        {
            // Cold start: return trending
            return await _db.Contents
                .Include(c => c.ContentGenres).ThenInclude(cg => cg.Genre)
                .Where(c => c.TenantId == tenantId && c.Status == "published" && c.IsTrending)
                .OrderByDescending(c => c.ViewCount)
                .Take(20)
                .Select(ListItemProjection)
                .ToListAsync();
        }

        return await _db.Contents
            .Include(c => c.ContentGenres).ThenInclude(cg => cg.Genre)
            .Where(c => c.TenantId == tenantId && c.Status == "published"
                && !watchedIds.Contains(c.Id)
                && c.ContentGenres.Any(cg => watchedGenres.Contains(cg.GenreId)))
            .OrderByDescending(c => c.AverageRating)
            .Take(20)
            .Select(ListItemProjection)
            .ToListAsync();
    }

    // ── Admin ─────────────────────────────────────────────────────────────────

    public async Task<ContentDetailDto> CreateContentAsync(CreateContentDto dto, Guid tenantId)
    {
        var content = new Content
        {
            TenantId = tenantId,
            Title = dto.Title,
            Description = dto.Description,
            ShortDescription = dto.ShortDescription,
            Type = dto.Type,
            ThumbnailUrl = dto.ThumbnailUrl,
            PosterUrl = dto.PosterUrl,
            BannerUrl = dto.BannerUrl,
            TrailerUrl = dto.TrailerUrl,
            ReleaseYear = dto.ReleaseYear,
            Language = dto.Language,
            AgeRating = dto.AgeRating ?? "PG",
            MonetizationModel = dto.MonetizationModel,
            Price = dto.Price,
            DirectorName = dto.DirectorName,
            IsFeatured = dto.IsFeatured,
            IsTrending = dto.IsTrending,
            Status = "draft",
            YoutubeId = dto.YoutubeId,
            VimeoId = dto.VimeoId,
            HlsUrl = dto.HlsUrl,
            VideoSourceType = dto.VideoSourceType,
            CreatedAt = DateTime.UtcNow
        };

        _db.Contents.Add(content);

        foreach (var genreId in dto.GenreIds)
            _db.ContentGenres.Add(new ContentGenre { ContentId = content.Id, GenreId = genreId });

        foreach (var tag in dto.Tags)
        {
            var tagEntity = await _db.Tags.FirstOrDefaultAsync(t => t.Name == tag && t.TenantId == tenantId)
                ?? new Tag { TenantId = tenantId, Name = tag };
            if (tagEntity.Id == Guid.Empty) _db.Tags.Add(tagEntity);
            _db.ContentTags.Add(new ContentTag { ContentId = content.Id, TagId = tagEntity.Id });
        }

        await _db.SaveChangesAsync();
        await InvalidateHomepageAsync(tenantId);

        return await GetContentDetailAsync(content.Id, tenantId);
    }

    public async Task<ContentDetailDto> UpdateContentAsync(Guid contentId, Guid tenantId, CreateContentDto dto)
    {
        var content = await _db.Contents.FirstOrDefaultAsync(c => c.Id == contentId && c.TenantId == tenantId)
            ?? throw new KeyNotFoundException("Content not found");

        content.Title = dto.Title;
        content.Description = dto.Description;
        content.ShortDescription = dto.ShortDescription ?? content.ShortDescription;
        content.ThumbnailUrl = dto.ThumbnailUrl ?? content.ThumbnailUrl;
        content.PosterUrl = dto.PosterUrl ?? content.PosterUrl;
        content.BannerUrl = dto.BannerUrl ?? content.BannerUrl;
        content.TrailerUrl = dto.TrailerUrl ?? content.TrailerUrl;
        content.ReleaseYear = dto.ReleaseYear ?? content.ReleaseYear;
        content.Language = dto.Language ?? content.Language;
        content.AgeRating = dto.AgeRating ?? content.AgeRating;
        content.MonetizationModel = dto.MonetizationModel;
        content.Price = dto.Price ?? content.Price;
        content.IsFeatured = dto.IsFeatured;
        content.IsTrending = dto.IsTrending;
        content.YoutubeId = dto.YoutubeId ?? content.YoutubeId;
        content.VimeoId = dto.VimeoId ?? content.VimeoId;
        content.HlsUrl = dto.HlsUrl ?? content.HlsUrl;

        // Update genres
        var existing = await _db.ContentGenres.Where(cg => cg.ContentId == contentId).ToListAsync();
        _db.ContentGenres.RemoveRange(existing);
        foreach (var genreId in dto.GenreIds)
            _db.ContentGenres.Add(new ContentGenre { ContentId = contentId, GenreId = genreId });

        await _db.SaveChangesAsync();
        await InvalidateHomepageAsync(content.TenantId);

        return await GetContentDetailAsync(contentId, content.TenantId);
    }

    public async Task<bool> DeleteContentAsync(Guid contentId, Guid tenantId)
    {
        var content = await _db.Contents.FirstOrDefaultAsync(c => c.Id == contentId && c.TenantId == tenantId);
        if (content == null) return false;
        content.IsDeleted = true;
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> PublishContentAsync(Guid contentId, Guid tenantId)
    {
        var content = await _db.Contents.FirstOrDefaultAsync(c => c.Id == contentId && c.TenantId == tenantId);
        if (content == null) return false;
        content.Status = "published";
        content.PublishedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        await InvalidateHomepageAsync(content.TenantId);
        return true;
    }

    public async Task<PagedResultDto<ContentListItemDto>> GetAdminContentAsync(Guid tenantId, int page, int pageSize, string? search = null, string? type = null)
    {
        var query = _db.Contents
            .IgnoreQueryFilters()
            .Include(c => c.ContentGenres).ThenInclude(cg => cg.Genre)
            .Where(c => c.TenantId == tenantId && !c.IsDeleted);

        if (!string.IsNullOrEmpty(search))
            query = query.Where(c => c.Title.Contains(search));

        if (!string.IsNullOrEmpty(type))
            query = query.Where(c => c.Type == type);

        var total = await query.CountAsync();
        var items = await query
            .OrderByDescending(c => c.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(ListItemProjection)
            .ToListAsync();

        return new PagedResultDto<ContentListItemDto> { Items = items, TotalCount = total, Page = page, PageSize = pageSize };
    }

    // ── Mapping Helpers ───────────────────────────────────────────────────────

    private async Task<StreamUrlsDto> GetStreamUrlsInternalAsync(Content content)
    {
        var asset = await _db.VideoAssets
            .FirstOrDefaultAsync(a => a.ContentId == content.Id && a.EpisodeId == null && a.Status == "ready");

        var dto = BuildStreamUrls(asset, content.HlsUrl, content.YoutubeId, content.VimeoId);
        await PopulateTracksAsync(dto, content.Id);
        return dto;
    }

    // Subtitle/audio tracks are managed per title (by ContentId) so they apply to
    // transcoded assets, external HLS URLs, and series episodes alike. Skipped for
    // YouTube/Vimeo, which surface captions through their own players.
    private async Task PopulateTracksAsync(StreamUrlsDto dto, Guid contentId)
    {
        if (dto.StreamProvider != "hls") return;

        var subs = await _db.Subtitles.Where(s => s.ContentId == contentId).ToListAsync();
        dto.Subtitles = subs.Select(s => new SubtitleDto
        {
            Language = s.LanguageCode ?? s.Language,
            Label = string.IsNullOrEmpty(s.Label) ? s.Language : s.Label,
            Url = _cdn.GetPublicUrl(s.FileUrl),
            Format = s.Format ?? "vtt"
        }).ToList();

        var auds = await _db.AudioTracks.Where(a => a.ContentId == contentId).OrderBy(a => a.TrackIndex).ToListAsync();
        dto.AudioTracks = auds.Select(a => new AudioTrackDto
        {
            Language = a.LanguageCode ?? a.Language,
            Label = string.IsNullOrEmpty(a.Label) ? a.Language : a.Label,
            TrackIndex = a.TrackIndex,
            IsDefault = a.IsDefault
        }).ToList();
    }

    private StreamUrlsDto BuildStreamUrls(VideoAsset? asset, string? hlsUrl, string? youtubeId, string? vimeoId)
    {
        var dto = new StreamUrlsDto();

        if (!string.IsNullOrEmpty(youtubeId))
        {
            dto.StreamProvider = "youtube";
            dto.YoutubeId = youtubeId;
            dto.YoutubeUrl = $"https://www.youtube.com/watch?v={youtubeId}";
            return dto;
        }

        if (!string.IsNullOrEmpty(vimeoId))
        {
            dto.StreamProvider = "vimeo";
            dto.VimeoId = vimeoId;
            dto.VimeoUrl = $"https://vimeo.com/{vimeoId}";
            return dto;
        }

        if (asset != null)
        {
            dto.StreamProvider = "hls";
            dto.Hls = _cdn.GetSignedUrl(asset.HlsPath ?? $"transcoded/{asset.ContentId}/master.m3u8");
            dto.Qualities = new Dictionary<string, string>
            {
                { "1080p", _cdn.GetSignedUrl($"transcoded/{asset.ContentId}/1080p/playlist.m3u8") },
                { "720p", _cdn.GetSignedUrl($"transcoded/{asset.ContentId}/720p/playlist.m3u8") },
                { "480p", _cdn.GetSignedUrl($"transcoded/{asset.ContentId}/480p/playlist.m3u8") },
                { "360p", _cdn.GetSignedUrl($"transcoded/{asset.ContentId}/360p/playlist.m3u8") }
            };
            // Subtitles/audio are attached per title via PopulateTracksAsync.
            return dto;
        }

        if (!string.IsNullOrEmpty(hlsUrl))
        {
            dto.StreamProvider = "hls";
            dto.Hls = hlsUrl;
            return dto;
        }

        return dto;
    }

    // SQL projection: list endpoints use this with .Select(...) so EF builds a lean SELECT of just
    // the list-item columns (+ genre names) instead of materializing full Content entities then
    // mapping in memory. Must stay translatable — no method calls EF can't convert.
    private static readonly Expression<Func<Content, ContentListItemDto>> ListItemProjection = c => new ContentListItemDto
    {
        Id = c.Id,
        Title = c.Title,
        Description = c.Description,
        Type = c.Type,
        ThumbnailUrl = c.ThumbnailUrl,
        PosterUrl = c.PosterUrl,
        BannerUrl = c.BannerUrl,
        ReleaseYear = c.ReleaseYear,
        AgeRating = c.AgeRating,
        AverageRating = c.AverageRating,
        DurationSeconds = c.DurationSeconds,
        MonetizationModel = c.MonetizationModel,
        Price = c.Price,
        IsFeatured = c.IsFeatured,
        IsTrending = c.IsTrending,
        IsNew = c.CreatedAt > DateTime.UtcNow.AddDays(-30),
        Genres = c.ContentGenres.Select(cg => cg.Genre.Name).ToList()
    };

    // In-memory mapper retained for paths that already hold a loaded Content (homepage rows/banners,
    // watch-history navigation). Null-safe on genres for partially-loaded entities.
    private static ContentListItemDto MapContentListItem(Content c) => new()
    {
        Id = c.Id,
        Title = c.Title,
        Description = c.Description,
        Type = c.Type,
        ThumbnailUrl = c.ThumbnailUrl,
        PosterUrl = c.PosterUrl,
        BannerUrl = c.BannerUrl,
        ReleaseYear = c.ReleaseYear,
        AgeRating = c.AgeRating,
        AverageRating = c.AverageRating,
        DurationSeconds = c.DurationSeconds,
        MonetizationModel = c.MonetizationModel,
        Price = c.Price,
        IsFeatured = c.IsFeatured,
        IsTrending = c.IsTrending,
        IsNew = c.CreatedAt > DateTime.UtcNow.AddDays(-30),
        Genres = c.ContentGenres?.Select(cg => cg.Genre.Name).ToList() ?? []
    };

    private static BrandingDto MapBranding(BrandingConfig? b) => new()
    {
        AppName = b?.AppName ?? "OTT Platform",
        LogoUrl = b?.LogoUrl,
        FaviconUrl = b?.FaviconUrl,
        PrimaryColor = b?.PrimaryColor ?? "#E50914",
        SecondaryColor = b?.SecondaryColor ?? "#141414",
        AccentColor = b?.AccentColor ?? "#FFFFFF",
        BackgroundColor = b?.BackgroundColor,
        TextColor = b?.TextColor,
        FontFamily = b?.FontFamily,
        CustomCss = b?.CustomCss,
        SplashScreenUrl = b?.SplashScreenUrl
    };

    private static BannerDto MapBanner(Banner b, Dictionary<Guid, Content> contents)
    {
        Content? content = b.ContentId.HasValue && contents.TryGetValue(b.ContentId.Value, out var c) ? c : null;
        return new BannerDto
        {
            Id = b.Id,
            Title = b.Title ?? content?.Title,
            Subtitle = b.Subtitle ?? content?.Description?.Substring(0, Math.Min(120, content?.Description?.Length ?? 0)),
            ImageUrl = b.ImageUrl ?? content?.BannerUrl,
            MobileImageUrl = b.MobileImageUrl ?? content?.ThumbnailUrl,
            CtaText = b.CtaText,
            CtaAction = b.CtaAction,
            ContentId = b.ContentId,
            Type = b.Type,
            SortOrder = b.SortOrder
        };
    }

    private static ContentRowDto MapContentRow(ContentRow r, Dictionary<Guid, Content> contents) => new()
    {
        Id = r.Id,
        Title = r.Title,
        RowType = r.RowType,
        DisplayStyle = r.DisplayStyle,
        SortOrder = r.SortOrder,
        Items = r.Items
            .OrderBy(i => i.SortOrder)
            .Where(i => contents.ContainsKey(i.ContentId))
            .Select(i => MapContentListItem(contents[i.ContentId]))
            .ToList()
    };

    private static LiveStreamListDto MapLiveStreamList(LiveStream l) => new()
    {
        Id = l.Id,
        Title = l.Title,
        Description = l.Description,
        ThumbnailUrl = l.ThumbnailUrl,
        Status = l.Status,
        ViewerCount = l.ViewerCount,
        StartedAt = l.StartedAt,
        Category = l.Category,
        IsScheduled = l.ScheduledAt.HasValue && l.Status == "offline",
        ScheduledAt = l.ScheduledAt
    };

    private static WatchProgressDto MapWatchProgress(WatchHistory h, int duration) => new()
    {
        PositionSeconds = h.PositionSeconds,
        DurationSeconds = duration,
        Percentage = duration > 0 ? Math.Round((double)h.PositionSeconds / duration * 100, 1) : 0,
        IsCompleted = h.IsCompleted,
        LastWatchedAt = h.LastWatchedAt
    };
}
