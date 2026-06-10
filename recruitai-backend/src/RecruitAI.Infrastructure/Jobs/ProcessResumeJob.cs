using Hangfire;
using Microsoft.Extensions.Logging;
using RecruitAI.Application.Features.Resumes.Commands;
using RecruitAI.Application.Interfaces;
using RecruitAI.Shared.Constants;
using UglyToad.PdfPig;

namespace RecruitAI.Infrastructure.Jobs;

/// <summary>
/// Hangfire background job: processes a single resume.
/// Flow: Download PDF → Extract text → AI pipeline → Update status → SignalR notifications
/// Retries 3 times with exponential backoff on failure.
/// </summary>
[AutomaticRetry(Attempts = 3, DelaysInSeconds = [60, 180, 600], OnAttemptsExceeded = AttemptsExceededAction.Fail)]
[Queue(HangfireQueues.Resumes)]
public sealed class ProcessResumeJob(
    IApplicationRepository applicationRepository,
    IStorageService storageService,
    IResumeProcessingService processingService,
    IRecruitmentHubContext hubContext,
    ILogger<ProcessResumeJob> logger) : IProcessResumeJob
{
    public async Task ExecuteAsync(Guid applicationId, CancellationToken ct)
    {
        logger.LogInformation("[ProcessResumeJob] Starting for application {AppId}", applicationId);

        var application = await applicationRepository.GetByIdAsync(applicationId, ct);
        if (application is null)
        {
            logger.LogError("[ProcessResumeJob] Application {AppId} not found — skipping", applicationId);
            return;
        }

        var candidateName = application.Candidate?.FullName ?? "Unknown";

        try
        {
            // 1. Transition to Processing
            application.MarkProcessing();
            await applicationRepository.SaveChangesAsync(ct);
            await hubContext.NotifyProcessingStartedAsync(application.JobId, applicationId, candidateName);

            // 2. Download PDF from S3
            logger.LogInformation("[ProcessResumeJob] Downloading PDF from S3 key {Key}", application.ResumeS3Key);
            var pdfBytes = await storageService.DownloadAsync(application.ResumeS3Key, ct);

            // 3. Extract text using PdfPig
            var extractedText = ExtractText(pdfBytes);
            logger.LogInformation("[ProcessResumeJob] Extracted {Chars} chars from PDF", extractedText.Length);

            // 4. Run AI pipeline
            var result = await processingService.ProcessAsync(applicationId, extractedText, ct);

            if (!result.Success)
                throw new InvalidOperationException(result.Error ?? "Processing returned failure");

            // 5. Mark scored
            application.MarkScored(result.FitScore, result.Rank);
            await applicationRepository.SaveChangesAsync(ct);

            await hubContext.NotifyFitScoreReadyAsync(
                application.JobId, applicationId, candidateName, result.FitScore, result.Rank);

            logger.LogInformation(
                "[ProcessResumeJob] Application {AppId} scored: {Score} (rank #{Rank})",
                applicationId, result.FitScore, result.Rank);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[ProcessResumeJob] Failed for application {AppId}", applicationId);

            application.MarkFailed(ex.Message);
            await applicationRepository.SaveChangesAsync(ct);

            await hubContext.NotifyProcessingFailedAsync(
                application.JobId, applicationId, candidateName, ex.Message);

            throw; // Re-throw so Hangfire applies retry policy
        }
    }

    private static string ExtractText(byte[] pdfBytes)
    {
        using var document = PdfDocument.Open(pdfBytes);
        var sb = new System.Text.StringBuilder();
        foreach (var page in document.GetPages())
            sb.AppendLine(page.Text);
        return sb.ToString();
    }
}
