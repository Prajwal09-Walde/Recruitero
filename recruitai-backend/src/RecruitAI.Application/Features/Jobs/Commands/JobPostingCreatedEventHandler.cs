using MediatR;
using Microsoft.Extensions.Logging;
using RecruitAI.Application.Interfaces;
using RecruitAI.Domain.Events;

namespace RecruitAI.Application.Features.Jobs.Commands;

/// <summary>
/// MediatR notification handler triggered by JobPostingCreatedEvent.
/// Orchestrates the full AI skill extraction and embedding pipeline.
/// 
/// Flow:
///   JobPosting.Create() → publishes JobPostingCreatedEvent
///   → Dispatcher calls this handler
///   → IJobSkillExtractor.ExtractAndEmbedAsync()
///   → Updates JobPosting.SkillGraph + EmbeddingPointId
/// </summary>
public sealed class JobPostingCreatedEventHandler(
    IJobSkillExtractor skillExtractor,
    IJobPostingRepository jobPostingRepository,
    ILogger<JobPostingCreatedEventHandler> logger)
    : INotificationHandler<JobPostingCreatedEvent>
{
    public async Task Handle(JobPostingCreatedEvent notification, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "[SkillGraph] Starting extraction for JobPosting {Id}: '{Title}'",
            notification.JobPostingId, notification.Title);

        try
        {
            var result = await skillExtractor.ExtractAndEmbedAsync(
                notification.JobPostingId,
                notification.Title,
                notification.Description,
                cancellationToken);

            var jobPosting = await jobPostingRepository.GetByIdAsync(
                notification.JobPostingId, cancellationToken);

            if (jobPosting is null)
            {
                logger.LogWarning("[SkillGraph] JobPosting {Id} not found after event — skipping",
                    notification.JobPostingId);
                return;
            }

            jobPosting.ApplySkillGraph(result.SkillGraph, result.QdrantPointId);
            await jobPostingRepository.SaveChangesAsync(cancellationToken);

            logger.LogInformation(
                "[SkillGraph] Extracted {Required} required + {NiceToHave} optional skills. " +
                "Qdrant point: {PointId}",
                result.SkillGraph.RequiredSkills.Count,
                result.SkillGraph.NiceToHaveSkills.Count,
                result.QdrantPointId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "[SkillGraph] Failed to extract skills for JobPosting {Id}", notification.JobPostingId);
            // Don't re-throw — domain event handlers must not crash the originating transaction
        }
    }
}
