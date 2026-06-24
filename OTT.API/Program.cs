using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Hangfire;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using OTT.API.Hubs;
using OTT.API.Jobs;
using OTT.API.Middleware;
using OTT.Application.Services;
using OTT.Infrastructure.Data;
using OTT.Infrastructure.Services;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using StackExchange.Redis;
using System.Text;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// ── Serilog ───────────────────────────────────────────────────────────────────
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
    .WriteTo.File("logs/ott-.log", rollingInterval: RollingInterval.Day, retainedFileCountLimit: 14)
    .CreateLogger();
builder.Host.UseSerilog();

// ── Database ──────────────────────────────────────────────────────────────────
var connStr = builder.Configuration.GetConnectionString("DefaultConnection")!;
builder.Services.AddDbContext<OttDbContext>(opts =>
    opts.UseSqlServer(connStr,
        o => o.EnableRetryOnFailure(3).CommandTimeout(30)));

// ── Redis ─────────────────────────────────────────────────────────────────────
var redisConn = builder.Configuration.GetConnectionString("Redis")!;
builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
{
    var options = ConfigurationOptions.Parse(redisConn);
    options.AbortOnConnectFail = false; // don't crash at startup if Redis is down
    // Fail fast when Redis isn't running so cached endpoints (e.g. /api/plans) fall
    // through to the database in ~1s instead of hanging on connect/sync timeouts.
    options.ConnectTimeout = 800;
    options.SyncTimeout = 800;
    options.ConnectRetry = 0;
    return ConnectionMultiplexer.Connect(options);
});
builder.Services.AddSingleton<IRedisCacheService, RedisCacheService>();

// ── JWT Auth ──────────────────────────────────────────────────────────────────
var jwtSecret = builder.Configuration["Jwt:Secret"]!;

// Fail fast on a missing / weak / placeholder signing key. Allowed (with a warning)
// in Development only so local runs work; refuses to start in any other environment.
var jwtSecretIsWeak = string.IsNullOrWhiteSpace(jwtSecret)
    || jwtSecret.Length < 32
    || jwtSecret.Contains("CHANGE", StringComparison.OrdinalIgnoreCase)
    || jwtSecret.Contains("${");
if (jwtSecretIsWeak)
{
    if (builder.Environment.IsDevelopment())
        Log.Warning("Jwt:Secret is weak/placeholder — acceptable in Development only. Configure a strong 64+ char secret before deploying.");
    else
        throw new InvalidOperationException(
            "Jwt:Secret is missing, too short, or a placeholder. Configure a strong 64+ char secret via environment variables or a secret store before running outside Development.");
}
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(opts =>
    {
        opts.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
            ValidateIssuer = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidateAudience = true,
            ValidAudience = builder.Configuration["Jwt:Audience"],
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1)
        };
        opts.Events = new JwtBearerEvents
        {
            OnMessageReceived = ctx =>
            {
                var accessToken = ctx.Request.Query["access_token"];
                var path = ctx.HttpContext.Request.Path;
                if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
                    ctx.Token = accessToken;
                return Task.CompletedTask;
            }
        };
    });
builder.Services.AddAuthorization();

// ── Rate Limiting ─────────────────────────────────────────────────────────────
// Protects against brute force / credential stuffing / OTP guessing / email bombing.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // Strict per-IP window for auth endpoints (login, OTP, refresh, forgot-password).
    options.AddPolicy("auth", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));

    // A generous per-IP safety net for the whole API.
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 300,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
});

// ── Response compression ──────────────────────────────────────────────────────
// Brotli/gzip on catalog JSON — large list payloads shrink ~70-80% over the wire.
builder.Services.AddResponseCompression(opts =>
{
    opts.EnableForHttps = true;
    opts.Providers.Add<Microsoft.AspNetCore.ResponseCompression.BrotliCompressionProvider>();
    opts.Providers.Add<Microsoft.AspNetCore.ResponseCompression.GzipCompressionProvider>();
    opts.MimeTypes = Microsoft.AspNetCore.ResponseCompression.ResponseCompressionDefaults.MimeTypes
        .Concat(new[] { "application/json", "application/problem+json" });
});
builder.Services.Configure<Microsoft.AspNetCore.ResponseCompression.BrotliCompressionProviderOptions>(
    o => o.Level = System.IO.Compression.CompressionLevel.Fastest);
