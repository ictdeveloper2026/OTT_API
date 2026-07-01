using Microsoft.EntityFrameworkCore;
using OTT.Domain.Entities;

namespace OTT.Infrastructure.Data;

public class OttDbContext : DbContext
{
    public OttDbContext(DbContextOptions<OttDbContext> options) : base(options) { }

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<BrandingConfig> BrandingConfigs => Set<BrandingConfig>();
    public DbSet<User> Users => Set<User>();
    public DbSet<UserProfile> UserProfiles => Set<UserProfile>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<Content> Contents => Set<Content>();
    public DbSet<Season> Seasons => Set<Season>();
    public DbSet<Episode> Episodes => Set<Episode>();
    public DbSet<VideoAsset> VideoAssets => Set<VideoAsset>();
    public DbSet<Subtitle> Subtitles => Set<Subtitle>();
    public DbSet<AudioTrack> AudioTracks => Set<AudioTrack>();
    public DbSet<ContentCast> ContentCasts => Set<ContentCast>();
    public DbSet<Genre> Genres => Set<Genre>();
    public DbSet<ContentGenre> ContentGenres => Set<ContentGenre>();
    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<ContentTag> ContentTags => Set<ContentTag>();
    public DbSet<Banner> Banners => Set<Banner>();
    public DbSet<ContentRow> ContentRows => Set<ContentRow>();
    public DbSet<ContentRowItem> ContentRowItems => Set<ContentRowItem>();
    public DbSet<LiveStream> LiveStreams => Set<LiveStream>();
    public DbSet<LiveChatMessage> LiveChatMessages => Set<LiveChatMessage>();
    public DbSet<SubscriptionPlan> SubscriptionPlans => Set<SubscriptionPlan>();
    public DbSet<UserSubscription> UserSubscriptions => Set<UserSubscription>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<PromoCode> PromoCodes => Set<PromoCode>();
    public DbSet<WatchHistory> WatchHistories => Set<WatchHistory>();
    public DbSet<Watchlist> Watchlists => Set<Watchlist>();
    public DbSet<UserRating> UserRatings => Set<UserRating>();
    public DbSet<Download> Downloads => Set<Download>();
    public DbSet<WatchParty> WatchParties => Set<WatchParty>();
    public DbSet<WatchPartyMember> WatchPartyMembers => Set<WatchPartyMember>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<DeviceToken> DeviceTokens => Set<DeviceToken>();
    public DbSet<AppConfig> AppConfigs => Set<AppConfig>();
    public DbSet<AnalyticsEvent> AnalyticsEvents => Set<AnalyticsEvent>();
    public DbSet<ContentAnalytics> ContentAnalytics => Set<ContentAnalytics>();
    public DbSet<CreatorApplication> CreatorApplications => Set<CreatorApplication>();
    public DbSet<ParentalControl> ParentalControls => Set<ParentalControl>();
    public DbSet<StorageConfiguration> StorageConfigurations => Set<StorageConfiguration>();
    public DbSet<IptvChannel> IptvChannels => Set<IptvChannel>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<Suggestion> Suggestions => Set<Suggestion>();
    public DbSet<SuggestionVote> SuggestionVotes => Set<SuggestionVote>();
    public DbSet<Poll> Polls => Set<Poll>();
    public DbSet<PollOption> PollOptions => Set<PollOption>();
    public DbSet<PollVote> PollVotes => Set<PollVote>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Tenant
        modelBuilder.Entity<Tenant>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Slug).IsUnique();
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.Slug).HasMaxLength(100).IsRequired();
            e.Property(x => x.Domain).HasMaxLength(255);
            e.Property(x => x.Plan).HasMaxLength(50).HasDefaultValue("basic");
            e.Property(x => x.CreatedAt).HasDefaultValueSql("GETUTCDATE()");
        });

        // BrandingConfig
        modelBuilder.Entity<BrandingConfig>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasOne(x => x.Tenant).WithOne(t => t.BrandingConfig).HasForeignKey<BrandingConfig>(x => x.TenantId);
            e.Property(x => x.AppName).HasMaxLength(200);
            e.Property(x => x.PrimaryColor).HasMaxLength(20).HasDefaultValue("#E50914");
            e.Property(x => x.SecondaryColor).HasMaxLength(20).HasDefaultValue("#141414");
            e.Property(x => x.AccentColor).HasMaxLength(20).HasDefaultValue("#FFFFFF");
        });

        // User
        modelBuilder.Entity<User>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.Email, x.TenantId }).IsUnique();
            e.HasOne(x => x.Tenant).WithMany(t => t.Users).HasForeignKey(x => x.TenantId);
            e.Property(x => x.Email).HasMaxLength(255).IsRequired();
            e.Property(x => x.Role).HasMaxLength(50).HasDefaultValue("viewer");
            e.Property(x => x.AuthProvider).HasMaxLength(50).HasDefaultValue("local");
            e.Property(x => x.CreatedAt).HasDefaultValueSql("GETUTCDATE()");
        });

        // UserProfile
        modelBuilder.Entity<UserProfile>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasOne(x => x.User).WithMany(u => u.Profiles).HasForeignKey(x => x.UserId);
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            e.Property(x => x.MaturityLevel).HasMaxLength(20).HasDefaultValue("all");
        });

        // RefreshToken
        modelBuilder.Entity<RefreshToken>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasOne(x => x.User).WithMany(u => u.RefreshTokens).HasForeignKey(x => x.UserId);
            e.HasIndex(x => x.Token).IsUnique();
        });

        // Content
        modelBuilder.Entity<Content>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasOne(x => x.Tenant).WithMany(t => t.Contents).HasForeignKey(x => x.TenantId);
            e.Property(x => x.ShortDescription).HasMaxLength(500);
            // Hot read paths filter by tenant+status and sort by trending/views.
            e.HasIndex(x => new { x.TenantId, x.Status });
            e.HasIndex(x => new { x.TenantId, x.IsTrending, x.ViewCount });
            e.Property(x => x.Title).HasMaxLength(500).IsRequired();
            e.Property(x => x.Type).HasMaxLength(50).IsRequired();
            e.Property(x => x.Status).HasMaxLength(50).HasDefaultValue("draft");
            e.Property(x => x.MonetizationModel).HasMaxLength(50).HasDefaultValue("svod");
            e.Property(x => x.AgeRating).HasMaxLength(20).HasDefaultValue("PG");
            e.Property(x => x.AverageRating).HasPrecision(3, 2);
            e.Property(x => x.Price).HasPrecision(10, 2);
            e.Property(x => x.CreatedAt).HasDefaultValueSql("GETUTCDATE()");
        });

        // Season
        modelBuilder.Entity<Season>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasOne(x => x.Content).WithMany(c => c.Seasons).HasForeignKey(x => x.ContentId);
        });

        // Episode
        modelBuilder.Entity<Episode>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasOne(x => x.Season).WithMany(s => s.Episodes).HasForeignKey(x => x.SeasonId);
            e.HasOne(x => x.Content).WithMany().HasForeignKey(x => x.ContentId);
            e.Property(x => x.Title).HasMaxLength(500).IsRequired();
        });

        // VideoAsset
        modelBuilder.Entity<VideoAsset>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Resolution).HasMaxLength(20);
            e.Property(x => x.StorageProvider).HasMaxLength(50).HasDefaultValue("s3");
            e.Property(x => x.Status).HasMaxLength(50).HasDefaultValue("pending");
            e.Property(x => x.ExternalProvider).HasMaxLength(50);
        });

        // Subtitle
        modelBuilder.Entity<Subtitle>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Language).HasMaxLength(10).IsRequired();
            e.Property(x => x.Label).HasMaxLength(100);
        });

        // AudioTrack
        modelBuilder.Entity<AudioTrack>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Language).HasMaxLength(10).IsRequired();
            e.Property(x => x.LanguageCode).HasMaxLength(10);
            e.Property(x => x.Label).HasMaxLength(100);
            e.HasOne(x => x.VideoAsset)
                .WithMany(a => a.AudioTracks)
                .HasForeignKey(x => x.AssetId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Genre
        modelBuilder.Entity<Genre>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.Name, x.TenantId }).IsUnique();
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            e.Property(x => x.Slug).HasMaxLength(100);
        });

        // ContentGenre (many-to-many)
        modelBuilder.Entity<ContentGenre>(e =>
        {
            e.HasKey(x => new { x.ContentId, x.GenreId });
            e.HasOne(x => x.Content).WithMany(c => c.ContentGenres).HasForeignKey(x => x.ContentId);
            e.HasOne(x => x.Genre).WithMany(g => g.ContentGenres).HasForeignKey(x => x.GenreId);
        });

        // ContentTag (many-to-many)
        modelBuilder.Entity<ContentTag>(e =>
        {
            e.HasKey(x => new { x.ContentId, x.TagId });
        });

        // Banner
        modelBuilder.Entity<Banner>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasOne(x => x.Tenant).WithMany(t => t.Banners).HasForeignKey(x => x.TenantId);
            e.Property(x => x.Title).HasMaxLength(500);
            e.Property(x => x.Type).HasMaxLength(50).HasDefaultValue("hero");
        });

        // ContentRow
        modelBuilder.Entity<ContentRow>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasOne(x => x.Tenant).WithMany(t => t.ContentRows).HasForeignKey(x => x.TenantId);
            e.Property(x => x.Title).HasMaxLength(200).IsRequired();
            e.Property(x => x.RowType).HasMaxLength(50).HasDefaultValue("manual");
        });

        // ContentRowItem
        modelBuilder.Entity<ContentRowItem>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasOne(x => x.ContentRow).WithMany(r => r.Items).HasForeignKey(x => x.ContentRowId);
        });

        // LiveStream
        modelBuilder.Entity<LiveStream>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasOne(x => x.Tenant).WithMany(t => t.LiveStreams).HasForeignKey(x => x.TenantId);
            e.Property(x => x.Title).HasMaxLength(500).IsRequired();
            e.Property(x => x.Status).HasMaxLength(50).HasDefaultValue("offline");
            e.Property(x => x.StreamKey).HasMaxLength(200);
            e.Property(x => x.CreatedAt).HasDefaultValueSql("GETUTCDATE()");
        });

        // SubscriptionPlan
        modelBuilder.Entity<SubscriptionPlan>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasOne(x => x.Tenant).WithMany(t => t.SubscriptionPlans).HasForeignKey(x => x.TenantId);
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.BillingCycle).HasMaxLength(50).HasDefaultValue("monthly");
            e.Property(x => x.Price).HasPrecision(10, 2);
            e.Property(x => x.Currency).HasMaxLength(10).HasDefaultValue("INR");
        });

        // UserSubscription
        modelBuilder.Entity<UserSubscription>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasOne(x => x.User).WithMany(u => u.Subscriptions).HasForeignKey(x => x.UserId);
            e.HasOne(x => x.Plan).WithMany().HasForeignKey(x => x.PlanId);
            // Active-subscription lookups (entitlement checks, renewals).
            e.HasIndex(x => new { x.UserId, x.Status, x.EndDate });
            e.Property(x => x.Status).HasMaxLength(50).HasDefaultValue("active");
            e.Property(x => x.PaymentGateway).HasMaxLength(50);
        });

        // Payment
        modelBuilder.Entity<Payment>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasOne(x => x.User).WithMany(u => u.Payments).HasForeignKey(x => x.UserId);
            // Revenue dashboards + idempotency lookups by gateway payment id.
            e.HasIndex(x => new { x.TenantId, x.Status, x.CreatedAt });
            e.HasIndex(x => x.GatewayPaymentId);
            e.Property(x => x.Gateway).HasMaxLength(50).IsRequired();
            e.Property(x => x.Status).HasMaxLength(50).HasDefaultValue("pending");
            e.Property(x => x.Amount).HasPrecision(10, 2);
            e.Property(x => x.Currency).HasMaxLength(10).HasDefaultValue("INR");
            e.Property(x => x.CreatedAt).HasDefaultValueSql("GETUTCDATE()");
        });

        // PromoCode
        modelBuilder.Entity<PromoCode>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.Code, x.TenantId }).IsUnique();
            e.Property(x => x.Code).HasMaxLength(100).IsRequired();
            e.Property(x => x.DiscountType).HasMaxLength(20).HasDefaultValue("percentage");
            e.Property(x => x.DiscountValue).HasPrecision(10, 2);
        });

        // WatchHistory
        modelBuilder.Entity<WatchHistory>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasOne(x => x.UserProfile).WithMany().HasForeignKey(x => x.ProfileId);
            e.HasIndex(x => new { x.ProfileId, x.ContentId });
            // Continue-watching orders by most-recently-watched.
            e.HasIndex(x => new { x.ProfileId, x.LastWatchedAt });
        });

        // Watchlist
        modelBuilder.Entity<Watchlist>(e =>
        {
            e.HasKey(x => new { x.ProfileId, x.ContentId });
        });

        // UserRating
        modelBuilder.Entity<UserRating>(e =>
        {
            e.HasKey(x => new { x.ProfileId, x.ContentId });
            e.Property(x => x.Rating).HasPrecision(3, 1);
        });

        // WatchParty
        modelBuilder.Entity<WatchParty>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Code).IsUnique();
            e.Property(x => x.Code).HasMaxLength(20).IsRequired();
            e.Property(x => x.Status).HasMaxLength(50).HasDefaultValue("active");
            e.Property(x => x.CreatedAt).HasDefaultValueSql("GETUTCDATE()");
        });

        // WatchPartyMember
        modelBuilder.Entity<WatchPartyMember>(e =>
        {
            e.HasKey(x => new { x.WatchPartyId, x.UserId });
        });

        // Notification
        modelBuilder.Entity<Notification>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasOne(x => x.User).WithMany(u => u.Notifications).HasForeignKey(x => x.UserId);
            e.Property(x => x.Title).HasMaxLength(500).IsRequired();
            e.Property(x => x.Type).HasMaxLength(50).HasDefaultValue("info");
            e.Property(x => x.CreatedAt).HasDefaultValueSql("GETUTCDATE()");
        });

        // DeviceToken
        modelBuilder.Entity<DeviceToken>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasOne(x => x.User).WithMany(u => u.DeviceTokens).HasForeignKey(x => x.UserId);
            e.HasIndex(x => x.Token).IsUnique();
            e.Property(x => x.Platform).HasMaxLength(50);
        });

        // AppConfig
        modelBuilder.Entity<AppConfig>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.Key }).IsUnique();
            e.Property(x => x.Key).HasMaxLength(100).IsRequired();
        });

        // AnalyticsEvent
        modelBuilder.Entity<AnalyticsEvent>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.EventType);
            e.HasIndex(x => x.CreatedAt);
            e.Property(x => x.EventType).HasMaxLength(100).IsRequired();
            e.Property(x => x.CreatedAt).HasDefaultValueSql("GETUTCDATE()");
        });

        // ContentAnalytics
        modelBuilder.Entity<ContentAnalytics>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.ContentId, x.Date }).IsUnique();
        });

        // ParentalControl
        modelBuilder.Entity<ParentalControl>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.ProfileId).IsUnique();
            e.Property(x => x.MaxRating).HasMaxLength(20).HasDefaultValue("PG");
        });

        // AuditLog
        modelBuilder.Entity<AuditLog>(e =>
        {
            e.HasKey(x => x.Id);
            // Newest-first queries scoped per tenant (the admin audit screen).
            e.HasIndex(x => new { x.TenantId, x.CreatedAt });
            e.Property(x => x.Action).HasMaxLength(10).IsRequired();
            e.Property(x => x.Path).HasMaxLength(512).IsRequired();
            e.Property(x => x.ActorEmail).HasMaxLength(256);
            e.Property(x => x.IpAddress).HasMaxLength(64);
            e.Property(x => x.UserAgent).HasMaxLength(512);
            e.Property(x => x.CreatedAt).HasDefaultValueSql("GETUTCDATE()");
        });

        // OutboxMessage
        modelBuilder.Entity<OutboxMessage>(e =>
        {
            e.HasKey(x => x.Id);
            // The dispatch job polls due pending messages: (Status, NextAttemptAt).
            e.HasIndex(x => new { x.Status, x.NextAttemptAt });
            e.Property(x => x.Channel).HasMaxLength(20).IsRequired();
            e.Property(x => x.Status).HasMaxLength(20).IsRequired().HasDefaultValue("pending");
            e.Property(x => x.LastError).HasMaxLength(2000);
            e.Property(x => x.CreatedAt).HasDefaultValueSql("GETUTCDATE()");
        });

        // Community: suggestions + polls
        modelBuilder.Entity<Suggestion>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Title).HasMaxLength(300).IsRequired();
            e.Property(x => x.Description).HasMaxLength(2000);
            e.Property(x => x.Status).HasMaxLength(20).IsRequired().HasDefaultValue("open");
            e.HasIndex(x => new { x.TenantId, x.Status, x.UpvoteCount });
        });
        modelBuilder.Entity<SuggestionVote>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.SuggestionId, x.UserId }).IsUnique(); // one upvote per user
            e.HasOne(x => x.Suggestion).WithMany(s => s.Votes).HasForeignKey(x => x.SuggestionId);
        });
        modelBuilder.Entity<Poll>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Question).HasMaxLength(300).IsRequired();
            e.Property(x => x.Description).HasMaxLength(2000);
            e.Property(x => x.Status).HasMaxLength(20).IsRequired().HasDefaultValue("open");
            e.HasIndex(x => new { x.TenantId, x.Status });
        });
        modelBuilder.Entity<PollOption>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Text).HasMaxLength(300).IsRequired();
            e.HasOne(x => x.Poll).WithMany(p => p.Options).HasForeignKey(x => x.PollId);
        });
        modelBuilder.Entity<PollVote>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.PollId, x.UserId }).IsUnique(); // one vote per poll
            e.HasOne(x => x.Poll).WithMany(p => p.Votes).HasForeignKey(x => x.PollId);
        });

        // Soft delete filter
        modelBuilder.Entity<Content>().HasQueryFilter(x => !x.IsDeleted);
        modelBuilder.Entity<User>().HasQueryFilter(x => !x.IsDeleted);
        modelBuilder.Entity<LiveStream>().HasQueryFilter(x => !x.IsDeleted);

        // SQL Server (unlike MySQL) rejects multiple cascade paths / cycles (error 1785).
        // Disable cascade delete globally; deletes are handled explicitly in services.
        foreach (var fk in modelBuilder.Model.GetEntityTypes().SelectMany(e => e.GetForeignKeys()))
            fk.DeleteBehavior = DeleteBehavior.Restrict;
    }

    public override int SaveChanges()
    {
        UpdateTimestamps();
        return base.SaveChanges();
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        UpdateTimestamps();
        return base.SaveChangesAsync(cancellationToken);
    }

    private void UpdateTimestamps()
    {
        var entries = ChangeTracker.Entries()
            .Where(e => e.State == EntityState.Modified);

        foreach (var entry in entries)
        {
            var prop = entry.Entity.GetType().GetProperty("UpdatedAt");
            if (prop != null && prop.CanWrite && prop.PropertyType == typeof(DateTime))
                prop.SetValue(entry.Entity, DateTime.UtcNow);
        }
    }
}
