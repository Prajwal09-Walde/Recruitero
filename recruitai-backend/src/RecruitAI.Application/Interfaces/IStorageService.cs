namespace RecruitAI.Application.Interfaces;

/// <summary>Abstracts cloud object storage (S3 implementation).</summary>
public interface IStorageService
{
    /// <summary>Uploads a stream and returns the object key.</summary>
    Task<string> UploadAsync(Stream content, string fileName, string contentType, CancellationToken ct = default);

    /// <summary>Generates a pre-signed URL valid for the given duration.</summary>
    Task<string> GetPresignedUrlAsync(string key, TimeSpan expiry, CancellationToken ct = default);

    /// <summary>Downloads object content as a byte array.</summary>
    Task<byte[]> DownloadAsync(string key, CancellationToken ct = default);

    /// <summary>Deletes an object by key.</summary>
    Task DeleteAsync(string key, CancellationToken ct = default);
}
