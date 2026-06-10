using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Hangfire;
using Microsoft.Extensions.Logging;
using RecruitAI.Application.Features.InterviewKit.Commands;
using RecruitAI.Application.Interfaces;
using RecruitAI.Shared.Constants;

namespace RecruitAI.Infrastructure.Jobs;

[AutomaticRetry(Attempts = 3, DelaysInSeconds = [60, 180, 600])]
[Queue(HangfireQueues.Resumes)]
public sealed class GenerateInterviewKitJob(
    IApplicationRepository applicationRepository,
    IJobPostingRepository jobPostingRepository,
    IFitScoringService fitScoringService,
    IInterviewKitGenerationService kitService,
    IRecruitmentHubContext hubContext,
    ILogger<GenerateInterviewKitJob> logger) : IGenerateInterviewKitJob
{
    public async Task ExecuteAsync(Guid applicationId, CancellationToken ct)
    {
        logger.LogInformation("[GenerateInterviewKitJob] Starting kit regeneration for application {AppId}", applicationId);

        var application = await applicationRepository.GetByIdAsync(applicationId, ct)
            ?? throw new InvalidOperationException($"Application {applicationId} not found");

        var jobPosting = await jobPostingRepository.GetByIdAsync(application.JobId, ct)
            ?? throw new InvalidOperationException($"JobPosting {application.JobId} not found");

        // Re-run fit scoring to get the latest ranking, chunks, and skill matches
        var fitResult = await fitScoringService.ScoreAsync(
            application.JobId,
            application.CandidateId,
            applicationId,
            ct);

        if (!fitResult.Success)
            throw new InvalidOperationException($"Failed to compute fit score: {fitResult.Error}");

        var candidateName = application.Candidate?.FullName ?? "Unknown";

        // Reconstruct the RankingContext
        var rankingContext = new RankingContext(
            ApplicationId: applicationId,
            JobId: application.JobId,
            CandidateId: application.CandidateId,
            JobTitle: jobPosting.Title,
            CandidateName: candidateName,
            ExperienceYears: 5.0, // Default since experience years isn't persisted in MongoDB
            FitScore: fitResult.FitScore,
            RequiredSkills: jobPosting.SkillGraph?.RequiredSkills.Select(s => s.Skill).ToList() ?? [],
            TopChunks: fitResult.Ranking.TopChunks,
            SkillMatches: fitResult.Ranking.SkillMatches,
            Seniority: jobPosting.SkillGraph?.Seniority ?? "mid");

        logger.LogInformation("[GenerateInterviewKitJob] Generating interview kit");
        var kit = await kitService.GenerateAsync(rankingContext, ct);

        await hubContext.NotifyInterviewKitReadyAsync(application.JobId, applicationId);

        logger.LogInformation("[GenerateInterviewKitJob] Kit regeneration complete for application {AppId}. Questions generated: {Count}", 
            applicationId, kit.Questions.Count);
    }
}
