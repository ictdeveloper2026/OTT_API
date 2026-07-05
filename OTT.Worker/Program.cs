using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using OTT.Application.Services;
using OTT.Infrastructure.Data;
using OTT.Infrastructure.Services;
using Serilog;

// Dedicated transcoding worker. Runs a Hangfire server bound to the "transcoding" queue against
// the SAME SQL storage as the API, so FFmpeg jobs the API enqueues run out-of-process here.
var builder = Host.CreateApplicationBuilder(args);

Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
    .WriteTo.File("logs/ott-worker-.log", rollingInterval: RollingInterval.Day, retainedFileCountLimit: 14)
    .CreateLogger();
builder.Services.AddSerilog();

var connStr = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("ConnectionStrings:DefaultConnection is required");

builder.Services.AddDbContext<OttDbContext>(opts =>
    opts.UseSqlServer(connStr, o => o.EnableRetryOnFailure(3).CommandTimeout(300))); // long timeout for transcode DB work

builder.Services.AddHttpClient();

// ── AWS S3 / S3-compatible storage (same resolution rules as the API) ──
builder.Services.AddSingleton<IAmazonS3>(_ =>
{
    var cfg = builder.Configuration;
    var s3Config = new AmazonS3Config();
    var serviceUrl = cfg["AWS:S3:ServiceUrl"];
    if (!string.IsNullOrWhiteSpace(serviceUrl))
    {
        s3Config.ServiceURL = serviceUrl;
        s3Config.ForcePathStyle = !string.Equals(cfg["AWS:S3:ForcePathStyle"], "false", StringComparison.OrdinalIgnoreCase);
    }
    else
    {
        s3Config.RegionEndpoint = RegionEndpoint.GetBySystemName(cfg["AWS:Region"] ?? "ap-south-1");
    }

    var accessKey = cfg["AWS:AccessKey"];
    var secretKey = cfg["AWS:SecretKey"];
    var hasStaticKeys = !string.IsNullOrWhiteSpace(accessKey) && !string.IsNullOrWhiteSpace(secretKey)
        && accessKey != "YOUR_AWS_ACCESS_KEY";
    return hasStaticKeys
        ? new AmazonS3Client(new BasicAWSCredentials(accessKey, secretKey), s3Config)
        : new AmazonS3Client(s3Config);
});

// Services the transcoding job depends on (transitively VideoService → storage/CloudFront).
// IStorageService is the admin-configurable backend (S3/S3-compatible/local disk) VideoService
// actually reads/writes through; it must be registered here too since transcoding runs on this
// worker host, not the API process.
builder.Services.AddSingleton<IStorageService, DynamicStorageService>();
builder.Services.AddScoped<ICloudFrontCdnService, CloudFrontCdnService>();
builder.Services.AddScoped<IVideoJobQueue, HangfireVideoJobQueue>();
builder.Services.AddScoped<IVideoService, VideoService>();
builder.Services.AddTransient<TranscodingJob>();

// ── Hangfire server bound ONLY to the transcoding queue ──
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

var workerCount = builder.Configuration.GetValue<int?>("Worker:Count") ?? 2;
builder.Services.AddHangfireServer(opts =>
{
    opts.WorkerCount = workerCount;            // transcoding is CPU-heavy: keep concurrency low
    opts.Queues = new[] { JobQueues.Transcoding };
    opts.ServerName = $"ott-transcoder-{Environment.MachineName}";
});

Log.Information("OTT transcoding worker starting (queue='{Queue}', workers={Workers})",
    JobQueues.Transcoding, workerCount);

var host = builder.Build();
await host.RunAsync();
