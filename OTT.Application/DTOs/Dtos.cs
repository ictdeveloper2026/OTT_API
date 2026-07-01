using System.ComponentModel.DataAnnotations;

namespace OTT.Application.DTOs;

// ── Auth DTOs ─────────────────────────────────────────────────────────────────
// [ApiController] auto-returns 400 ProblemDetails when these annotations fail.

public record LoginRequestDto(
    [Required, EmailAddress] string Email,
    [Required] string Password,
    string? DeviceId);

public record RegisterRequestDto(
    [Required, EmailAddress] string Email,
    [Required, MinLength(8), MaxLength(128)] string Password,
    [Required, MaxLength(100)] string FirstName,
    [MaxLength(100)] string LastName,
    [Phone] string? Phone);

public record VerifyOtpDto(
    [Required, EmailAddress] string Email,
    [Required, RegularExpression(@"^\d{6}$", ErrorMessage = "OTP must be 6 digits")] string Otp);

public record SocialLoginDto(
    [Required] string Provider,
    [Required] string Token,
    string? DeviceId);

public record ResetPasswordDto(
    [Required] string Token,
    [Required, MinLength(8), MaxLength(128)] string NewPassword);

public record ChangePasswordDto(
    [Required] string CurrentPassword,
    [Required, MinLength(8), MaxLength(128)] string NewPassword);

public record RefreshTokenRequestDto([Required] string RefreshToken);
public record ForgotPasswordRequestDto([Required, EmailAddress] string Email);
public record LogoutRequestDto([Required] string RefreshToken);
public record SendOtpRequestDto([Required, EmailAddress] string Email);
public record SelectProfileRequestDto([Required] Guid ProfileId);

public class AuthResponseDto
{
    public string AccessToken { get; set; } = "";
    public string RefreshToken { get; set; } = "";
    public int ExpiresIn { get; set; } = 3600;
    public UserDto User { get; set; } = null!;
    public List<ProfileDto> Profiles { get; set; } = [];
}

public class UserDto
{
    public Guid Id { get; set; }
    public string Email { get; set; } = "";
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Phone { get; set; }
    public string? AvatarUrl { get; set; }
    public string Role { get; set; } = "viewer";
    public bool IsEmailVerified { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class ProfileDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string? AvatarUrl { get; set; }
    public bool IsDefault { get; set; }
    public string MaturityLevel { get; set; } = "all";
    public string? Language { get; set; }
    public bool IsPinLocked { get; set; }
}

// ── Content DTOs ─────────────────────────────────────────────────────────────

public class ContentListItemDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public string Type { get; set; } = "";
    public string? ThumbnailUrl { get; set; }
    public string? PosterUrl { get; set; }
    public string? BannerUrl { get; set; }
    public int? ReleaseYear { get; set; }
    public string? AgeRating { get; set; }
    public decimal? AverageRating { get; set; }
    public int? DurationSeconds { get; set; }
    public string MonetizationModel { get; set; } = "svod";
    public decimal? Price { get; set; }
    public List<string> Genres { get; set; } = [];
    public bool IsFeatured { get; set; }
    public bool IsNew { get; set; }
    public bool IsTrending { get; set; }
}

public class ContentDetailDto : ContentListItemDto
{
    public string? TrailerUrl { get; set; }
    public string? DirectorName { get; set; }
    public List<CastDto> Cast { get; set; } = [];
    public List<string> Tags { get; set; } = [];
    public List<string> Languages { get; set; } = [];
    public List<SeasonDto> Seasons { get; set; } = [];
    public StreamUrlsDto? StreamUrls { get; set; }
    public WatchProgressDto? WatchProgress { get; set; }
    public bool IsInWatchlist { get; set; }
    public decimal? UserRating { get; set; }
    public List<ContentListItemDto> Related { get; set; } = [];
}

public class SeasonDto
{
    public Guid Id { get; set; }
    public int SeasonNumber { get; set; }
    public string? Title { get; set; }
    public List<EpisodeDto> Episodes { get; set; } = [];
}