builder.Services.Configure<Microsoft.AspNetCore.ResponseCompression.GzipCompressionProviderOptions>(
    o => o.Level = System.IO.Compression.CompressionLevel.Fastest);

// ── Output caching (.NET 8) ───────────────────────────────────────────────────
// Anonymous catalog GETs are cached server-side per tenant for a short TTL. The default
// policy already skips requests with an Authorization header / Set-Cookie response, so
// per-user responses are never cached. The "catalog" policy varies by tenant + query.
builder.Services.AddOutputCache(opts =>
{
    opts.AddPolicy("catalog", b => b
        .Expire(TimeSpan.FromSeconds(60))
        .SetVaryByHeader("X-Tenant-ID", "Host")
        .SetVaryByQuery("*"));
});

// ── CORS ──────────────────────────────────────────────────────────────────────
// Set Cors:Origins (string[]) in production to restrict to known front-ends. When
// unset (local dev) it stays permissive so the web client / Swagger keep working.
var corsOrigins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>();
builder.Services.AddCors(opts => opts.AddPolicy("AllowAll", policy =>
{
    if (corsOrigins is { Length: > 0 })
        policy.WithOrigins(corsOrigins).AllowAnyHeader().AllowAnyMethod().AllowCredentials();
    else
        policy.AllowAnyHeader().AllowAnyMethod().AllowCredentials().SetIsOriginAllowed(_ => true);
}));

// ── SignalR ───────────────────────────────────────────────────────────────────
builder.Services.AddSignalR(opts =>
{
    opts.EnableDetailedErrors = builder.Environment.IsDevelopment();
    opts.MaximumReceiveMessageSize = 32 * 1024;
}).AddStackExchangeRedis(redisConn); // backplane so hubs work across multiple instances

// ── Hangfire ──────────────────────────────────────────────────────────────────
builder.Services.AddHangfire(cfg => cfg
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UseSqlServerStorage(connStr, new Hangfire.SqlServer.SqlServerStorageOptions
    {
        SchemaName = "HangFire",
        QueuePollInterval = TimeSpan.FromSeconds(5),
        PrepareSchemaIfNecessary = true
    }));
builder.Services.AddHangfireServer(opts => opts.WorkerCount = 4);

// ── AWS S3 / S3-compatible storage ─────────────────────────────────────────────
// Set AWS:S3:ServiceUrl to point at any S3-compatible provider (MinIO, Wasabi,
// DigitalOcean Spaces, Cloudflare R2). Leave it empty to use real AWS S3 by region.
builder.Services.AddSingleton<IAmazonS3>(_ =>
{
    var cfg = builder.Configuration;
    var s3Config = new AmazonS3Config();

    var serviceUrl = cfg["AWS:S3:ServiceUrl"];
    if (!string.IsNullOrWhiteSpace(serviceUrl))
    {
        s3Config.ServiceURL = serviceUrl;
        // path-style addressing is required by MinIO and most non-AWS providers
        s3Config.ForcePathStyle = !string.Equals(cfg["AWS:S3:ForcePathStyle"], "false", StringComparison.OrdinalIgnoreCase);
    }
    else
    {
        s3Config.RegionEndpoint = RegionEndpoint.GetBySystemName(cfg["AWS:Region"] ?? "ap-south-1");
    }

    var accessKey = cfg["AWS:AccessKey"];
    var secretKey = cfg["AWS:SecretKey"];
    var hasStaticKeys = !string.IsNullOrWhiteSpace(accessKey)
        && !string.IsNullOrWhiteSpace(secretKey)
        && accessKey != "YOUR_AWS_ACCESS_KEY";

    return hasStaticKeys
        ? new AmazonS3Client(new BasicAWSCredentials(accessKey, secretKey), s3Config)
        : new AmazonS3Client(s3Config); // fall back to default credential chain (IAM role / env vars)
});

