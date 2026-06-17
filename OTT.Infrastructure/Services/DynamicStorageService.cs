using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Transfer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OTT.Infrastructure.Data;

namespace OTT.Infrastructure.Services;

// ── Public contract ────────────────────────────────────────────────────────────

public record PresignedUploadResult(string Url, string Key, Dictionary<string, string> Headers);

public record StorageConfigInfo(
    Guid? Id,
    string Provider,
    string? BucketName,
    string? Region,
    string? ServiceUrl,
    bool ForcePathStyle,
    string? PublicBaseUrl,
    string? LocalRootPath,
    bool CredentialsSet,
    DateTime? UpdatedAt,
    string Source);

/// <summary>
/// Storage facade whose backing provider is chosen at runtime from the active
/// <c>StorageConfiguration</c> row in the database, falling back to appsettings.
/// Call <see cref="Invalidate"/> after changing the config to hot-reload the provider.
/// </summary>
public interface IStorageService
{
    Task<string> UploadAsync(Stream stream, string key, string contentType, CancellationToken ct = default);
    Task<string> UploadPublicAsync(string key, Stream stream, string contentType, CancellationToken ct = default);
    Task<PresignedUploadResult> GetPresignedUploadUrlAsync(string key, string contentType, TimeSpan expiry);
    Task DeleteAsync(string key);
    Task<bool> ExistsAsync(string key);
    Task<Stream> DownloadAsync(string key);
    string GetPublicUrl(string key);
    Task<StorageConfigInfo> GetActiveConfigAsync();
    void Invalidate();
}

// ── Resolved settings + internal provider abstraction ───────────────────────────

internal sealed class ResolvedStorageSettings
{
    public Guid? Id { get; init; }
    public string Provider { get; init; } = "s3";
    public string? BucketName { get; init; }
    public string? Region { get; init; }
    public string? AccessKey { get; init; }
    public string? SecretKey { get; init; }
    public string? ServiceUrl { get; init; }
    public bool ForcePathStyle { get; init; } = true;
    public string? PublicBaseUrl { get; init; }
    public string? LocalRootPath { get; init; }
    public DateTime? UpdatedAt { get; init; }
    public string Source { get; init; } = "appsettings";

    public StorageConfigInfo ToInfo() => new(
        Id, Provider, BucketName, Region, ServiceUrl, ForcePathStyle,
        PublicBaseUrl, LocalRootPath,
        CredentialsSet: !string.IsNullOrWhiteSpace(AccessKey) && !string.IsNullOrWhiteSpace(SecretKey),
        UpdatedAt, Source);
}

internal interface IStorageProvider
{
    Task<string> UploadAsync(Stream stream, string key, string contentType, bool isPublic, CancellationToken ct);
    Task<PresignedUploadResult> GetPresignedUploadUrlAsync(string key, string contentType, TimeSpan expiry);
    Task DeleteAsync(string key);
    Task<bool> ExistsAsync(string key);
    Task<Stream> DownloadAsync(string key);
    string GetPublicUrl(string key);
}

// ── Dynamic resolver (singleton, caches the built provider) ─────────────────────

