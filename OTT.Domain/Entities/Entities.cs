using System;
using System.Collections.Generic;

namespace OTT.Domain.Entities
{
    // ── Tenant ────────────────────────────────────────────────────────────────
    public class Tenant
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Name { get; set; } = "";
        public string Slug { get; set; } = "";
        public string? Domain { get; set; }
        public string Plan { get; set; } = "basic";
        public string? ContactEmail { get; set; }
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public ICollection<User> Users { get; set; } = new List<User>();
        public ICollection<Content> Contents { get; set; } = new List<Content>();
        public ICollection<Banner> Banners { get; set; } = new List<Banner>();
        public ICollection<ContentRow> ContentRows { get; set; } = new List<ContentRow>();
        public ICollection<LiveStream> LiveStreams { get; set; } = new List<LiveStream>();
        public ICollection<SubscriptionPlan> SubscriptionPlans { get; set; } = new List<SubscriptionPlan>();
        public BrandingConfig? BrandingConfig { get; set; }
    }

    // ── Branding / Config ─────────────────────────────────────────────────────
    public class BrandingConfig
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid TenantId { get; set; }
        public string? AppName { get; set; }
        public string? LogoUrl { get; set; }
        public string? FaviconUrl { get; set; }
        public string? SplashScreenUrl { get; set; }
        public string? PrimaryColor { get; set; }
        public string? SecondaryColor { get; set; }
        public string? AccentColor { get; set; }
        public string? BackgroundColor { get; set; }
        public string? TextColor { get; set; }
        public string? FontFamily { get; set; }
        public string? CustomCss { get; set; }
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
        public Tenant? Tenant { get; set; }
    }

    public class AppConfig
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid TenantId { get; set; }
        public string Key { get; set; } = "";
        public string? Value { get; set; }
        public bool IsPublic { get; set; }
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    // ── User ──────────────────────────────────────────────────────────────────
    public class User
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid TenantId { get; set; }
        public string Email { get; set; } = "";
        public string? PasswordHash { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? AvatarUrl { get; set; }
        public string? Phone { get; set; }
        public string Role { get; set; } = "viewer";
        public string AuthProvider { get; set; } = "local";
        public bool IsEmailVerified { get; set; }
        public bool IsPhoneVerified { get; set; }
        public bool IsBlocked { get; set; }
        public bool IsActive { get; set; } = true;
        public bool IsDeleted { get; set; }
        public string? SocialProvider { get; set; }
        public string? SocialId { get; set; }
        public string? Language { get; set; } = "en";
        public string? Country { get; set; }
        public DateTime? LastLoginAt { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
        public Tenant? Tenant { get; set; }
        public ICollection<UserProfile> Profiles { get; set; } = new List<UserProfile>();
        public ICollection<UserSubscription> Subscriptions { get; set; } = new List<UserSubscription>();
        public ICollection<Payment> Payments { get; set; } = new List<Payment>();
        public ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();
        public ICollection<DeviceToken> DeviceTokens { get; set; } = new List<DeviceToken>();
        public ICollection<Notification> Notifications { get; set; } = new List<Notification>();
    }

    public class UserProfile
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid UserId { get; set; }
        public string Name { get; set; } = "";
        public string? AvatarUrl { get; set; }
        public bool IsDefault { get; set; }
        public string MaturityLevel { get; set; } = "all";
        public string? Language { get; set; }
        public string? PinHash { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public User? User { get; set; }
    }

    public class RefreshToken
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid UserId { get; set; }
        public string Token { get; set; } = "";
        public DateTime ExpiresAt { get; set; }
        public bool IsRevoked { get; set; }
        public DateTime? RevokedAt { get; set; }
        public string? DeviceInfo { get; set; }
        public string? IpAddress { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public User? User { get; set; }
    }

    // ── Content ───────────────────────────────────────────────────────────────
    public class Content
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid TenantId { get; set; }
        public string Title { get; set; } = "";
        public string? Description { get; set; }
        public string Type { get; set; } = "movie"; // movie | series | short | documentary | live
        public string? PosterUrl { get; set; }
        public string? ThumbnailUrl { get; set; }
        public string? BannerUrl { get; set; }
        public string? TrailerUrl { get; set; }
        public int? ReleaseYear { get; set; }
        public int? DurationSeconds { get; set; }
        public string? Language { get; set; }
        public string? Country { get; set; }
        public string? DirectorName { get; set; }
        public string? AgeRating { get; set; }
        public string MonetizationModel { get; set; } = "svod"; // svod | avod | tvod
        public decimal? Price { get; set; }
        public string Status { get; set; } = "draft"; // draft | published | archived
        public bool IsFeatured { get; set; }
        public bool IsNew { get; set; }
        public bool IsTrending { get; set; }
        public decimal? AverageRating { get; set; }
        public int RatingCount { get; set; }
        public long ViewCount { get; set; }
        public string? HlsUrl { get; set; }
        public string? YoutubeId { get; set; }
        public string? VimeoId { get; set; }
        public string? VideoSourceType { get; set; }
        public string? VideoSourceId { get; set; }
        public string? DrmKeyId { get; set; }
        public DateTime? PublishedAt { get; set; }
        public bool IsDeleted { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
        public Tenant? Tenant { get; set; }
        public ICollection<ContentGenre> ContentGenres { get; set; } = new List<ContentGenre>();
        public ICollection<ContentTag> ContentTags { get; set; } = new List<ContentTag>();
        public ICollection<ContentCast> ContentCasts { get; set; } = new List<ContentCast>();
        public ICollection<Season> Seasons { get; set; } = new List<Season>();
        public ICollection<VideoAsset> VideoAssets { get; set; } = new List<VideoAsset>();
    }

    public class ContentCast
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid ContentId { get; set; }
        public string ActorName { get; set; } = "";
        public string? CharacterName { get; set; }
        public string? Role { get; set; }
        public string? PhotoUrl { get; set; }
        public int SortOrder { get; set; }
        public Content? Content { get; set; }
    }

    public class Genre
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid TenantId { get; set; }
        public string Name { get; set; } = "";
        public string Slug { get; set; } = "";
        public string? IconUrl { get; set; }
        public int SortOrder { get; set; }
        public ICollection<ContentGenre> ContentGenres { get; set; } = new List<ContentGenre>();
    }

    public class ContentGenre
    {
        public Guid ContentId { get; set; }
        public Guid GenreId { get; set; }
        public Content? Content { get; set; }
        public Genre? Genre { get; set; }
    }

    public class Tag
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid TenantId { get; set; }
        public string Name { get; set; } = "";
        public string Slug { get; set; } = "";
        public ICollection<ContentTag> ContentTags { get; set; } = new List<ContentTag>();
    }

    public class ContentTag
    {
        public Guid ContentId { get; set; }
        public Guid TagId { get; set; }
        public Content? Content { get; set; }
        public Tag? Tag { get; set; }
    }

    // ── Season & Episode ──────────────────────────────────────────────────────
    public class Season
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid ContentId { get; set; }
        public int SeasonNumber { get; set; }
        public string? Title { get; set; }
        public string? Description { get; set; }
        public string? PosterUrl { get; set; }
        public int? ReleaseYear { get; set; }
        public Content? Content { get; set; }
        public ICollection<Episode> Episodes { get; set; } = new List<Episode>();
    }

    public class Episode
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid SeasonId { get; set; }
        public Guid? ContentId { get; set; }
        public int EpisodeNumber { get; set; }
        public string Title { get; set; } = "";
        public string? Description { get; set; }
        public string? ThumbnailUrl { get; set; }
        public int? DurationSeconds { get; set; }
        public string? HlsUrl { get; set; }
        public string? ExternalVideoUrl { get; set; }
        public string? YoutubeId { get; set; }
        public string? VimeoId { get; set; }
        public bool IsFree { get; set; }
        public DateTime? AiredAt { get; set; }
        public Season? Season { get; set; }
        public Content? Content { get; set; }
        public ICollection<VideoAsset> VideoAssets { get; set; } = new List<VideoAsset>();
    }

    // ── Video Asset / Subtitle ────────────────────────────────────────────────
    public class VideoAsset
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid? ContentId { get; set; }
        public Guid? EpisodeId { get; set; }
        public string? OriginalFileName { get; set; }
        public string? OriginalKey { get; set; }
        public string? HlsPath { get; set; }
        public string? StorageProvider { get; set; }
        public string? Resolution { get; set; }
        public string? ExternalProvider { get; set; }
        public string Status { get; set; } = "pending";
        public int? DurationSeconds { get; set; }
        public int? OriginalWidth { get; set; }
        public int? OriginalHeight { get; set; }
        public string? ErrorMessage { get; set; }
        public DateTime? TranscodingStartedAt { get; set; }
        public DateTime? TranscodedAt { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public Content? Content { get; set; }
        public ICollection<Subtitle> Subtitles { get; set; } = new List<Subtitle>();
    }

    public class Subtitle
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid? ContentId { get; set; }
        public Guid? AssetId { get; set; }
        public string Language { get; set; } = "";
        public string? LanguageCode { get; set; }
        public string Label { get; set; } = "";
        public string FileUrl { get; set; } = "";
        public string Format { get; set; } = "vtt";
        public VideoAsset? VideoAsset { get; set; }
    }

    // ── Banner / Content Rows ─────────────────────────────────────────────────
    public class Banner
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid TenantId { get; set; }
        public string Title { get; set; } = "";
        public string? Subtitle { get; set; }
        public string Type { get; set; } = "hero";
        public string? ImageUrl { get; set; }
        public string? MobileImageUrl { get; set; }
        public string? ActionType { get; set; }
        public string? ActionValue { get; set; }
        public string? CtaText { get; set; }
        public string? CtaAction { get; set; }
        public Guid? ContentId { get; set; }
        public int SortOrder { get; set; }
        public bool IsActive { get; set; } = true;
        public DateTime? StartsAt { get; set; }
        public DateTime? EndsAt { get; set; }
        public Tenant? Tenant { get; set; }
    }

    public class ContentRow
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid TenantId { get; set; }
        public string Title { get; set; } = "";
        public string RowType { get; set; } = "manual"; // genre | tag | manual | trending | new | watchlist
        public string? SourceValue { get; set; }
        public string DisplayStyle { get; set; } = "portrait";
        public int SortOrder { get; set; }
        public int MaxItems { get; set; } = 20;
        public bool IsActive { get; set; } = true;
        public Tenant? Tenant { get; set; }
        public ICollection<ContentRowItem> Items { get; set; } = new List<ContentRowItem>();
    }

    public class ContentRowItem
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid ContentRowId { get; set; }
        public Guid ContentId { get; set; }
        public int SortOrder { get; set; }
        public ContentRow? ContentRow { get; set; }
    }

    // ── Live Stream ───────────────────────────────────────────────────────────
    public class LiveStream
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid TenantId { get; set; }
        public Guid CreatedByUserId { get; set; }
        public string Title { get; set; } = "";
        public string? Description { get; set; }
        public string? ThumbnailUrl { get; set; }
        public string StreamProvider { get; set; } = "antmedia"; // antmedia | youtube | vimeo
        public string? StreamKey { get; set; }
        public string? AntMediaStreamId { get; set; }
        public string? PlaybackUrl { get; set; }
        public string? YoutubeStreamId { get; set; }
        public string? VimeoStreamId { get; set; }
        public string? Category { get; set; }
        public bool ChatEnabled { get; set; } = true;
        public bool IsScheduled { get; set; }
        public DateTime? ScheduledAt { get; set; }
        public string MonetizationModel { get; set; } = "free";
        public string Status { get; set; } = "offline"; // offline | live | ended
        public int ViewerCount { get; set; }
        public int? PeakViewerCount { get; set; }
        public DateTime? StartedAt { get; set; }
        public DateTime? EndedAt { get; set; }
        public bool IsDeleted { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public Tenant? Tenant { get; set; }
    }

    public class LiveChatMessage
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid StreamId { get; set; }
        public Guid UserId { get; set; }
        public string Message { get; set; } = "";
        public bool IsDeleted { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    // ── Subscription / Payment ────────────────────────────────────────────────
    public class SubscriptionPlan
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid TenantId { get; set; }
        public string Name { get; set; } = "";
        public string? Description { get; set; }
        public decimal Price { get; set; }
        public string Currency { get; set; } = "INR";
        public string BillingCycle { get; set; } = "monthly";
        public int MaxProfiles { get; set; } = 1;
        public int MaxStreams { get; set; } = 1;
        public bool AllowDownloads { get; set; }
        public bool AllowUhd { get; set; }
        public string? Features { get; set; } // JSON array
        public bool IsActive { get; set; } = true;
        public bool IsPopular { get; set; }
        public string? RazorpayPlanId { get; set; }
        public string? StripePriceId { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public Tenant? Tenant { get; set; }
    }

    public class UserSubscription
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid UserId { get; set; }
        public Guid PlanId { get; set; }
        public string Status { get; set; } = "active";
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public bool AutoRenew { get; set; } = true;
        public string? PaymentGateway { get; set; }
        public string? GatewayPaymentId { get; set; }
        public string? RazorpayOrderId { get; set; }
        public string? RazorpaySubscriptionId { get; set; }
        public DateTime? CancelledAt { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public User User { get; set; } = null!;
        public SubscriptionPlan Plan { get; set; } = null!;
    }

    public class Payment
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid UserId { get; set; }
        public Guid TenantId { get; set; }
        // Set for per-title (TVOD/PPV) purchases; null for subscription payments.
        public Guid? ContentId { get; set; }
        public string Gateway { get; set; } = "";
        public string? GatewayOrderId { get; set; }
        public string? GatewayPaymentId { get; set; }
        public decimal Amount { get; set; }
        public string Currency { get; set; } = "INR";
        public string Status { get; set; } = "pending";
        public string? Description { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public User? User { get; set; }
    }

    public class PromoCode
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid TenantId { get; set; }
        public string Code { get; set; } = "";
        public string DiscountType { get; set; } = "percentage";
        public decimal DiscountValue { get; set; }
        public int? MaxUses { get; set; }
        public int UsedCount { get; set; }
        public DateTime? ExpiresAt { get; set; }
        public bool IsActive { get; set; } = true;
    }

    // ── Watch History / Watchlist / Rating / Download ─────────────────────────
    public class WatchHistory
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid ProfileId { get; set; }
        public Guid? UserId { get; set; }
        public Guid ContentId { get; set; }
        public Guid? EpisodeId { get; set; }
        public int PositionSeconds { get; set; }
        public int? TotalSeconds { get; set; }
        public bool IsCompleted { get; set; }
        public DateTime AddedAt { get; set; } = DateTime.UtcNow;
        public DateTime LastWatchedAt { get; set; } = DateTime.UtcNow;
        public UserProfile? UserProfile { get; set; }
        public Content? Content { get; set; }
    }

    public class Watchlist
    {
        public Guid ProfileId { get; set; }
        public Guid ContentId { get; set; }
        public DateTime AddedAt { get; set; } = DateTime.UtcNow;
        public Content? Content { get; set; }
    }

    public class UserRating
    {
        public Guid ProfileId { get; set; }
        public Guid ContentId { get; set; }
        public decimal Rating { get; set; }
        public string? Review { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public class Download
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid ProfileId { get; set; }
        public Guid? UserId { get; set; }
        public Guid ContentId { get; set; }
        public Guid? EpisodeId { get; set; }
        public string Status { get; set; } = "downloading";
        public string? LocalPath { get; set; }
        public DateTime? ExpiresAt { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    // ── Watch Party ───────────────────────────────────────────────────────────
    public class WatchParty
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid HostUserId { get; set; }
        public Guid? ContentId { get; set; }
        public Guid? EpisodeId { get; set; }
        public string Code { get; set; } = "";
        public bool IsPrivate { get; set; }
        public int MaxMembers { get; set; } = 10;
        public string Status { get; set; } = "active";
        public DateTime? EndedAt { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public Content? Content { get; set; }
        public ICollection<WatchPartyMember> Members { get; set; } = new List<WatchPartyMember>();
    }

    public class WatchPartyMember
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid WatchPartyId { get; set; }
        public Guid UserId { get; set; }
        public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
        public DateTime? LeftAt { get; set; }
        public WatchParty? WatchParty { get; set; }
    }

    // ── Notifications ─────────────────────────────────────────────────────────
    public class Notification
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid UserId { get; set; }
        public string Title { get; set; } = "";
        public string Body { get; set; } = "";
        public string Type { get; set; } = "info";
        public string? ActionUrl { get; set; }
        public string? ImageUrl { get; set; }
        public bool IsRead { get; set; }
        public DateTime? ReadAt { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public User? User { get; set; }
    }

    public class DeviceToken
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid UserId { get; set; }
        public string Token { get; set; } = "";
        public string Platform { get; set; } = "";
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }
        public User? User { get; set; }
    }

    // ── Analytics ─────────────────────────────────────────────────────────────
    public class AnalyticsEvent
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid? UserId { get; set; }
        public Guid TenantId { get; set; }
        public string EventType { get; set; } = "";
        public Guid? ContentId { get; set; }
        public Guid? EpisodeId { get; set; }
        public int? WatchDurationSeconds { get; set; }
        public string? Platform { get; set; }
        public string? Country { get; set; }
        public string? DeviceType { get; set; }
        public string? ExtraData { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public class ContentAnalytics
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid ContentId { get; set; }
        public DateTime Date { get; set; }
        public int Views { get; set; }
        public int TotalWatchSeconds { get; set; }
        public int UniqueViewers { get; set; }
        public Content? Content { get; set; }
    }

    // ── Creator / Parental ────────────────────────────────────────────────────
    public class CreatorApplication
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid UserId { get; set; }
        public Guid TenantId { get; set; }
        public string Status { get; set; } = "pending"; // pending | approved | rejected
        public string? ChannelName { get; set; }
        public string? Bio { get; set; }
        public string? Reason { get; set; }
        public DateTime? ReviewedAt { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public class ParentalControl
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid ProfileId { get; set; }
        public string MaxRating { get; set; } = "PG";
        public bool RequirePin { get; set; }
        public string? PinHash { get; set; }
        public bool BlockViolence { get; set; }
        public bool BlockLanguage { get; set; }
        public bool BlockSexualContent { get; set; }
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    // ── IPTV channels (synced from iptv-org) ───────────────────────────────────
    public class IptvChannel
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string ChannelId { get; set; } = "";   // iptv-org channel id
        public string Name { get; set; } = "";
        public string? Country { get; set; }           // ISO 2-letter code
        public string? CountryName { get; set; }
        public string? Languages { get; set; }         // csv of language codes
        public string? Categories { get; set; }        // csv of category ids
        public string? LogoUrl { get; set; }
        public string StreamUrl { get; set; } = "";
        public string? Quality { get; set; }
        public bool IsNsfw { get; set; }
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    // ── Storage (admin-configurable, hot-reloadable) ───────────────────────────
    public class StorageConfiguration
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Provider { get; set; } = "s3";   // s3 | minio | wasabi | spaces | r2 | local
        public string? BucketName { get; set; }
        public string? Region { get; set; }
        public string? AccessKey { get; set; }
        public string? SecretKey { get; set; }          // NOTE: store encrypted at rest in production
        public string? ServiceUrl { get; set; }         // for S3-compatible providers
        public bool ForcePathStyle { get; set; } = true;
        public string? PublicBaseUrl { get; set; }       // CDN / static host used to build public URLs
        public string? LocalRootPath { get; set; }       // root folder for the "local" provider
        public bool IsActive { get; set; } = true;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    // ── Audit Log (admin/privileged action trail — compliance) ─────────────────
    public class AuditLog
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid TenantId { get; set; }
        public Guid? ActorUserId { get; set; }          // null for unauthenticated/system actions
        public string? ActorEmail { get; set; }
        public string Action { get; set; } = string.Empty;   // HTTP method (POST/PUT/PATCH/DELETE)
        public string Path { get; set; } = string.Empty;     // request path acted upon
        public int StatusCode { get; set; }
        public string? IpAddress { get; set; }
        public string? UserAgent { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