public class EpisodeDto
{
    public Guid Id { get; set; }
    public int EpisodeNumber { get; set; }
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public string? ThumbnailUrl { get; set; }
    public int? DurationSeconds { get; set; }
    public StreamUrlsDto? StreamUrls { get; set; }
    public WatchProgressDto? WatchProgress { get; set; }
}

public class CastDto
{
    public string Name { get; set; } = "";
    public string? Character { get; set; }
    public string? PhotoUrl { get; set; }
    public string? Role { get; set; }
}

public class StreamUrlsDto
{
    public string? Hls { get; set; }
    public string? Dash { get; set; }
    public string? YoutubeId { get; set; }
    public string? VimeoId { get; set; }
    public string? YoutubeUrl { get; set; }
    public string? VimeoUrl { get; set; }
    public Dictionary<string, string> Qualities { get; set; } = [];
    public List<SubtitleDto> Subtitles { get; set; } = [];
    public List<AudioTrackDto> AudioTracks { get; set; } = [];
    public string? DrmLicenseUrl { get; set; }
    public string StreamProvider { get; set; } = "hls";
}

public class SubtitleDto
{
    public string Language { get; set; } = "";
    public string Label { get; set; } = "";
    public string Url { get; set; } = "";
    public string Format { get; set; } = "vtt";
}

public class AudioTrackDto
{
    public string Language { get; set; } = "";
    public string Label { get; set; } = "";
    public int TrackIndex { get; set; }
    public bool IsDefault { get; set; }
}

public class WatchProgressDto
{
    public int PositionSeconds { get; set; }
    public int DurationSeconds { get; set; }
    public double Percentage { get; set; }
    public bool IsCompleted { get; set; }
    public DateTime LastWatchedAt { get; set; }
}

// ── Home / Banner DTOs ───────────────────────────────────────────────────────

public class HomePageDto
{
    public BrandingDto Branding { get; set; } = null!;
    public List<BannerDto> Banners { get; set; } = [];
    public List<ContentRowDto> Rows { get; set; } = [];
    public List<ContentListItemDto> ContinueWatching { get; set; } = [];
    public List<ContentListItemDto> Recommendations { get; set; } = [];
    public List<LiveStreamListDto> LiveNow { get; set; } = [];
}

public class BannerDto
{
    public Guid Id { get; set; }
    public string? Title { get; set; }
    public string? Subtitle { get; set; }
    public string? ImageUrl { get; set; }
    public string? MobileImageUrl { get; set; }
    public string? CtaText { get; set; }
    public string? CtaAction { get; set; }
    public Guid? ContentId { get; set; }
    public string Type { get; set; } = "hero";
    public int SortOrder { get; set; }
}

public class ContentRowDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = "";
    public string RowType { get; set; } = "manual";
    public string DisplayStyle { get; set; } = "landscape";
    public int SortOrder { get; set; }
    public List<ContentListItemDto> Items { get; set; } = [];
}

public class BrandingDto
{
    public string AppName { get; set; } = "";
    public string? LogoUrl { get; set; }
    public string? FaviconUrl { get; set; }
    public string PrimaryColor { get; set; } = "#E50914";
    public string SecondaryColor { get; set; } = "#141414";
    public string AccentColor { get; set; } = "#FFFFFF";
    public string? BackgroundColor { get; set; }
    public string? TextColor { get; set; }
    public string? FontFamily { get; set; }
    public string? CustomCss { get; set; }
    public string? SplashScreenUrl { get; set; }
}

// ── Search DTOs ───────────────────────────────────────────────────────────────

public class SearchRequestDto
{
    public string Query { get; set; } = "";
    public string? Type { get; set; }
    public List<string>? Genres { get; set; }
    public string? Language { get; set; }
    public string? AgeRating { get; set; }
    public int? ReleaseYear { get; set; }
    public string SortBy { get; set; } = "relevance";
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

public class SearchResultDto
{
    public List<ContentListItemDto> Results { get; set; } = [];
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalPages { get; set; }
    public List<string> Suggestions { get; set; } = [];
}

// ── Subscription DTOs ─────────────────────────────────────────────────────────

public class SubscriptionPlanDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public decimal Price { get; set; }
    public string Currency { get; set; } = "INR";
    public string BillingCycle { get; set; } = "monthly";
    public int MaxProfiles { get; set; }
    public int MaxStreams { get; set; }
    public bool AllowDownloads { get; set; }
    public bool AllowUhd { get; set; }
    public List<string> Features { get; set; } = [];
    public bool IsPopular { get; set; }
    public bool IsActive { get; set; } = true;
    public string? RazorpayPlanId { get; set; }
}