// ── Application Services ──────────────────────────────────────────────────────
builder.Services.AddScoped<IJwtTokenService, JwtTokenService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IContentService, ContentService>();
builder.Services.AddScoped<IEntitlementService, EntitlementService>();
builder.Services.AddScoped<IStreamSessionService, StreamSessionService>();
builder.Services.AddScoped<ISubscriptionService, SubscriptionService>();
builder.Services.AddScoped<IVideoService, VideoService>();
builder.Services.AddScoped<ILiveStreamService, LiveStreamService>();
builder.Services.AddScoped<IOutboxService, OutboxService>();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<IS3StorageService, S3StorageService>();
builder.Services.AddScoped<ICloudFrontCdnService, CloudFrontCdnService>();
// Admin-configurable, hot-reloadable storage (DB-backed, falls back to appsettings)
builder.Services.AddSingleton<IStorageService, DynamicStorageService>();
// Admin-configurable, hot-reloadable per-tenant settings (payments, email, social, feature flags)
builder.Services.AddSingleton<IDynamicSettingsService, DynamicSettingsService>();
// IPTV channel sync (iptv-org)
builder.Services.AddScoped<IIptvSyncService, IptvSyncService>();
builder.Services.AddSingleton<IHubService, HubService>();
builder.Services.AddTransient<TranscodingJob>();
builder.Services.AddTransient<WriteBehindFlushJob>();
builder.Services.AddTransient<OutboxDispatchJob>();
builder.Services.AddTransient<SubscriptionRenewalJob>();
builder.Services.AddTransient<AnalyticsJob>();
builder.Services.AddTransient<CleanupJob>();

// ── OpenTelemetry (traces + metrics) ──────────────────────────────────────────
// Exports via OTLP only when OpenTelemetry:OtlpEndpoint is configured (e.g. a collector
// or Grafana/Tempo). Instrumentation is always collected; without an endpoint it's a no-op.
var otlpEndpoint = builder.Configuration["OpenTelemetry:OtlpEndpoint"];
builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService(serviceName: "ott-api"))
    .WithTracing(t =>
    {
        t.AddAspNetCoreInstrumentation().AddHttpClientInstrumentation();
        if (!string.IsNullOrWhiteSpace(otlpEndpoint))
            t.AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint));
    })
    .WithMetrics(m =>
    {
        m.AddAspNetCoreInstrumentation().AddHttpClientInstrumentation().AddRuntimeInstrumentation();
        if (!string.IsNullOrWhiteSpace(otlpEndpoint))
            m.AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint));
    });

// ── Health Checks ─────────────────────────────────────────────────────────────
builder.Services.AddHealthChecks()
    .AddSqlServer(connStr, name: "sqlserver", tags: new[] { "ready" })
    .AddRedis(redisConn, name: "redis", tags: new[] { "ready" });

// ── Swagger ───────────────────────────────────────────────────────────────────
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(opts =>
{
    opts.SwaggerDoc("v1", new OpenApiInfo { Title = "OTT Platform API", Version = "v1",
        Description = "Full-featured OTT Streaming Platform REST API" });
    opts.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header. Format: Bearer {token}",
        Name = "Authorization", In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey, Scheme = "Bearer"
    });
    opts.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme { Reference = new OpenApiReference
                { Type = ReferenceType.SecurityScheme, Id = "Bearer" } },
            Array.Empty<string>()
        }
    });
});

builder.Services.AddControllers()
    .AddJsonOptions(o =>
    {
        // EF entities returned raw can form Tenant<->children navigation cycles
        // (TenantMiddleware tracks the Tenant). Ignore cycles instead of throwing.
        o.JsonSerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
        o.JsonSerializerOptions.DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
    });
builder.Services.AddHttpContextAccessor();
builder.Services.AddMemoryCache();
builder.Services.AddHttpClient();

