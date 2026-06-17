using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Transfer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace OTT.Infrastructure.Services;

public interface IS3StorageService
{
    Task<string> UploadFileAsync(Stream fileStream, string key, string contentType, IProgress<long>? progress = null);
    Task<string> GetPresignedUploadUrlAsync(string key, string contentType, int expiryMinutes = 60);
    Task DeleteFileAsync(string key);
    Task<bool> FileExistsAsync(string key);
    Task<Stream> DownloadFileAsync(string key);
    string GetPublicUrl(string key);
    Task CopyFileAsync(string sourceKey, string destinationKey);
    Task<List<string>> ListFilesAsync(string prefix);
}

public class S3StorageService : IS3StorageService
{
    private readonly IAmazonS3 _s3Client;
    private readonly string _bucketName;
    private readonly string _region;
    private readonly ILogger<S3StorageService> _logger;

    public S3StorageService(IAmazonS3 s3Client, IConfiguration config, ILogger<S3StorageService> logger)
    {
        _s3Client = s3Client;
        _bucketName = config["AWS:S3:BucketName"] ?? throw new InvalidOperationException("S3 bucket not configured");
        _region = config["AWS:Region"] ?? "ap-south-1";
        _logger = logger;
    }

    public async Task<string> UploadFileAsync(Stream fileStream, string key, string contentType, IProgress<long>? progress = null)
    {
        try
        {
            var transferUtility = new TransferUtility(_s3Client);
            var uploadRequest = new TransferUtilityUploadRequest
            {
                BucketName = _bucketName,
                Key = key,
                InputStream = fileStream,
                ContentType = contentType,
                ServerSideEncryptionMethod = ServerSideEncryptionMethod.AES256,
                CannedACL = S3CannedACL.Private
            };

            if (progress != null)
            {
                long lastReported = 0;
                uploadRequest.UploadProgressEvent += (_, args) =>
                {
                    if (args.TransferredBytes - lastReported > 1_000_000)
                    {
                        progress.Report(args.TransferredBytes);
                        lastReported = args.TransferredBytes;
                    }
                };
            }

            await transferUtility.UploadAsync(uploadRequest);
            return GetPublicUrl(key);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upload file to S3: {Key}", key);
            throw;
        }
    }

    public async Task<string> GetPresignedUploadUrlAsync(string key, string contentType, int expiryMinutes = 60)
    {
        var request = new GetPreSignedUrlRequest
        {
            BucketName = _bucketName,
            Key = key,
            Verb = HttpVerb.PUT,
            Expires = DateTime.UtcNow.AddMinutes(expiryMinutes),
            ContentType = contentType
        };
        return await _s3Client.GetPreSignedURLAsync(request);
    }

    public async Task DeleteFileAsync(string key)
    {
        try
        {
            await _s3Client.DeleteObjectAsync(_bucketName, key);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete file from S3: {Key}", key);
            throw;
        }
    }

    public async Task<bool> FileExistsAsync(string key)
    {
        try
        {
            await _s3Client.GetObjectMetadataAsync(_bucketName, key);
            return true;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    public async Task<Stream> DownloadFileAsync(string key)
    {
        var response = await _s3Client.GetObjectAsync(_bucketName, key);
        return response.ResponseStream;
    }

    public string GetPublicUrl(string key)
    {
        return $"https://{_bucketName}.s3.{_region}.amazonaws.com/{key}";
    }

    public async Task CopyFileAsync(string sourceKey, string destinationKey)
    {
        await _s3Client.CopyObjectAsync(_bucketName, sourceKey, _bucketName, destinationKey);
    }

    public async Task<List<string>> ListFilesAsync(string prefix)
    {
        var keys = new List<string>();
        var request = new ListObjectsV2Request { BucketName = _bucketName, Prefix = prefix };

        ListObjectsV2Response response;
        do
        {
            response = await _s3Client.ListObjectsV2Async(request);
            keys.AddRange(response.S3Objects.Select(o => o.Key));
            request.ContinuationToken = response.NextContinuationToken;
        } while (response.IsTruncated);

        return keys;
    }
}