public class CreateOrderDto
{
    [Required] public Guid PlanId { get; set; }
    [MaxLength(100)] public string? PromoCode { get; set; }
    [Required, RegularExpression("^(razorpay|stripe|paypal)$", ErrorMessage = "Unsupported gateway")]
    public string Gateway { get; set; } = "razorpay";
}

public class OrderResponseDto
{
    public string OrderId { get; set; } = "";
    public string Gateway { get; set; } = "";
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "";
    public string? RazorpayKeyId { get; set; }
    public string? StripeClientSecret { get; set; }
}

public class VerifyPaymentDto
{
    [Required, MaxLength(200)] public string OrderId { get; set; } = "";
    [Required, MaxLength(200)] public string PaymentId { get; set; } = "";
    [MaxLength(512)] public string? Signature { get; set; }
    [Required, RegularExpression("^(razorpay|stripe|paypal)$", ErrorMessage = "Unsupported gateway")]
    public string Gateway { get; set; } = "razorpay";
}

public class UserSubscriptionDto
{
    public Guid Id { get; set; }
    public SubscriptionPlanDto Plan { get; set; } = null!;
    public string Status { get; set; } = "";
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public bool AutoRenew { get; set; }
    public DateTime? CancelledAt { get; set; }
}

// ── Live Stream DTOs ─────────────────────────────────────────────────────────

public class LiveStreamListDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public string? ThumbnailUrl { get; set; }
    public string Status { get; set; } = "offline";
    public int? ViewerCount { get; set; }
    public DateTime? StartedAt { get; set; }
    public string? Category { get; set; }
    public bool IsScheduled { get; set; }
    public DateTime? ScheduledAt { get; set; }
}

public class LiveStreamDetailDto : LiveStreamListDto
{
    public string? PlaybackUrl { get; set; }
    public string? YoutubeStreamId { get; set; }
    public string? VimeoStreamId { get; set; }
    public string StreamProvider { get; set; } = "antmedia";
    public bool ChatEnabled { get; set; }
    public List<LiveChatMessageDto> RecentChat { get; set; } = [];
}