public class DynamicStorageService : IStorageService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _config;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<DynamicStorageService> _logger;
    private readonly object _lock = new();

    private IStorageProvider? _provider;
    private ResolvedStorageSettings? _settings;

    public DynamicStorageService(
        IServiceScopeFactory scopeFactory,
        IConfiguration config,
        ILoggerFactory loggerFactory)
    {
        _scopeFactory = scopeFactory;
        _config = config;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<DynamicStorageService>();
    }

    public void Invalidate()
    {
        lock (_lock)
        {
            _provider = null;
            _settings = null;
        }
        _logger.LogInformation("Storage provider cache invalidated; will reload on next use");
    }

    private IStorageProvider GetProvider()
    {
        if (_provider != null) return _provider;
        lock (_lock)
        {
            if (_provider != null) return _provider;
            var settings = LoadSettings();
            _provider = BuildProvider(settings);
            _settings = settings;
            _logger.LogInformation("Storage provider loaded: {Provider} (source: {Source})",
                settings.Provider, settings.Source);
            return _provider;
        }
    }

    private ResolvedStorageSettings LoadSettings()
    {
        // 1. Prefer the active DB row (admin-configured at runtime).
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<OttDbContext>();
            var row = db.StorageConfigurations
                .AsNoTracking()
                .Where(x => x.IsActive)
                .OrderByDescending(x => x.UpdatedAt)
                .FirstOrDefault();

            if (row != null)
            {
                return new ResolvedStorageSettings
                {
                    Id = row.Id,
                    Provider = string.IsNullOrWhiteSpace(row.Provider) ? "s3" : row.Provider.ToLowerInvariant(),
                    BucketName = row.BucketName,
                    Region = row.Region,
                    AccessKey = row.AccessKey,
                    SecretKey = row.SecretKey,
                    ServiceUrl = row.ServiceUrl,
                    ForcePathStyle = row.ForcePathStyle,
                    PublicBaseUrl = row.PublicBaseUrl,
                    LocalRootPath = row.LocalRootPath,
                    UpdatedAt = row.UpdatedAt,
                    Source = "database"
                };
            }
        }
        catch (Exception ex)
        {
            // Table may not exist yet, or DB is unreachable — fall back to config.
            _logger.LogWarning(ex, "Could not read StorageConfiguration from DB; falling back to appsettings");
        }

        // 2a. Fall back to a dedicated "Storage" section if present (supports any provider, incl. local).
        var storageProvider = _config["Storage:Provider"];
        if (!string.IsNullOrWhiteSpace(storageProvider))
        {
            return new ResolvedStorageSettings
            {
                Provider = storageProvider.ToLowerInvariant(),
                BucketName = _config["Storage:BucketName"],
                Region = _config["Storage:Region"] ?? "ap-south-1",
                AccessKey = _config["Storage:AccessKey"],
                SecretKey = _config["Storage:SecretKey"],
                ServiceUrl = _config["Storage:ServiceUrl"],
                ForcePathStyle = !string.Equals(_config["Storage:ForcePathStyle"], "false", StringComparison.OrdinalIgnoreCase),
                PublicBaseUrl = _config["Storage:PublicBaseUrl"],
                LocalRootPath = _config["Storage:LocalRootPath"],
                Source = "appsettings:Storage"
            };
        }

        // 2b. Legacy fall back to AWS:* keys.
        return new ResolvedStorageSettings
        {
            Provider = (_config["AWS:S3:ServiceUrl"] is { Length: > 0 }) ? "s3-compatible" : "s3",
            BucketName = _config["AWS:S3:BucketName"],
            Region = _config["AWS:Region"] ?? "ap-south-1",
            AccessKey = _config["AWS:AccessKey"],
            SecretKey = _config["AWS:SecretKey"],
            ServiceUrl = _config["AWS:S3:ServiceUrl"],
            ForcePathStyle = !string.Equals(_config["AWS:S3:ForcePathStyle"], "false", StringComparison.OrdinalIgnoreCase),
            PublicBaseUrl = _config["AWS:CloudFront:Domain"] is { Length: > 0 } d ? $"https://{d}" : null,
            Source = "appsettings:AWS"
        };
    }

    private IStorageProvider BuildProvider(ResolvedStorageSettings s)
    {
        if (s.Provider == "local")
        {
            var root = string.IsNullOrWhiteSpace(s.LocalRootPath)
                ? Path.Combine(AppContext.BaseDirectory, "wwwroot", "uploads")
                : s.LocalRootPath;
            var baseUrl = string.IsNullOrWhiteSpace(s.PublicBaseUrl) ? "/uploads" : s.PublicBaseUrl;
            return new LocalDiskStorageProvider(root, baseUrl,
                _loggerFactory.CreateLogger<LocalDiskStorageProvider>());
        }

        var s3Config = new AmazonS3Config();
        if (!string.IsNullOrWhiteSpace(s.ServiceUrl))
        {
            s3Config.ServiceURL = s.ServiceUrl;
            s3Config.ForcePathStyle = s.ForcePathStyle;
        }
        else
        {
            s3Config.RegionEndpoint = RegionEndpoint.GetBySystemName(s.Region ?? "ap-south-1");
        }

        var hasKeys = !string.IsNullOrWhiteSpace(s.AccessKey)
            && !string.IsNullOrWhiteSpace(s.SecretKey)
            && s.AccessKey != "YOUR_AWS_ACCESS_KEY";

        var client = hasKeys
            ? new AmazonS3Client(new BasicAWSCredentials(s.AccessKey, s.SecretKey), s3Config)
            : new AmazonS3Client(s3Config);

        var bucket = s.BucketName ?? throw new InvalidOperationException("Storage bucket name is not configured");
        return new S3StorageProvider(client, bucket, s, _loggerFactory.CreateLogger<S3StorageProvider>());
    }

    // ── IStorageService ─────────────────────────────────────────────────────────

    public Task<string> UploadAsync(Stream stream, string key, string contentType, CancellationToken ct = default)
        => GetProvider().UploadAsync(stream, key, contentType, isPublic: false, ct);

    public Task<string> UploadPublicAsync(string key, Stream stream, string contentType, CancellationToken ct = default)
        => GetProvider().UploadAsync(stream, key, contentType, isPublic: true, ct);

    public Task<PresignedUploadResult> GetPresignedUploadUrlAsync(string key, string contentType, TimeSpan expiry)
        => GetProvider().GetPresignedUploadUrlAsync(key, contentType, expiry);

    public Task DeleteAsync(string key) => GetProvider().DeleteAsync(key);
    public Task<bool> ExistsAsync(string key) => GetProvider().ExistsAsync(key);
    public Task<Stream> DownloadAsync(string key) => GetProvider().DownloadAsync(key);
    public string GetPublicUrl(string key) => GetProvider().GetPublicUrl(key);

    public Task<StorageConfigInfo> GetActiveConfigAsync()
    {
        GetProvider(); // ensure settings loaded
        return Task.FromResult((_settings ?? LoadSettings()).ToInfo());
    }
}

