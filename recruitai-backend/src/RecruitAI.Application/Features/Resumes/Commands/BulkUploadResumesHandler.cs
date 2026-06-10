using Hangfire;
using MediatR;
using Microsoft.Extensions.Logging;
using RecruitAI.Application.Interfaces;
using RecruitAI.Domain.Entities;
using RecruitAI.Shared.Exceptions;

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

        // 2. Process each file concurrently (sequential for simplicity; parallelize if needed)
        foreach (var file in request.Files)
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
