using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using RecruitAI.Application.Interfaces;
using RecruitAI.Shared.Exceptions;

namespace RecruitAI.Infrastructure.Storage;

/// <summary>
/// AWS S3 implementation of IStorageService.
/// Uses pre-signed URLs for direct client access to objects.
/// </summary>
public sealed class S3StorageService(
    IAmazonS3 s3Client,
    IConfiguration configuration,
    ILogger<S3StorageService> logger) : IStorageService
{
    private string BucketName => configuration["AWS:S3:BucketName"]
        ?? throw new InvalidOperationException("AWS:S3:BucketName is not configured.");

    public async Task<string> UploadAsync(
        Stream content,
        string fileName,
        string contentType,
        CancellationToken ct = default)
    {
        var key = $"{fileName}";

        var request = new PutObjectRequest
        {
            BucketName = BucketName,
            Key = key,
            InputStream = content,
            ContentType = contentType,
            ServerSideEncryptionMethod = ServerSideEncryptionMethod.AES256
        };

        try
        {
            await s3Client.PutObjectAsync(request, ct);
            logger.LogInformation("Uploaded object to S3: {Key}", key);
            return key;
        }
        catch (AmazonS3Exception ex)
        {
            logger.LogError(ex, "S3 upload failed for key {Key}", key);
            throw new ExternalServiceException("AWS S3", $"Upload failed: {ex.Message}", ex);
        }
    }

    public async Task<string> GetPresignedUrlAsync(string key, TimeSpan expiry, CancellationToken ct = default)
    {
        var request = new GetPreSignedUrlRequest
        {
            BucketName = BucketName,
            Key = key,
            Expires = DateTime.UtcNow.Add(expiry),
            Verb = HttpVerb.GET
        };

        var url = await s3Client.GetPreSignedURLAsync(request);
        return url;
    }

    public async Task<byte[]> DownloadAsync(string key, CancellationToken ct = default)
    {
        try
        {
            var response = await s3Client.GetObjectAsync(BucketName, key, ct);
            using var ms = new MemoryStream();
            await response.ResponseStream.CopyToAsync(ms, ct);
            return ms.ToArray();
        }
        catch (AmazonS3Exception ex)
        {
            logger.LogError(ex, "S3 download failed for key {Key}", key);
            throw new ExternalServiceException("AWS S3", $"Download failed: {ex.Message}", ex);
        }
    }

    public async Task DeleteAsync(string key, CancellationToken ct = default)
    {
        await s3Client.DeleteObjectAsync(BucketName, key, ct);
        logger.LogInformation("Deleted S3 object: {Key}", key);
    }
}
