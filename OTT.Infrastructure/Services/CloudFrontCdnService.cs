using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Amazon.CloudFront;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace OTT.Infrastructure.Services;

public interface ICloudFrontCdnService
{
    string GetSignedUrl(string s3Key, int expiryMinutes = 360);
    string GetSignedHlsUrl(string contentId, string quality, int expiryMinutes = 360);
    string GetThumbnailUrl(string s3Key);
    string GetPublicUrl(string s3Key);
}

public class CloudFrontCdnService : ICloudFrontCdnService
{
    private readonly string _distributionDomain;
    private readonly string _keyPairId;
    private readonly string _privateKeyPath;
    private readonly ILogger<CloudFrontCdnService> _logger;

    public CloudFrontCdnService(IConfiguration config, ILogger<CloudFrontCdnService> logger)
    {
        _distributionDomain = config["AWS:CloudFront:Domain"] ?? throw new InvalidOperationException("CloudFront domain not configured");
        _keyPairId = config["AWS:CloudFront:KeyPairId"] ?? throw new InvalidOperationException("CloudFront KeyPairId not configured");
        _privateKeyPath = config["AWS:CloudFront:PrivateKeyPath"] ?? "/app/keys/cloudfront.pem";
        _logger = logger;
    }

    public string GetSignedUrl(string s3Key, int expiryMinutes = 360)
    {
        try
        {
            var url = $"https://{_distributionDomain}/{s3Key}";
            var expiry = DateTimeOffset.UtcNow.AddMinutes(expiryMinutes).ToUnixTimeSeconds();

            var policy = JsonSerializer.Serialize(new
            {
                Statement = new[]
                {
                    new
                    {
                        Resource = url,
                        Condition = new
                        {
                            DateLessThan = new { AWS_EpochTime = expiry }
                        }
                    }
                }
            });

            var encodedPolicy = Base64UrlEncode(Encoding.UTF8.GetBytes(policy));
            var signature = SignPolicy(policy);
            var encodedSignature = Base64UrlEncode(signature);

            return $"{url}?Policy={encodedPolicy}&Signature={encodedSignature}&Key-Pair-Id={_keyPairId}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate signed URL for key: {Key}", s3Key);
            return GetPublicUrl(s3Key);
        }
    }

    public string GetSignedHlsUrl(string contentId, string quality, int expiryMinutes = 360)
    {
        var hlsKey = $"transcoded/{contentId}/{quality}/playlist.m3u8";
        return GetSignedUrl(hlsKey, expiryMinutes);
    }

    public string GetThumbnailUrl(string s3Key)
    {
        return $"https://{_distributionDomain}/{s3Key}";
    }

    public string GetPublicUrl(string s3Key)
    {
        return $"https://{_distributionDomain}/{s3Key}";
    }

    private byte[] SignPolicy(string policy)
    {
        try
        {
            var pemContent = File.ReadAllText(_privateKeyPath);
            using var rsa = RSA.Create();
            rsa.ImportFromPem(pemContent);
            return rsa.SignData(Encoding.UTF8.GetBytes(policy), HashAlgorithmName.SHA1, RSASignaturePadding.Pkcs1);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "CloudFront signing failed, using unsigned URL");
            return Array.Empty<byte>();
        }
    }

    private static string Base64UrlEncode(byte[] data)
    {
        return Convert.ToBase64String(data)
            .Replace('+', '-')
            .Replace('=', '_')
            .Replace('/', '~');
    }
}
