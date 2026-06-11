using Hangfire;
using MediatR;
using Microsoft.Extensions.Logging;
using RecruitAI.Application.Interfaces;
using RecruitAI.Domain.Entities;
using RecruitAI.Shared.Exceptions;
using Microsoft.AspNetCore.Http;
using System.IO;
using System.IO.Compression;

namespace RecruitAI.Application.Features.Resumes.Commands;

/// <summary>
/// Handles BulkUploadResumesCommand:
/// 1. Verifies job exists
/// 2. For each PDF: uploads to S3, creates Candidate + Application, enqueues Hangfire job
/// 3. Broadcasts ResumeUploaded events via SignalR
/// 4. Returns 202 with applicationIds and estimated processing time
/// </summary>
public sealed class BulkUploadResumesHandler(
    IJobRepository jobRepository,
    ICandidateRepository candidateRepository,
    IApplicationRepository applicationRepository,
    IStorageService storageService,
    IBackgroundJobClient backgroundJobClient,
    IRecruitmentHubContext hubContext,
    ILogger<BulkUploadResumesHandler> logger)
    : IRequestHandler<BulkUploadResumesCommand, BulkUploadResumesResult>
{
    public async Task<BulkUploadResumesResult> Handle(
        BulkUploadResumesCommand request,
        CancellationToken cancellationToken)
    {
        // 1. Validate job existence
        var jobExists = await jobRepository.ExistsAsync(request.JobId, cancellationToken);
        if (!jobExists)
            throw new NotFoundException(nameof(Domain.Entities.Job), request.JobId);

        var applicationIds = new List<Guid>();
        var filesToProcess = new List<IFormFile>();

        // 2a. Pre-process and extract zip files
        foreach (var file in request.Files)
        {
            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (ext == ".zip")
            {
                try
                {
                    using var stream = file.OpenReadStream();
                    using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
                    foreach (var entry in archive.Entries)
                    {
                        if (string.IsNullOrEmpty(entry.Name) || entry.Length == 0)
                            continue;

                        var entryExt = Path.GetExtension(entry.Name).ToLowerInvariant();
                        if (entryExt == ".pdf" || entryExt == ".docx" || entryExt == ".txt")
                        {
                            using var entryStream = entry.Open();
                            using var ms = new MemoryStream();
                            await entryStream.CopyToAsync(ms, cancellationToken);
                            var bytes = ms.ToArray();

                            var contentType = entryExt switch
                            {
                                ".pdf" => "application/pdf",
                                ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                                ".txt" => "text/plain",
                                _ => "application/octet-stream"
                            };

                            filesToProcess.Add(new InMemoryFormFile(bytes, entry.Name, contentType));
                        }
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to extract ZIP file {FileName}", file.FileName);
                }
            }
            else
            {
                filesToProcess.Add(file);
            }
        }

        // 2b. Process each file (PDF, DOCX, TXT)
        foreach (var file in filesToProcess)
        {
            try
            {
                // 2a. Upload to S3
                await using var stream = file.OpenReadStream();
                var s3Key = await storageService.UploadAsync(
                    stream,
                    $"resumes/{request.JobId}/{Guid.NewGuid()}_{file.FileName}",
                    file.ContentType,
                    cancellationToken);

                logger.LogInformation("Uploaded resume {FileName} to S3 key {Key}", file.FileName, s3Key);

                // 2b. Derive candidate name from filename (real impl: parse PDF metadata)
                var candidateName = Path.GetFileNameWithoutExtension(file.FileName)
                    .Replace("_", " ")
                    .Replace("-", " ");
                var candidateEmail = !string.IsNullOrWhiteSpace(request.CandidateEmail)
                    ? request.CandidateEmail
                    : $"{Guid.NewGuid()}@unknown.recruitai.io"; // Extracted by AI later

                // 2c. Upsert Candidate
                var candidate = await candidateRepository.GetByEmailAsync(candidateEmail, cancellationToken);
                if (candidate is null)
                {
                    candidate = new Candidate(candidateName, candidateEmail);
                    await candidateRepository.AddAsync(candidate, cancellationToken);
                }

                // 2d. Create Application (Status = Queued)
                var application = new Domain.Entities.Application(request.JobId, candidate.Id, s3Key);
                await applicationRepository.AddAsync(application, cancellationToken);
                await applicationRepository.SaveChangesAsync(cancellationToken);

                applicationIds.Add(application.Id);

                // 2e. Enqueue Hangfire background job
                backgroundJobClient.Enqueue<IProcessResumeJob>(
                    job => job.ExecuteAsync(application.Id, CancellationToken.None));

                // 2f. Broadcast ResumeUploaded via SignalR
                await hubContext.NotifyResumeUploadedAsync(
                    request.JobId,
                    application.Id,
                    candidateName,
                    DateTime.UtcNow);

                logger.LogInformation(
                    "Application {AppId} created for job {JobId}, Hangfire job enqueued",
                    application.Id, request.JobId);
            }
            catch (Exception ex) when (ex is not NotFoundException)
            {
                logger.LogError(ex, "Failed to process file {FileName}", file.FileName);
                // Continue processing remaining files; partial success is acceptable
            }
        }

        // ~2 minutes per resume for AI processing
        var estimatedMinutes = applicationIds.Count * 2;
        return new BulkUploadResumesResult(
            request.JobId,
            applicationIds,
            $"~{estimatedMinutes} minutes");
    }
}

/// <summary>Marker interface for the Hangfire job to avoid Infrastructure dependency here.</summary>
public interface IProcessResumeJob
{
    Task ExecuteAsync(Guid applicationId, CancellationToken ct);
}

public sealed class InMemoryFormFile : IFormFile
{
    private readonly byte[] _content;
    public InMemoryFormFile(byte[] content, string fileName, string contentType)
    {
        _content = content;
        FileName = fileName;
        ContentType = contentType;
        Length = content.Length;
    }

    public string ContentType { get; }
    public string ContentDisposition => $"form-data; name=\"files\"; filename=\"{FileName}\"";
    public IHeaderDictionary Headers => new HeaderDictionary();
    public long Length { get; }
    public string Name => "files";
    public string FileName { get; }

    public Stream OpenReadStream() => new MemoryStream(_content);
    public void CopyTo(Stream target) => new MemoryStream(_content).CopyTo(target);
    public Task CopyToAsync(Stream target, CancellationToken cancellationToken = default) => new MemoryStream(_content).CopyToAsync(target, cancellationToken);
}