public class LiveChatMessageDto
{
    public Guid Id { get; set; }
    public string UserName { get; set; } = "";
    public string? AvatarUrl { get; set; }
    public string Message { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}

// ── Watch Party DTOs ─────────────────────────────────────────────────────────

public class CreateWatchPartyDto
{
    public Guid ContentId { get; set; }
    public Guid? EpisodeId { get; set; }
    public bool IsPrivate { get; set; } = true;
    public int MaxMembers { get; set; } = 10;
}

public class WatchPartyDto
{
    public Guid Id { get; set; }
    public string Code { get; set; } = "";
    public ContentListItemDto Content { get; set; } = null!;
    public Guid HostUserId { get; set; }
    public string HostName { get; set; } = "";
    public int MemberCount { get; set; }
    public int MaxMembers { get; set; }
    public string Status { get; set; } = "active";
    public int CurrentPositionSeconds { get; set; }
    public bool IsPlaying { get; set; }
    public DateTime CreatedAt { get; set; }
}

// ── Admin DTOs ────────────────────────────────────────────────────────────────

public class DashboardStatsDto
{
    public int TotalUsers { get; set; }
    public int ActiveSubscriptions { get; set; }
    public decimal MonthlyRevenue { get; set; }
    public int TotalContent { get; set; }
    public int LiveStreams { get; set; }
    public int TotalViews { get; set; }
    public double AvgWatchTimeMinutes { get; set; }
    public List<RevenueChartDto> RevenueChart { get; set; } = [];
    public List<ContentAnalyticsDto> TopContent { get; set; } = [];
    public List<UserGrowthDto> UserGrowth { get; set; } = [];
}

public class RevenueChartDto
{
    public string Label { get; set; } = "";
    public decimal Amount { get; set; }
}

public class ContentAnalyticsDto
{
    public Guid ContentId { get; set; }
    public string Title { get; set; } = "";
    public string? ThumbnailUrl { get; set; }
    public long TotalViews { get; set; }
    public double AvgWatchTimeMinutes { get; set; }
    public decimal CompletionRate { get; set; }
}

public class UserGrowthDto
{
    public string Label { get; set; } = "";
    public int Count { get; set; }
}

public class CreateContentDto
{
    [Required, MaxLength(300)] public string Title { get; set; } = "";
    [MaxLength(5000)] public string? Description { get; set; }
    [MaxLength(500)] public string? ShortDescription { get; set; }
    [Required, RegularExpression("^(movie|series|documentary|short)$", ErrorMessage = "Invalid content type")]
    public string Type { get; set; } = "movie";
    public string? ThumbnailUrl { get; set; }
    public string? PosterUrl { get; set; }
    public string? BannerUrl { get; set; }
    public string? TrailerUrl { get; set; }
    [Range(1900, 2100)] public int? ReleaseYear { get; set; }
    [MaxLength(50)] public string? Language { get; set; }
    [MaxLength(20)] public string? AgeRating { get; set; }
    [RegularExpression("^(free|avod|svod|tvod)$", ErrorMessage = "Invalid monetization model")]
    public string MonetizationModel { get; set; } = "svod";
    [Range(0, 1000000)] public decimal? Price { get; set; }
    public string? DirectorName { get; set; }
    public string? Cast { get; set; }
    public List<Guid> GenreIds { get; set; } = [];
    public List<string> Tags { get; set; } = [];
    public bool IsFeatured { get; set; }
    public bool IsTrending { get; set; }
    // Video source
    public string VideoSourceType { get; set; } = "upload"; // upload, youtube, vimeo, hls
    public string? VideoUrl { get; set; }
    public string? YoutubeId { get; set; }
    public string? VimeoId { get; set; }
    public string? HlsUrl { get; set; }
}

public class UpdateBrandingDto
{
    public string? AppName { get; set; }
    public string? LogoUrl { get; set; }
    public string? FaviconUrl { get; set; }
    public string? PrimaryColor { get; set; }
    public string? SecondaryColor { get; set; }
    public string? AccentColor { get; set; }
    public string? BackgroundColor { get; set; }
    public string? TextColor { get; set; }
    public string? FontFamily { get; set; }
    public string? CustomCss { get; set; }
    public string? SplashScreenUrl { get; set; }
}

public class CreateLiveStreamDto
{
    [Required, MaxLength(300)] public string Title { get; set; } = "";
    [MaxLength(5000)] public string? Description { get; set; }
    public string? ThumbnailUrl { get; set; }
    [Required, MaxLength(50)] public string StreamProvider { get; set; } = "antmedia";
    public string? YoutubeStreamId { get; set; }
    public string? VimeoStreamId { get; set; }
    public string? Category { get; set; }
    public bool ChatEnabled { get; set; } = true;
    public bool IsScheduled { get; set; }
    public DateTime? ScheduledAt { get; set; }
    public string MonetizationModel { get; set; } = "svod";
}

// ── Storage Config DTOs ─────────────────────────────────────────────────────────

public class UpdateStorageConfigDto
{
    [Required, RegularExpression("^(s3|minio|wasabi|spaces|r2|local)$", ErrorMessage = "Unsupported storage provider")]
    public string Provider { get; set; } = "s3"; // s3 | minio | wasabi | spaces | r2 | local
    public string? BucketName { get; set; }
    public string? Region { get; set; }
    public string? AccessKey { get; set; }
    public string? SecretKey { get; set; } // leave null/empty to keep the existing secret
    public string? ServiceUrl { get; set; }
    public bool ForcePathStyle { get; set; } = true;
    public string? PublicBaseUrl { get; set; }
    public string? LocalRootPath { get; set; }
}

public class UpdateSettingsDto
{
    public Dictionary<string, string?> Settings { get; set; } = new();
    public bool IsPublic { get; set; }
    public bool Global { get; set; } // write to app-wide settings (TenantId = empty) instead of the current tenant
}

public class PagedResultDto<T>
{
    public List<T> Items { get; set; } = [];
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);
    public bool HasNext => Page < TotalPages;
    public bool HasPrevious => Page > 1;
}

