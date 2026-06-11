using Hangfire;
using Microsoft.Extensions.Logging;
using RecruitAI.Application.Features.Resumes.Commands;
using RecruitAI.Application.Interfaces;
using RecruitAI.Shared.Constants;
using UglyToad.PdfPig;
using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;

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

            // 2. Download from S3
            logger.LogInformation("[ProcessResumeJob] Downloading file from S3 key {Key}", application.ResumeS3Key);
            var fileBytes = await storageService.DownloadAsync(application.ResumeS3Key, ct);

            // 3. Extract text depending on file format extension
            var ext = Path.GetExtension(application.ResumeS3Key).ToLowerInvariant();
            string extractedText;
            if (ext == ".docx")
            {
                extractedText = ExtractTextFromDocx(fileBytes);
                logger.LogInformation("[ProcessResumeJob] Extracted {Chars} chars from DOCX", extractedText.Length);
            }
            else if (ext == ".txt")
            {
                extractedText = ExtractTextFromTxt(fileBytes);
                logger.LogInformation("[ProcessResumeJob] Extracted {Chars} chars from TXT", extractedText.Length);
            }
            else
            {
                extractedText = ExtractText(fileBytes);
                logger.LogInformation("[ProcessResumeJob] Extracted {Chars} chars from PDF", extractedText.Length);
            }

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

    private static string ExtractTextFromDocx(byte[] docxBytes)
    {
        try
        {
            using var stream = new MemoryStream(docxBytes);
            using var archive = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Read);
            var entry = archive.GetEntry("word/document.xml");
            if (entry is null) return string.Empty;

            using var entryStream = entry.Open();
            var doc = XDocument.Load(entryStream);
            
            var paragraphs = doc.Descendants().Where(x => x.Name.LocalName == "p");
            var sb = new System.Text.StringBuilder();
            foreach (var p in paragraphs)
            {
                var tElements = p.Descendants().Where(x => x.Name.LocalName == "t");
                foreach (var t in tElements)
                {
                    sb.Append(t.Value);
                }
                sb.AppendLine();
            }
            return sb.ToString();
        }
        catch (Exception ex)
        {
            return $"[Error parsing DOCX: {ex.Message}]";
        }
    }

    private static string ExtractTextFromTxt(byte[] txtBytes)
    {
        return System.Text.Encoding.UTF8.GetString(txtBytes);
    }
}
