using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using RecruitAI.Application.Interfaces;
using RecruitAI.Domain.Entities;
using RecruitAI.Shared.Exceptions;

namespace RecruitAI.Infrastructure.AI;

public sealed class ResumeProcessingService(
    IResumeEmbeddingService embeddingService,
    IFitScoringService fitScoringService,
    ICandidateRepository candidateRepository,
    IApplicationRepository applicationRepository,
    ILogger<ResumeProcessingService> logger)
    : IResumeProcessingService
{
    public async Task<ProcessingResult> ProcessAsync(
        Guid applicationId,
        string extractedText,
        CancellationToken ct = default)
    {
        logger.LogInformation("[ResumeProcessingService] Starting AI pipeline for application {AppId}", applicationId);

        try
        {
            var application = await applicationRepository.GetByIdAsync(applicationId, ct)
                ?? throw new NotFoundException(nameof(Application), applicationId);

            // 1. Embed chunks and extract metadata using OpenAI (GPT-4o)
            logger.LogInformation("[ResumeProcessingService] Step 1: Chunking, embedding, and extracting metadata");
            var embeddingResult = await embeddingService.ProcessResumeAsync(
                application.CandidateId,
                applicationId,
                application.JobId,
                extractedText,
                ct);

            // 2. Update Candidate details with extracted metadata
            logger.LogInformation("[ResumeProcessingService] Step 2: Updating candidate details");
            var candidate = application.Candidate;
            if (candidate is null)
            {
                candidate = await candidateRepository.GetByEmailAsync(
                    embeddingResult.Metadata.Email ?? $"{Guid.NewGuid()}@unknown.recruitai.io", ct)
                    ?? new Candidate(embeddingResult.Metadata.Name, embeddingResult.Metadata.Email ?? $"{Guid.NewGuid()}@unknown.recruitai.io");
            }

            candidate.UpdateMetadata(
                embeddingResult.Metadata.Name ?? candidate.FullName,
                embeddingResult.Metadata.Email ?? candidate.Email
            );
            await candidateRepository.AddAsync(candidate, ct);

            // 3. Compute fit score via Qdrant
            logger.LogInformation("[ResumeProcessingService] Step 3: Computing fit score");
            var fitResult = await fitScoringService.ScoreAsync(
                application.JobId,
                application.CandidateId,
                applicationId,
                ct);

            if (!fitResult.Success)
            {
                throw new InvalidOperationException($"Scoring failed: {fitResult.Error}");
            }

            var topSkills = fitResult.Ranking.SkillMatches
                .Where(s => s.Matched)
                .Select(s => s.Skill)
                .ToList();

            return new ProcessingResult(
                FitScore: fitResult.FitScore,
                Rank: fitResult.Rank,
                TopSkillMatches: topSkills,
                Success: true
            );
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[ResumeProcessingService] AI processing pipeline failed for application {AppId}", applicationId);
            return new ProcessingResult(0m, 999, [], false, ex.Message);
        }
    }
}