public class ApiResponse<T>
{
    public bool Success { get; set; }
    public T? Data { get; set; }
    public string? Message { get; set; }
    public List<string> Errors { get; set; } = [];

    public static ApiResponse<T> Ok(T data, string? message = null) =>
        new() { Success = true, Data = data, Message = message };

    public static ApiResponse<T> Fail(string error) =>
        new() { Success = false, Errors = [error] };

    public static ApiResponse<T> Fail(List<string> errors) =>
        new() { Success = false, Errors = errors };
}

// ── Upload DTOs ───────────────────────────────────────────────────────────────

public class UploadRequestDto
{
    public string FileName { get; set; } = "";
    public string ContentType { get; set; } = "";
    public long FileSize { get; set; }
    public string UploadType { get; set; } = "video"; // video, thumbnail, poster, banner, logo, subtitle
    public Guid? ContentId { get; set; }
    public Guid? EpisodeId { get; set; }
}

public class UploadUrlResponseDto
{
    public string UploadUrl { get; set; } = "";
    public string FileKey { get; set; } = "";
    public Dictionary<string, string> Headers { get; set; } = [];
    public string? AssetId { get; set; }
}

public class TranscodeRequestDto
{
    public Guid AssetId { get; set; }
    public string SourceKey { get; set; } = "";
    public List<string> Qualities { get; set; } = ["1080p", "720p", "480p", "360p"];
    public bool GenerateThumbnails { get; set; } = true;
    public bool ExtractAudio { get; set; } = false;
}

// ── Community (suggestions + polls) ─────────────────────────────────────────────
public class CreateSuggestionDto
{
    [Required, MaxLength(300)] public string Title { get; set; } = "";
    [MaxLength(2000)] public string? Description { get; set; }
}

public class UpdateSuggestionStatusDto
{
    [Required, RegularExpression("^(open|planned|added|rejected|promoted)$", ErrorMessage = "Invalid status")]
    public string Status { get; set; } = "open";
    public Guid? LinkedContentId { get; set; }
}

public class CreatePollDto
{
    [Required, MaxLength(300)] public string Question { get; set; } = "";
    [MaxLength(2000)] public string? Description { get; set; }
    public DateTime? EndsAt { get; set; }
    [MinLength(2, ErrorMessage = "A poll needs at least two options")]
    public List<string> Options { get; set; } = [];
    public Guid? SourceSuggestionId { get; set; }
}

public class UpdatePollDto
{
    [MaxLength(300)] public string? Question { get; set; }
    [MaxLength(2000)] public string? Description { get; set; }
    [RegularExpression("^(open|closed)$", ErrorMessage = "Status must be 'open' or 'closed'")]
    public string? Status { get; set; }
    public DateTime? EndsAt { get; set; }
}

public class CastVoteDto
{
    [Required] public Guid OptionId { get; set; }
}

public class SuggestionDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public string Status { get; set; } = "open";
    public int UpvoteCount { get; set; }
    public bool HasVoted { get; set; }
    public Guid? LinkedContentId { get; set; }
    public Guid CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class PollOptionDto
{
    public Guid Id { get; set; }
    public string Text { get; set; } = "";
    public int VoteCount { get; set; }
    public Guid? LinkedContentId { get; set; }
    public int SortOrder { get; set; }
}

public class PollDto
{
    public Guid Id { get; set; }
    public string Question { get; set; } = "";
    public string? Description { get; set; }
    public string Status { get; set; } = "open";
    public DateTime? EndsAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public int TotalVotes { get; set; }
    public Guid? MyOptionId { get; set; }   // the option the caller voted for, if any
    public List<PollOptionDto> Options { get; set; } = [];
}
