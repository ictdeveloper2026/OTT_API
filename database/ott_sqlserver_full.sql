IF OBJECT_ID(N'[__EFMigrationsHistory]') IS NULL
BEGIN
    CREATE TABLE [__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260617182555_InitialCreate'
)
BEGIN
    CREATE TABLE [AnalyticsEvents] (
        [Id] uniqueidentifier NOT NULL,
        [UserId] uniqueidentifier NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [EventType] nvarchar(100) NOT NULL,
        [ContentId] uniqueidentifier NULL,
        [EpisodeId] uniqueidentifier NULL,
        [WatchDurationSeconds] int NULL,
        [Platform] nvarchar(max) NULL,
        [Country] nvarchar(max) NULL,
        [DeviceType] nvarchar(max) NULL,
        [ExtraData] nvarchar(max) NULL,
        [CreatedAt] datetime2 NOT NULL DEFAULT (GETUTCDATE()),
        CONSTRAINT [PK_AnalyticsEvents] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260617182555_InitialCreate'
)
BEGIN
    CREATE TABLE [AppConfigs] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [Key] nvarchar(100) NOT NULL,
        [Value] nvarchar(max) NULL,
        [IsPublic] bit NOT NULL,
        [UpdatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_AppConfigs] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260617182555_InitialCreate'
)
BEGIN
    CREATE TABLE [CreatorApplications] (
        [Id] uniqueidentifier NOT NULL,
        [UserId] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [Status] nvarchar(max) NOT NULL,
        [ChannelName] nvarchar(max) NULL,
        [Bio] nvarchar(max) NULL,
        [Reason] nvarchar(max) NULL,
        [ReviewedAt] datetime2 NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_CreatorApplications] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260617182555_InitialCreate'
)
BEGIN
    CREATE TABLE [Downloads] (
        [Id] uniqueidentifier NOT NULL,
        [ProfileId] uniqueidentifier NOT NULL,
        [UserId] uniqueidentifier NULL,
        [ContentId] uniqueidentifier NOT NULL,
        [EpisodeId] uniqueidentifier NULL,
        [Status] nvarchar(max) NOT NULL,
        [LocalPath] nvarchar(max) NULL,
        [ExpiresAt] datetime2 NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_Downloads] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260617182555_InitialCreate'
)
BEGIN
    CREATE TABLE [Genres] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [Name] nvarchar(100) NOT NULL,
        [Slug] nvarchar(100) NOT NULL,
        [IconUrl] nvarchar(max) NULL,
        [SortOrder] int NOT NULL,
        CONSTRAINT [PK_Genres] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260617182555_InitialCreate'
)
BEGIN
    CREATE TABLE [LiveChatMessages] (
        [Id] uniqueidentifier NOT NULL,
        [StreamId] uniqueidentifier NOT NULL,
        [UserId] uniqueidentifier NOT NULL,
        [Message] nvarchar(max) NOT NULL,
        [IsDeleted] bit NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_LiveChatMessages] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260617182555_InitialCreate'
)
BEGIN
    CREATE TABLE [ParentalControls] (
        [Id] uniqueidentifier NOT NULL,
        [ProfileId] uniqueidentifier NOT NULL,
        [MaxRating] nvarchar(20) NOT NULL DEFAULT N'PG',
        [RequirePin] bit NOT NULL,
        [PinHash] nvarchar(max) NULL,
        [BlockViolence] bit NOT NULL,
        [BlockLanguage] bit NOT NULL,
        [BlockSexualContent] bit NOT NULL,
        [UpdatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_ParentalControls] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260617182555_InitialCreate'
)
BEGIN
    CREATE TABLE [PromoCodes] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [Code] nvarchar(100) NOT NULL,
        [DiscountType] nvarchar(20) NOT NULL DEFAULT N'percentage',
        [DiscountValue] decimal(10,2) NOT NULL,
        [MaxUses] int NULL,
        [UsedCount] int NOT NULL,
        [ExpiresAt] datetime2 NULL,
        [IsActive] bit NOT NULL,
        CONSTRAINT [PK_PromoCodes] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260617182555_InitialCreate'
)
BEGIN
    CREATE TABLE [StorageConfigurations] (
        [Id] uniqueidentifier NOT NULL,
        [Provider] nvarchar(max) NOT NULL,
        [BucketName] nvarchar(max) NULL,
        [Region] nvarchar(max) NULL,
        [AccessKey] nvarchar(max) NULL,
        [SecretKey] nvarchar(max) NULL,
        [ServiceUrl] nvarchar(max) NULL,
        [ForcePathStyle] bit NOT NULL,
        [PublicBaseUrl] nvarchar(max) NULL,
        [LocalRootPath] nvarchar(max) NULL,
        [IsActive] bit NOT NULL,
        [UpdatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_StorageConfigurations] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260617182555_InitialCreate'
)
BEGIN
    CREATE TABLE [Tags] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [Name] nvarchar(max) NOT NULL,
        [Slug] nvarchar(max) NOT NULL,
        CONSTRAINT [PK_Tags] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260617182555_InitialCreate'
)
BEGIN
    CREATE TABLE [Tenants] (
        [Id] uniqueidentifier NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        [Slug] nvarchar(100) NOT NULL,
        [Domain] nvarchar(255) NULL,
        [Plan] nvarchar(50) NOT NULL DEFAULT N'basic',
        [ContactEmail] nvarchar(max) NULL,
        [IsActive] bit NOT NULL,
        [CreatedAt] datetime2 NOT NULL DEFAULT (GETUTCDATE()),
        CONSTRAINT [PK_Tenants] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260617182555_InitialCreate'
)
BEGIN
    CREATE TABLE [UserRatings] (
        [ProfileId] uniqueidentifier NOT NULL,
        [ContentId] uniqueidentifier NOT NULL,
        [Rating] decimal(3,1) NOT NULL,
        [Review] nvarchar(max) NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_UserRatings] PRIMARY KEY ([ProfileId], [ContentId])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260617182555_InitialCreate'
)
BEGIN
    CREATE TABLE [Banners] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [Title] nvarchar(500) NOT NULL,
        [Subtitle] nvarchar(max) NULL,
        [Type] nvarchar(50) NOT NULL DEFAULT N'hero',
        [ImageUrl] nvarchar(max) NULL,
        [MobileImageUrl] nvarchar(max) NULL,
        [ActionType] nvarchar(max) NULL,
        [ActionValue] nvarchar(max) NULL,
        [CtaText] nvarchar(max) NULL,
        [CtaAction] nvarchar(max) NULL,
        [ContentId] uniqueidentifier NULL,
        [SortOrder] int NOT NULL,
        [IsActive] bit NOT NULL,
        [StartsAt] datetime2 NULL,
        [EndsAt] datetime2 NULL,
        CONSTRAINT [PK_Banners] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Banners_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260617182555_InitialCreate'
)
BEGIN
    CREATE TABLE [BrandingConfigs] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [AppName] nvarchar(200) NULL,
        [LogoUrl] nvarchar(max) NULL,
        [FaviconUrl] nvarchar(max) NULL,
        [SplashScreenUrl] nvarchar(max) NULL,
        [PrimaryColor] nvarchar(20) NULL DEFAULT N'#E50914',
        [SecondaryColor] nvarchar(20) NULL DEFAULT N'#141414',
        [AccentColor] nvarchar(20) NULL DEFAULT N'#FFFFFF',
        [BackgroundColor] nvarchar(max) NULL,
        [TextColor] nvarchar(max) NULL,
        [FontFamily] nvarchar(max) NULL,
        [CustomCss] nvarchar(max) NULL,
        [UpdatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_BrandingConfigs] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_BrandingConfigs_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260617182555_InitialCreate'
)
BEGIN
    CREATE TABLE [ContentRows] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [Title] nvarchar(200) NOT NULL,
        [RowType] nvarchar(50) NOT NULL DEFAULT N'manual',
        [SourceValue] nvarchar(max) NULL,
        [DisplayStyle] nvarchar(max) NOT NULL,
        [SortOrder] int NOT NULL,
        [MaxItems] int NOT NULL,
        [IsActive] bit NOT NULL,
        CONSTRAINT [PK_ContentRows] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_ContentRows_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260617182555_InitialCreate'
)
BEGIN
    CREATE TABLE [Contents] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [Title] nvarchar(500) NOT NULL,
        [Description] nvarchar(max) NULL,
        [Type] nvarchar(50) NOT NULL,
        [PosterUrl] nvarchar(max) NULL,
        [ThumbnailUrl] nvarchar(max) NULL,
        [BannerUrl] nvarchar(max) NULL,
        [TrailerUrl] nvarchar(max) NULL,
        [ReleaseYear] int NULL,
        [DurationSeconds] int NULL,
        [Language] nvarchar(max) NULL,
        [Country] nvarchar(max) NULL,
        [DirectorName] nvarchar(max) NULL,
        [AgeRating] nvarchar(20) NULL DEFAULT N'PG',
        [MonetizationModel] nvarchar(50) NOT NULL DEFAULT N'svod',
        [Price] decimal(10,2) NULL,
        [Status] nvarchar(50) NOT NULL DEFAULT N'draft',
        [IsFeatured] bit NOT NULL,
        [IsNew] bit NOT NULL,
        [IsTrending] bit NOT NULL,
        [AverageRating] decimal(3,2) NULL,
        [RatingCount] int NOT NULL,
        [ViewCount] bigint NOT NULL,
        [HlsUrl] nvarchar(max) NULL,
        [YoutubeId] nvarchar(max) NULL,
        [VimeoId] nvarchar(max) NULL,
        [VideoSourceType] nvarchar(max) NULL,
        [VideoSourceId] nvarchar(max) NULL,
        [DrmKeyId] nvarchar(max) NULL,
        [PublishedAt] datetime2 NULL,
        [IsDeleted] bit NOT NULL,
        [CreatedAt] datetime2 NOT NULL DEFAULT (GETUTCDATE()),
        [UpdatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_Contents] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Contents_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260617182555_InitialCreate'
)
BEGIN
    CREATE TABLE [LiveStreams] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [CreatedByUserId] uniqueidentifier NOT NULL,
        [Title] nvarchar(500) NOT NULL,
        [Description] nvarchar(max) NULL,
        [ThumbnailUrl] nvarchar(max) NULL,
        [StreamProvider] nvarchar(max) NOT NULL,
        [StreamKey] nvarchar(200) NULL,
        [AntMediaStreamId] nvarchar(max) NULL,
        [PlaybackUrl] nvarchar(max) NULL,
        [YoutubeStreamId] nvarchar(max) NULL,
        [VimeoStreamId] nvarchar(max) NULL,
        [Category] nvarchar(max) NULL,
        [ChatEnabled] bit NOT NULL,
        [IsScheduled] bit NOT NULL,
        [ScheduledAt] datetime2 NULL,
        [MonetizationModel] nvarchar(max) NOT NULL,
        [Status] nvarchar(50) NOT NULL DEFAULT N'offline',
        [ViewerCount] int NOT NULL,
        [PeakViewerCount] int NULL,
        [StartedAt] datetime2 NULL,
        [EndedAt] datetime2 NULL,
        [IsDeleted] bit NOT NULL,
        [CreatedAt] datetime2 NOT NULL DEFAULT (GETUTCDATE()),
        CONSTRAINT [PK_LiveStreams] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_LiveStreams_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260617182555_InitialCreate'
)
BEGIN
    CREATE TABLE [SubscriptionPlans] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        [Description] nvarchar(max) NULL,
        [Price] decimal(10,2) NOT NULL,
        [Currency] nvarchar(10) NOT NULL DEFAULT N'INR',
        [BillingCycle] nvarchar(50) NOT NULL DEFAULT N'monthly',
        [MaxProfiles] int NOT NULL,
        [MaxStreams] int NOT NULL,
        [AllowDownloads] bit NOT NULL,
        [AllowUhd] bit NOT NULL,
        [Features] nvarchar(max) NULL,
        [IsActive] bit NOT NULL,
        [IsPopular] bit NOT NULL,
        [RazorpayPlanId] nvarchar(max) NULL,
        [StripePriceId] nvarchar(max) NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_SubscriptionPlans] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_SubscriptionPlans_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260617182555_InitialCreate'
)
BEGIN
    CREATE TABLE [Users] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [Email] nvarchar(255) NOT NULL,
        [PasswordHash] nvarchar(max) NULL,
        [FirstName] nvarchar(max) NULL,
        [LastName] nvarchar(max) NULL,
        [AvatarUrl] nvarchar(max) NULL,
        [Phone] nvarchar(max) NULL,
        [Role] nvarchar(50) NOT NULL DEFAULT N'viewer',
        [AuthProvider] nvarchar(50) NOT NULL DEFAULT N'local',
        [IsEmailVerified] bit NOT NULL,
        [IsPhoneVerified] bit NOT NULL,
        [IsBlocked] bit NOT NULL,
        [IsActive] bit NOT NULL,
        [IsDeleted] bit NOT NULL,
        [SocialProvider] nvarchar(max) NULL,
        [SocialId] nvarchar(max) NULL,
        [Language] nvarchar(max) NULL,
        [Country] nvarchar(max) NULL,
        [LastLoginAt] datetime2 NULL,
        [CreatedAt] datetime2 NOT NULL DEFAULT (GETUTCDATE()),
        [UpdatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_Users] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Users_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260617182555_InitialCreate'
)
BEGIN
    CREATE TABLE [ContentRowItems] (
        [Id] uniqueidentifier NOT NULL,
        [ContentRowId] uniqueidentifier NOT NULL,
        [ContentId] uniqueidentifier NOT NULL,
        [SortOrder] int NOT NULL,
        CONSTRAINT [PK_ContentRowItems] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_ContentRowItems_ContentRows_ContentRowId] FOREIGN KEY ([ContentRowId]) REFERENCES [ContentRows] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260617182555_InitialCreate'
)
BEGIN
    CREATE TABLE [ContentAnalytics] (
        [Id] uniqueidentifier NOT NULL,
        [ContentId] uniqueidentifier NOT NULL,
        [Date] datetime2 NOT NULL,
        [Views] int NOT NULL,
        [TotalWatchSeconds] int NOT NULL,
        [UniqueViewers] int NOT NULL,
        CONSTRAINT [PK_ContentAnalytics] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_ContentAnalytics_Contents_ContentId] FOREIGN KEY ([ContentId]) REFERENCES [Contents] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260617182555_InitialCreate'
)
BEGIN
    CREATE TABLE [ContentCasts] (
        [Id] uniqueidentifier NOT NULL,
        [ContentId] uniqueidentifier NOT NULL,
        [ActorName] nvarchar(max) NOT NULL,
        [CharacterName] nvarchar(max) NULL,
        [Role] nvarchar(max) NULL,
        [PhotoUrl] nvarchar(max) NULL,
        [SortOrder] int NOT NULL,
        CONSTRAINT [PK_ContentCasts] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_ContentCasts_Contents_ContentId] FOREIGN KEY ([ContentId]) REFERENCES [Contents] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260617182555_InitialCreate'
)
BEGIN
    CREATE TABLE [ContentGenres] (
        [ContentId] uniqueidentifier NOT NULL,
        [GenreId] uniqueidentifier NOT NULL,
        CONSTRAINT [PK_ContentGenres] PRIMARY KEY ([ContentId], [GenreId]),
        CONSTRAINT [FK_ContentGenres_Contents_ContentId] FOREIGN KEY ([ContentId]) REFERENCES [Contents] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_ContentGenres_Genres_GenreId] FOREIGN KEY ([GenreId]) REFERENCES [Genres] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260617182555_InitialCreate'
)
BEGIN
    CREATE TABLE [ContentTags] (
        [ContentId] uniqueidentifier NOT NULL,
        [TagId] uniqueidentifier NOT NULL,
        CONSTRAINT [PK_ContentTags] PRIMARY KEY ([ContentId], [TagId]),
        CONSTRAINT [FK_ContentTags_Contents_ContentId] FOREIGN KEY ([ContentId]) REFERENCES [Contents] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_ContentTags_Tags_TagId] FOREIGN KEY ([TagId]) REFERENCES [Tags] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260617182555_InitialCreate'
)
BEGIN
    CREATE TABLE [Seasons] (
        [Id] uniqueidentifier NOT NULL,
        [ContentId] uniqueidentifier NOT NULL,
        [SeasonNumber] int NOT NULL,
        [Title] nvarchar(max) NULL,
        [Description] nvarchar(max) NULL,
        [PosterUrl] nvarchar(max) NULL,
        [ReleaseYear] int NULL,
        CONSTRAINT [PK_Seasons] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Seasons_Contents_ContentId] FOREIGN KEY ([ContentId]) REFERENCES [Contents] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260617182555_InitialCreate'
)
BEGIN
    CREATE TABLE [Watchlists] (
        [ProfileId] uniqueidentifier NOT NULL,
        [ContentId] uniqueidentifier NOT NULL,
        [AddedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_Watchlists] PRIMARY KEY ([ProfileId], [ContentId]),
        CONSTRAINT [FK_Watchlists_Contents_ContentId] FOREIGN KEY ([ContentId]) REFERENCES [Contents] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260617182555_InitialCreate'
)
BEGIN
    CREATE TABLE [WatchParties] (
        [Id] uniqueidentifier NOT NULL,
        [HostUserId] uniqueidentifier NOT NULL,
        [ContentId] uniqueidentifier NULL,
        [EpisodeId] uniqueidentifier NULL,
        [Code] nvarchar(20) NOT NULL,
        [IsPrivate] bit NOT NULL,
        [MaxMembers] int NOT NULL,
        [Status] nvarchar(50) NOT NULL DEFAULT N'active',
        [EndedAt] datetime2 NULL,
        [CreatedAt] datetime2 NOT NULL DEFAULT (GETUTCDATE()),
        CONSTRAINT [PK_WatchParties] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_WatchParties_Contents_ContentId] FOREIGN KEY ([ContentId]) REFERENCES [Contents] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260617182555_InitialCreate'
)
BEGIN
    CREATE TABLE [DeviceTokens] (
        [Id] uniqueidentifier NOT NULL,
        [UserId] uniqueidentifier NOT NULL,
        [Token] nvarchar(450) NOT NULL,
        [Platform] nvarchar(50) NOT NULL,
        [IsActive] bit NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NULL,
        CONSTRAINT [PK_DeviceTokens] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_DeviceTokens_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260617182555_InitialCreate'
)
BEGIN
    CREATE TABLE [Notifications] (
        [Id] uniqueidentifier NOT NULL,
        [UserId] uniqueidentifier NOT NULL,
        [Title] nvarchar(500) NOT NULL,
        [Body] nvarchar(max) NOT NULL,
        [Type] nvarchar(50) NOT NULL DEFAULT N'info',
        [ActionUrl] nvarchar(max) NULL,
        [ImageUrl] nvarchar(max) NULL,
        [IsRead] bit NOT NULL,
        [ReadAt] datetime2 NULL,
        [CreatedAt] datetime2 NOT NULL DEFAULT (GETUTCDATE()),
        CONSTRAINT [PK_Notifications] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Notifications_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260617182555_InitialCreate'
)
BEGIN
    CREATE TABLE [Payments] (
        [Id] uniqueidentifier NOT NULL,
        [UserId] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [Gateway] nvarchar(50) NOT NULL,
        [GatewayOrderId] nvarchar(max) NULL,
        [GatewayPaymentId] nvarchar(max) NULL,
        [Amount] decimal(10,2) NOT NULL,
        [Currency] nvarchar(10) NOT NULL DEFAULT N'INR',
        [Status] nvarchar(50) NOT NULL DEFAULT N'pending',
        [Description] nvarchar(max) NULL,
        [CreatedAt] datetime2 NOT NULL DEFAULT (GETUTCDATE()),
        CONSTRAINT [PK_Payments] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Payments_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260617182555_InitialCreate'
)
BEGIN
    CREATE TABLE [RefreshTokens] (
        [Id] uniqueidentifier NOT NULL,
        [UserId] uniqueidentifier NOT NULL,
        [Token] nvarchar(450) NOT NULL,
        [ExpiresAt] datetime2 NOT NULL,
        [IsRevoked] bit NOT NULL,
        [RevokedAt] datetime2 NULL,
        [DeviceInfo] nvarchar(max) NULL,
        [IpAddress] nvarchar(max) NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_RefreshTokens] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_RefreshTokens_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260617182555_InitialCreate'
)
BEGIN
    CREATE TABLE [UserProfiles] (
        [Id] uniqueidentifier NOT NULL,
        [UserId] uniqueidentifier NOT NULL,
        [Name] nvarchar(100) NOT NULL,
        [AvatarUrl] nvarchar(max) NULL,
        [IsDefault] bit NOT NULL,
        [MaturityLevel] nvarchar(20) NOT NULL DEFAULT N'all',
        [Language] nvarchar(max) NULL,
        [PinHash] nvarchar(max) NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_UserProfiles] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_UserProfiles_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260617182555_InitialCreate'
)
BEGIN
    CREATE TABLE [UserSubscriptions] (
        [Id] uniqueidentifier NOT NULL,
        [UserId] uniqueidentifier NOT NULL,
        [PlanId] uniqueidentifier NOT NULL,
        [Status] nvarchar(50) NOT NULL DEFAULT N'active',
        [StartDate] datetime2 NOT NULL,
        [EndDate] datetime2 NOT NULL,
        [AutoRenew] bit NOT NULL,
        [PaymentGateway] nvarchar(50) NULL,
        [GatewayPaymentId] nvarchar(max) NULL,
        [RazorpayOrderId] nvarchar(max) NULL,
        [RazorpaySubscriptionId] nvarchar(max) NULL,
        [CancelledAt] datetime2 NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_UserSubscriptions] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_UserSubscriptions_SubscriptionPlans_PlanId] FOREIGN KEY ([PlanId]) REFERENCES [SubscriptionPlans] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_UserSubscriptions_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260617182555_InitialCreate'
)
BEGIN
    CREATE TABLE [Episodes] (
        [Id] uniqueidentifier NOT NULL,
        [SeasonId] uniqueidentifier NOT NULL,
        [ContentId] uniqueidentifier NULL,
        [EpisodeNumber] int NOT NULL,
        [Title] nvarchar(500) NOT NULL,
        [Description] nvarchar(max) NULL,
        [ThumbnailUrl] nvarchar(max) NULL,
        [DurationSeconds] int NULL,
        [HlsUrl] nvarchar(max) NULL,
        [ExternalVideoUrl] nvarchar(max) NULL,
        [YoutubeId] nvarchar(max) NULL,
        [VimeoId] nvarchar(max) NULL,
        [IsFree] bit NOT NULL,
        [AiredAt] datetime2 NULL,
        CONSTRAINT [PK_Episodes] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Episodes_Contents_ContentId] FOREIGN KEY ([ContentId]) REFERENCES [Contents] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Episodes_Seasons_SeasonId] FOREIGN KEY ([SeasonId]) REFERENCES [Seasons] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260617182555_InitialCreate'
)
BEGIN
    CREATE TABLE [WatchPartyMembers] (
        [WatchPartyId] uniqueidentifier NOT NULL,
        [UserId] uniqueidentifier NOT NULL,
        [Id] uniqueidentifier NOT NULL,
        [JoinedAt] datetime2 NOT NULL,
        [LeftAt] datetime2 NULL,
        CONSTRAINT [PK_WatchPartyMembers] PRIMARY KEY ([WatchPartyId], [UserId]),
        CONSTRAINT [FK_WatchPartyMembers_WatchParties_WatchPartyId] FOREIGN KEY ([WatchPartyId]) REFERENCES [WatchParties] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260617182555_InitialCreate'
)
BEGIN
    CREATE TABLE [WatchHistories] (
        [Id] uniqueidentifier NOT NULL,
        [ProfileId] uniqueidentifier NOT NULL,
        [UserId] uniqueidentifier NULL,
        [ContentId] uniqueidentifier NOT NULL,
        [EpisodeId] uniqueidentifier NULL,
        [PositionSeconds] int NOT NULL,
        [TotalSeconds] int NULL,
        [IsCompleted] bit NOT NULL,
        [AddedAt] datetime2 NOT NULL,
        [LastWatchedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_WatchHistories] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_WatchHistories_Contents_ContentId] FOREIGN KEY ([ContentId]) REFERENCES [Contents] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_WatchHistories_UserProfiles_ProfileId] FOREIGN KEY ([ProfileId]) REFERENCES [UserProfiles] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260617182555_InitialCreate'
)
BEGIN
    CREATE TABLE [VideoAssets] (
        [Id] uniqueidentifier NOT NULL,
        [ContentId] uniqueidentifier NULL,
        [EpisodeId] uniqueidentifier NULL,
        [OriginalFileName] nvarchar(max) NULL,
        [OriginalKey] nvarchar(max) NULL,
        [HlsPath] nvarchar(max) NULL,
        [StorageProvider] nvarchar(50) NULL DEFAULT N's3',
        [Resolution] nvarchar(20) NULL,
        [ExternalProvider] nvarchar(50) NULL,
        [Status] nvarchar(50) NOT NULL DEFAULT N'pending',
        [DurationSeconds] int NULL,
        [OriginalWidth] int NULL,
        [OriginalHeight] int NULL,
        [ErrorMessage] nvarchar(max) NULL,
        [TranscodingStartedAt] datetime2 NULL,
        [TranscodedAt] datetime2 NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_VideoAssets] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_VideoAssets_Contents_ContentId] FOREIGN KEY ([ContentId]) REFERENCES [Contents] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_VideoAssets_Episodes_EpisodeId] FOREIGN KEY ([EpisodeId]) REFERENCES [Episodes] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260617182555_InitialCreate'
)
BEGIN
    CREATE TABLE [Subtitles] (
        [Id] uniqueidentifier NOT NULL,
        [ContentId] uniqueidentifier NULL,
        [AssetId] uniqueidentifier NULL,
        [Language] nvarchar(10) NOT NULL,
        [LanguageCode] nvarchar(max) NULL,
        [Label] nvarchar(100) NOT NULL,
        [FileUrl] nvarchar(max) NOT NULL,
        [Format] nvarchar(max) NOT NULL,
        [VideoAssetId] uniqueidentifier NULL,
        CONSTRAINT [PK_Subtitles] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Subtitles_VideoAssets_VideoAssetId] FOREIGN KEY ([VideoAssetId]) REFERENCES [VideoAssets] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260617182555_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_AnalyticsEvents_CreatedAt] ON [AnalyticsEvents] ([CreatedAt]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260617182555_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_AnalyticsEvents_EventType] ON [AnalyticsEvents] ([EventType]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260617182555_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_AppConfigs_TenantId_Key] ON [AppConfigs] ([TenantId], [Key]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260617182555_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Banners_TenantId] ON [Banners] ([TenantId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260617182555_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_BrandingConfigs_TenantId] ON [BrandingConfigs] ([TenantId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260617182555_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_ContentAnalytics_ContentId_Date] ON [ContentAnalytics] ([ContentId], [Date]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260617182555_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_ContentCasts_ContentId] ON [ContentCasts] ([ContentId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260617182555_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_ContentGenres_GenreId] ON [ContentGenres] ([GenreId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260617182555_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_ContentRowItems_ContentRowId] ON [ContentRowItems] ([ContentRowId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260617182555_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_ContentRows_TenantId] ON [ContentRows] ([TenantId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260617182555_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Contents_TenantId] ON [Contents] ([TenantId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260617182555_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_ContentTags_TagId] ON [ContentTags] ([TagId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260617182555_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_DeviceTokens_Token] ON [DeviceTokens] ([Token]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260617182555_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_DeviceTokens_UserId] ON [DeviceTokens] ([UserId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260617182555_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Episodes_ContentId] ON [Episodes] ([ContentId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260617182555_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Episodes_SeasonId] ON [Episodes] ([SeasonId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260617182555_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Genres_Name_TenantId] ON [Genres] ([Name], [TenantId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260617182555_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_LiveStreams_TenantId] ON [LiveStreams] ([TenantId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260617182555_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Notifications_UserId] ON [Notifications] ([UserId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260617182555_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_ParentalControls_ProfileId] ON [ParentalControls] ([ProfileId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260617182555_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Payments_UserId] ON [Payments] ([UserId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260617182555_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_PromoCodes_Code_TenantId] ON [PromoCodes] ([Code], [TenantId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260617182555_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_RefreshTokens_Token] ON [RefreshTokens] ([Token]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260617182555_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_RefreshTokens_UserId] ON [RefreshTokens] ([UserId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260617182555_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Seasons_ContentId] ON [Seasons] ([ContentId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260617182555_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_SubscriptionPlans_TenantId] ON [SubscriptionPlans] ([TenantId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260617182555_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Subtitles_VideoAssetId] ON [Subtitles] ([VideoAssetId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260617182555_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Tenants_Slug] ON [Tenants] ([Slug]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260617182555_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_UserProfiles_UserId] ON [UserProfiles] ([UserId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260617182555_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Users_Email_TenantId] ON [Users] ([Email], [TenantId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260617182555_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Users_TenantId] ON [Users] ([TenantId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260617182555_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_UserSubscriptions_PlanId] ON [UserSubscriptions] ([PlanId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260617182555_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_UserSubscriptions_UserId] ON [UserSubscriptions] ([UserId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260617182555_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_VideoAssets_ContentId] ON [VideoAssets] ([ContentId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260617182555_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_VideoAssets_EpisodeId] ON [VideoAssets] ([EpisodeId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260617182555_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_WatchHistories_ContentId] ON [WatchHistories] ([ContentId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260617182555_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_WatchHistories_ProfileId_ContentId] ON [WatchHistories] ([ProfileId], [ContentId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260617182555_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Watchlists_ContentId] ON [Watchlists] ([ContentId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260617182555_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_WatchParties_Code] ON [WatchParties] ([Code]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260617182555_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_WatchParties_ContentId] ON [WatchParties] ([ContentId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260617182555_InitialCreate'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260617182555_InitialCreate', N'8.0.0');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260620171707_AddIptvChannels'
)
BEGIN
    CREATE TABLE [IptvChannels] (
        [Id] uniqueidentifier NOT NULL,
        [ChannelId] nvarchar(max) NOT NULL,
        [Name] nvarchar(max) NOT NULL,
        [Country] nvarchar(max) NULL,
        [CountryName] nvarchar(max) NULL,
        [Languages] nvarchar(max) NULL,
        [Categories] nvarchar(max) NULL,
        [LogoUrl] nvarchar(max) NULL,
        [StreamUrl] nvarchar(max) NOT NULL,
        [Quality] nvarchar(max) NULL,
        [IsNsfw] bit NOT NULL,
        [UpdatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_IptvChannels] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260620171707_AddIptvChannels'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260620171707_AddIptvChannels', N'8.0.0');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260623182843_AddPaymentContentId'
)
BEGIN
    ALTER TABLE [Payments] ADD [ContentId] uniqueidentifier NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260623182843_AddPaymentContentId'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260623182843_AddPaymentContentId', N'8.0.0');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260624170638_AddPerformanceIndexes'
)
BEGIN
    DROP INDEX [IX_UserSubscriptions_UserId] ON [UserSubscriptions];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260624170638_AddPerformanceIndexes'
)
BEGIN
    DROP INDEX [IX_Contents_TenantId] ON [Contents];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260624170638_AddPerformanceIndexes'
)
BEGIN
    DECLARE @var0 sysname;
    SELECT @var0 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Payments]') AND [c].[name] = N'GatewayPaymentId');
    IF @var0 IS NOT NULL EXEC(N'ALTER TABLE [Payments] DROP CONSTRAINT [' + @var0 + '];');
    ALTER TABLE [Payments] ALTER COLUMN [GatewayPaymentId] nvarchar(450) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260624170638_AddPerformanceIndexes'
)
BEGIN
    CREATE INDEX [IX_WatchHistories_ProfileId_LastWatchedAt] ON [WatchHistories] ([ProfileId], [LastWatchedAt]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260624170638_AddPerformanceIndexes'
)
BEGIN
    CREATE INDEX [IX_UserSubscriptions_UserId_Status_EndDate] ON [UserSubscriptions] ([UserId], [Status], [EndDate]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260624170638_AddPerformanceIndexes'
)
BEGIN
    CREATE INDEX [IX_Payments_GatewayPaymentId] ON [Payments] ([GatewayPaymentId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260624170638_AddPerformanceIndexes'
)
BEGIN
    CREATE INDEX [IX_Payments_TenantId_Status_CreatedAt] ON [Payments] ([TenantId], [Status], [CreatedAt]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260624170638_AddPerformanceIndexes'
)
BEGIN
    CREATE INDEX [IX_Contents_TenantId_IsTrending_ViewCount] ON [Contents] ([TenantId], [IsTrending], [ViewCount]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260624170638_AddPerformanceIndexes'
)
BEGIN
    CREATE INDEX [IX_Contents_TenantId_Status] ON [Contents] ([TenantId], [Status]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260624170638_AddPerformanceIndexes'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260624170638_AddPerformanceIndexes', N'8.0.0');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260624181314_AddAuditLog'
)
BEGIN
    CREATE TABLE [AuditLogs] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [ActorUserId] uniqueidentifier NULL,
        [ActorEmail] nvarchar(256) NULL,
        [Action] nvarchar(10) NOT NULL,
        [Path] nvarchar(512) NOT NULL,
        [StatusCode] int NOT NULL,
        [IpAddress] nvarchar(64) NULL,
        [UserAgent] nvarchar(512) NULL,
        [CreatedAt] datetime2 NOT NULL DEFAULT (GETUTCDATE()),
        CONSTRAINT [PK_AuditLogs] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260624181314_AddAuditLog'
)
BEGIN
    CREATE INDEX [IX_AuditLogs_TenantId_CreatedAt] ON [AuditLogs] ([TenantId], [CreatedAt]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260624181314_AddAuditLog'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260624181314_AddAuditLog', N'8.0.0');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260624182854_AddOutboxMessages'
)
BEGIN
    CREATE TABLE [OutboxMessages] (
        [Id] uniqueidentifier NOT NULL,
        [Channel] nvarchar(20) NOT NULL,
        [Payload] nvarchar(max) NOT NULL,
        [Status] nvarchar(20) NOT NULL DEFAULT N'pending',
        [Attempts] int NOT NULL,
        [MaxAttempts] int NOT NULL,
        [LastError] nvarchar(2000) NULL,
        [CreatedAt] datetime2 NOT NULL DEFAULT (GETUTCDATE()),
        [NextAttemptAt] datetime2 NOT NULL,
        [SentAt] datetime2 NULL,
        CONSTRAINT [PK_OutboxMessages] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260624182854_AddOutboxMessages'
)
BEGIN
    CREATE INDEX [IX_OutboxMessages_Status_NextAttemptAt] ON [OutboxMessages] ([Status], [NextAttemptAt]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260624182854_AddOutboxMessages'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260624182854_AddOutboxMessages', N'8.0.0');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260630181926_AddAudioTracks'
)
BEGIN
    CREATE TABLE [AudioTracks] (
        [Id] uniqueidentifier NOT NULL,
        [ContentId] uniqueidentifier NULL,
        [AssetId] uniqueidentifier NULL,
        [Language] nvarchar(10) NOT NULL,
        [LanguageCode] nvarchar(10) NULL,
        [Label] nvarchar(100) NOT NULL,
        [TrackIndex] int NOT NULL,
        [IsDefault] bit NOT NULL,
        CONSTRAINT [PK_AudioTracks] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_AudioTracks_VideoAssets_AssetId] FOREIGN KEY ([AssetId]) REFERENCES [VideoAssets] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260630181926_AddAudioTracks'
)
BEGIN
    CREATE INDEX [IX_AudioTracks_AssetId] ON [AudioTracks] ([AssetId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260630181926_AddAudioTracks'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260630181926_AddAudioTracks', N'8.0.0');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260630190920_AddContentShortDescription'
)
BEGIN
    ALTER TABLE [Contents] ADD [ShortDescription] nvarchar(500) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260630190920_AddContentShortDescription'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260630190920_AddContentShortDescription', N'8.0.0');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260701160207_AddCommunityPolls'
)
BEGIN
    CREATE TABLE [Polls] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [CreatedByUserId] uniqueidentifier NOT NULL,
        [Question] nvarchar(300) NOT NULL,
        [Description] nvarchar(2000) NULL,
        [Status] nvarchar(20) NOT NULL DEFAULT N'open',
        [SourceSuggestionId] uniqueidentifier NULL,
        [EndsAt] datetime2 NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_Polls] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260701160207_AddCommunityPolls'
)
BEGIN
    CREATE TABLE [Suggestions] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [CreatedByUserId] uniqueidentifier NOT NULL,
        [Title] nvarchar(300) NOT NULL,
        [Description] nvarchar(2000) NULL,
        [Status] nvarchar(20) NOT NULL DEFAULT N'open',
        [UpvoteCount] int NOT NULL,
        [LinkedContentId] uniqueidentifier NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NULL,
        CONSTRAINT [PK_Suggestions] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260701160207_AddCommunityPolls'
)
BEGIN
    CREATE TABLE [PollOptions] (
        [Id] uniqueidentifier NOT NULL,
        [PollId] uniqueidentifier NOT NULL,
        [Text] nvarchar(300) NOT NULL,
        [LinkedContentId] uniqueidentifier NULL,
        [VoteCount] int NOT NULL,
        [SortOrder] int NOT NULL,
        CONSTRAINT [PK_PollOptions] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_PollOptions_Polls_PollId] FOREIGN KEY ([PollId]) REFERENCES [Polls] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260701160207_AddCommunityPolls'
)
BEGIN
    CREATE TABLE [PollVotes] (
        [Id] uniqueidentifier NOT NULL,
        [PollId] uniqueidentifier NOT NULL,
        [PollOptionId] uniqueidentifier NOT NULL,
        [UserId] uniqueidentifier NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_PollVotes] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_PollVotes_Polls_PollId] FOREIGN KEY ([PollId]) REFERENCES [Polls] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260701160207_AddCommunityPolls'
)
BEGIN
    CREATE TABLE [SuggestionVotes] (
        [Id] uniqueidentifier NOT NULL,
        [SuggestionId] uniqueidentifier NOT NULL,
        [UserId] uniqueidentifier NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_SuggestionVotes] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_SuggestionVotes_Suggestions_SuggestionId] FOREIGN KEY ([SuggestionId]) REFERENCES [Suggestions] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260701160207_AddCommunityPolls'
)
BEGIN
    CREATE INDEX [IX_PollOptions_PollId] ON [PollOptions] ([PollId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260701160207_AddCommunityPolls'
)
BEGIN
    CREATE INDEX [IX_Polls_TenantId_Status] ON [Polls] ([TenantId], [Status]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260701160207_AddCommunityPolls'
)
BEGIN
    CREATE UNIQUE INDEX [IX_PollVotes_PollId_UserId] ON [PollVotes] ([PollId], [UserId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260701160207_AddCommunityPolls'
)
BEGIN
    CREATE INDEX [IX_Suggestions_TenantId_Status_UpvoteCount] ON [Suggestions] ([TenantId], [Status], [UpvoteCount]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260701160207_AddCommunityPolls'
)
BEGIN
    CREATE UNIQUE INDEX [IX_SuggestionVotes_SuggestionId_UserId] ON [SuggestionVotes] ([SuggestionId], [UserId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260701160207_AddCommunityPolls'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260701160207_AddCommunityPolls', N'8.0.0');
END;
GO

COMMIT;
GO

