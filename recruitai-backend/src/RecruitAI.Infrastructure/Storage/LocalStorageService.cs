using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using RecruitAI.Application.Interfaces;
using RecruitAI.Shared.Exceptions;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace RecruitAI.Infrastructure.Storage;

/// <summary>
/// Local disk implementation of IStorageService.
/// Saves files directly to the local file system.
/// </summary>
public sealed class LocalStorageService(
    IConfiguration configuration,
    ILogger<LocalStorageService> logger) : IStorageService
{
    private string BasePath => configuration["Storage:LocalPath"]
        ?? Path.Combine(AppContext.BaseDirectory, "uploads");

    public async Task<string> UploadAsync(
        Stream content,
        string fileName,
        string contentType,
        CancellationToken ct = default)
    {
        // Use the fileName as the key/relative path
        var key = fileName;
        var fullPath = Path.Combine(BasePath, key);

        try
        {
            // Ensure the directory exists
            var directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // Write content to file
            using var fileStream = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true);
            await content.CopyToAsync(fileStream, ct);

            logger.LogInformation("Uploaded file to local storage: {FullPath}", fullPath);
            return key;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Local storage upload failed for file {FileName}", fileName);
            throw new ExternalServiceException("Local Storage", $"Upload failed: {ex.Message}", ex);
        }
    }

    public Task<string> GetPresignedUrlAsync(string key, TimeSpan expiry, CancellationToken ct = default)
    {
        // Since we are running locally, we can return the local path.
        var fullPath = Path.GetFullPath(Path.Combine(BasePath, key));
        return Task.FromResult(fullPath);
    }

    public async Task<byte[]> DownloadAsync(string key, CancellationToken ct = default)
    {
        var fullPath = Path.Combine(BasePath, key);

        try
        {
            if (!File.Exists(fullPath))
            {
                throw new FileNotFoundException($"File not found in local storage: {key}", fullPath);
            }

            return await File.ReadAllBytesAsync(fullPath, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Local storage download failed for key {Key}", key);
            throw new ExternalServiceException("Local Storage", $"Download failed: {ex.Message}", ex);
        }
    }

    public Task DeleteAsync(string key, CancellationToken ct = default)
    {
        var fullPath = Path.Combine(BasePath, key);

        try
        {
            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
                logger.LogInformation("Deleted file from local storage: {FullPath}", fullPath);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Local storage deletion failed for key {Key}", key);
            throw new ExternalServiceException("Local Storage", $"Deletion failed: {ex.Message}", ex);
        }

        return Task.CompletedTask;
    }
}