// ── Firebase (push notifications) ──────────────────────────────────────────────
// NotificationService sends via FirebaseMessaging.DefaultInstance, which requires an
// initialized FirebaseApp. Without this, every push call throws. Disabled gracefully
// when credentials aren't present (local dev).
var firebaseCredsPath = builder.Configuration["Firebase:CredentialsPath"];
if (!string.IsNullOrWhiteSpace(firebaseCredsPath) && File.Exists(firebaseCredsPath))
{
    if (FirebaseAdmin.FirebaseApp.DefaultInstance == null)
        FirebaseAdmin.FirebaseApp.Create(new FirebaseAdmin.AppOptions
        {
            Credential = Google.Apis.Auth.OAuth2.GoogleCredential.FromFile(firebaseCredsPath)
        });
    Log.Information("Firebase Admin initialized — push notifications enabled");
}
else
{
    Log.Warning("Firebase credentials not found (Firebase:CredentialsPath='{Path}'); push notifications disabled", firebaseCredsPath);
}

// ── Build App ─────────────────────────────────────────────────────────────────
var app = builder.Build();

app.UseResponseCompression();
app.UseMiddleware<ExceptionMiddleware>();
// Structured request logging (method, path, status, elapsed) enriched with trace + tenant.
app.UseSerilogRequestLogging(opts =>
{
    opts.EnrichDiagnosticContext = (diag, ctx) =>
    {
        diag.Set("TraceId", ctx.TraceIdentifier);
        if (ctx.Items.TryGetValue("TenantId", out var t) && t is not null)
            diag.Set("TenantId", t.ToString());
    };
});

// Baseline security headers on every response.
app.Use(async (ctx, next) =>
{
    var h = ctx.Response.Headers;
    h["X-Content-Type-Options"] = "nosniff";
    h["X-Frame-Options"] = "DENY";
    h["Referrer-Policy"] = "no-referrer";
    await next();
});

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "OTT Platform API v1"));
}

app.UseCors("AllowAll");
app.UseRateLimiter();
app.UseStaticFiles(); // serves wwwroot (incl. /uploads for the local storage provider)
app.UseOutputCache();
// ETag / conditional-GET for catalog JSON (runs after static files, so large file
// downloads are already served and never buffered here).
app.UseMiddleware<ETagMiddleware>();
// clients (and trips up self-signed dev certs). Keep the redirect for production only.
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
    app.UseHttpsRedirection();
}
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<TenantMiddleware>();
app.UseMiddleware<AuditMiddleware>(); // records admin mutations (after auth + tenant resolution)

app.MapControllers();
app.MapHub<WatchPartyHub>("/hubs/watchparty");
app.MapHub<LiveChatHub>("/hubs/livechat");
app.MapHub<NotificationHub>("/hubs/notifications");
// Liveness = process is up (no dependency checks). Readiness = dependencies reachable.
app.MapHealthChecks("/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions { Predicate = c => c.Tags.Contains("ready") });
app.MapHealthChecks("/health"); // legacy aggregate
app.MapGet("/", () => Results.Ok(new { status = "OTT Platform API", version = "1.0.0", timestamp = DateTime.UtcNow }));

if (app.Configuration.GetValue<bool>("Hangfire:Dashboard"))
{
    app.UseHangfireDashboard("/hangfire", new DashboardOptions
    {
        Authorization = [new Hangfire.Dashboard.LocalRequestsOnlyAuthorizationFilter()],
        DashboardTitle = "OTT Jobs Dashboard"
    });
}

// Auto-migrate
using (var scope = app.Services.CreateScope())
{
    try
    {
        var db = scope.ServiceProvider.GetRequiredService<OttDbContext>();
        await db.Database.MigrateAsync();
        Log.Information("Database migrations applied");

        await DbSeeder.SeedAsync(db, builder.Configuration);
        Log.Information("Database seeded (default tenant, branding, admin, plan)");
    }
    catch (Exception ex)
    {
        Log.Warning(ex, "Database migration/seed skipped: {Message}", ex.Message);
    }
}

try
{
    HangfireScheduler.ConfigureRecurringJobs();
}
catch (Exception ex)
{
    Log.Warning(ex, "Recurring job scheduling skipped: {Message}", ex.Message);
}
Log.Information("OTT Platform API started on {Env}", app.Environment.EnvironmentName);
await app.RunAsync();
