using Hangfire;
using Microsoft.Extensions.Logging;
using RecruitAI.Application.Interfaces;
using RecruitAI.Shared.Constants;

namespace RecruitAI.Infrastructure.Jobs;

/// <summary>
/// Hangfire job that orchestrates the full AI processing pipeline after resume text extraction:
///   ProcessResumeJob calls IResumeProcessingService.ProcessAsync()
///   → which calls this job internally via Hangfire continuation
///
/// Full sequence:
///   1. Embed resume chunks (IResumeEmbeddingService)
///   2. Score candidate fit (IFitScoringService)
///   3. Generate ranking narrative (ICandidateRankingService)
///   4. Generate interview kit (IInterviewKitGenerationService)
///   5. Enqueue webhook dispatch as continuation
/// </summary>
[AutomaticRetry(Attempts = 2, DelaysInSeconds = [120, 600])]
[Queue(HangfireQueues.Resumes)]
public sealed class GenerateRankingAndKitJob(
    IResumeEmbeddingService embeddingService,
    IFitScoringService fitScoringService,
    ICandidateRankingService rankingService,
    IInterviewKitGenerationService kitService,
    IApplicationRepository applicationRepository,
    IJobPostingRepository jobPostingRepository,
    IRecruitmentHubContext hubContext,
    IBackgroundJobClient backgroundJobClient,
    ILogger<GenerateRankingAndKitJob> logger)
{
    public async Task ExecuteAsync(
        Guid applicationId,
        string extractedResumeText,
        CancellationToken ct = default)
    {
        logger.LogInformation("[AIOrchestration] Starting pipeline for application {AppId}", applicationId);

        var application = await applicationRepository.GetByIdAsync(applicationId, ct)
            ?? throw new InvalidOperationException($"Application {applicationId} not found");

        var jobPosting = await jobPostingRepository.GetByIdAsync(application.JobId, ct)
            ?? throw new InvalidOperationException($"JobPosting {application.JobId} not found");

        var candidateName = application.Candidate?.FullName ?? "Unknown";

        // ── Step 1: Embed resume chunks ─────────────────────────────────────────
        logger.LogInformation("[AIOrchestration] Step 1: Embedding resume chunks");
        var embeddingResult = await embeddingService.ProcessResumeAsync(
            application.CandidateId,
            applicationId,
            application.JobId,
            extractedResumeText,
            ct);

        // ── Step 2: Score fit ────────────────────────────────────────────────────
        logger.LogInformation("[AIOrchestration] Step 2: Computing fit score");
        var fitResult = await fitScoringService.ScoreAsync(
            application.JobId,
            application.CandidateId,
            applicationId,
            ct);

        await hubContext.NotifyFitScoreReadyAsync(
            application.JobId, applicationId, candidateName,
            fitResult.FitScore, fitResult.Rank);

        // ── Step 3: Build ranking context ────────────────────────────────────────
        var rankingContext = new RankingContext(
            ApplicationId: applicationId,
            JobId: application.JobId,
            CandidateId: application.CandidateId,
            JobTitle: jobPosting.Title,
            CandidateName: candidateName,
            ExperienceYears: embeddingResult.Metadata.TotalExperienceYears,
            FitScore: fitResult.FitScore,
            RequiredSkills: jobPosting.SkillGraph?.RequiredSkills.Select(s => s.Skill).ToList() ?? [],
            TopChunks: fitResult.Ranking.TopChunks,
            SkillMatches: fitResult.Ranking.SkillMatches,
            Seniority: jobPosting.SkillGraph?.Seniority ?? "mid");

        // ── Step 4: Generate ranking narrative ───────────────────────────────────
        logger.LogInformation("[AIOrchestration] Step 3: Generating ranking narrative");
        var narrative = await rankingService.GenerateNarrativeAsync(rankingContext, ct);

        // ── Step 5: Generate interview kit (continuation job) ────────────────────
        logger.LogInformation("[AIOrchestration] Step 4: Generating interview kit");
        var kit = await kitService.GenerateAsync(rankingContext, ct);

        await hubContext.NotifyInterviewKitReadyAsync(application.JobId, applicationId);

        // ── Step 6: Enqueue webhook dispatch as Hangfire continuation ─────────────
        var webhookJobId = backgroundJobClient.Enqueue<IDispatchWebhookJob>(
            job => job.ExecuteAsync(applicationId, CancellationToken.None));

        logger.LogInformation(
            "[AIOrchestration] Pipeline complete for app {AppId}. " +
            "Score={Score}, Recommendation={Rec}, Questions={Qs}, WebhookJob={WebhookJobId}",
            applicationId, fitResult.FitScore, narrative.Recommendation,
            kit.Questions.Count, webhookJobId);
    }
}

/// <summary>Marker interface for webhook dispatch job (avoids circular dependency).</summary>
public interface IDispatchWebhookJob
{
    Task ExecuteAsync(Guid applicationId, CancellationToken ct);
}