// ── S3 / S3-compatible provider ─────────────────────────────────────────────────

internal sealed class S3StorageProvider : IStorageProvider
{
    private readonly IAmazonS3 _s3;
    private readonly string _bucket;
    private readonly ResolvedStorageSettings _settings;
    private readonly ILogger<S3StorageProvider> _logger;

    public S3StorageProvider(IAmazonS3 s3, string bucket, ResolvedStorageSettings settings, ILogger<S3StorageProvider> logger)
    {
        _s3 = s3;
        _bucket = bucket;
        _settings = settings;
        _logger = logger;
    }

    public async Task<string> UploadAsync(Stream stream, string key, string contentType, bool isPublic, CancellationToken ct)
    {
        var transfer = new TransferUtility(_s3);
        var req = new TransferUtilityUploadRequest
        {
            BucketName = _bucket,
            Key = key,
            InputStream = stream,
            ContentType = contentType,
            CannedACL = isPublic ? S3CannedACL.PublicRead : S3CannedACL.Private
        };
        await transfer.UploadAsync(req, ct);
        return GetPublicUrl(key);
    }

    public Task<PresignedUploadResult> GetPresignedUploadUrlAsync(string key, string contentType, TimeSpan expiry)
    {
        var url = _s3.GetPreSignedURL(new GetPreSignedUrlRequest
        {
            BucketName = _bucket,
            Key = key,
            Verb = HttpVerb.PUT,
            Expires = DateTime.UtcNow.Add(expiry),
            ContentType = contentType
        });
        return Task.FromResult(new PresignedUploadResult(url, key,
            new Dictionary<string, string> { ["Content-Type"] = contentType }));
    }

    public Task DeleteAsync(string key) => _s3.DeleteObjectAsync(_bucket, key);

    public async Task<bool> ExistsAsync(string key)
    {
        try
        {
            await _s3.GetObjectMetadataAsync(_bucket, key);
            return true;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    public async Task<Stream> DownloadAsync(string key)
    {
        var resp = await _s3.GetObjectAsync(_bucket, key);
        return resp.ResponseStream;
    }

    public string GetPublicUrl(string key)
    {
        if (!string.IsNullOrWhiteSpace(_settings.PublicBaseUrl))
            return $"{_settings.PublicBaseUrl.TrimEnd('/')}/{key}";
        if (!string.IsNullOrWhiteSpace(_settings.ServiceUrl))
            return $"{_settings.ServiceUrl.TrimEnd('/')}/{_bucket}/{key}";
        return $"https://{_bucket}.s3.{_settings.Region ?? "ap-south-1"}.amazonaws.com/{key}";
    }
}

// ── Local disk provider ─────────────────────────────────────────────────────────

internal sealed class LocalDiskStorageProvider : IStorageProvider
{
    private readonly string _root;
    private readonly string _publicBaseUrl;
    private readonly ILogger<LocalDiskStorageProvider> _logger;

    public LocalDiskStorageProvider(string root, string publicBaseUrl, ILogger<LocalDiskStorageProvider> logger)
    {
        _root = root;
        _publicBaseUrl = publicBaseUrl.TrimEnd('/');
        _logger = logger;
        Directory.CreateDirectory(_root);
    }

    private string PathFor(string key) =>
        Path.Combine(_root, key.Replace('/', Path.DirectorySeparatorChar));

    public async Task<string> UploadAsync(Stream stream, string key, string contentType, bool isPublic, CancellationToken ct)
    {
        var path = PathFor(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var fs = File.Create(path);
        await stream.CopyToAsync(fs, ct);
        return GetPublicUrl(key);
    }

    public Task<PresignedUploadResult> GetPresignedUploadUrlAsync(string key, string contentType, TimeSpan expiry)
        => throw new NotSupportedException(
            "The local storage provider does not support direct-to-storage presigned uploads. Upload through the server (UploadAsync) instead.");

    public Task DeleteAsync(string key)
    {
        var path = PathFor(key);
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string key) => Task.FromResult(File.Exists(PathFor(key)));

    public Task<Stream> DownloadAsync(string key)
        => Task.FromResult<Stream>(File.OpenRead(PathFor(key)));

    public string GetPublicUrl(string key) => $"{_publicBaseUrl}/{key}";
}
