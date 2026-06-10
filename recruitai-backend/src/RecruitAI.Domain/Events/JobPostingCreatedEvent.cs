using RecruitAI.Domain.Common;

namespace RecruitAI.Domain.Events;

/// <summary>
/// Raised when a JobPosting is created or its description is updated.
/// Handled by the AI pipeline to extract the SkillGraph and generate the job embedding.
/// </summary>
public record JobPostingCreatedEvent(
    Guid JobPostingId,
    string Title,
    string Description
) : IDomainEvent
{
    public DateTime OccurredOn { get; } = DateTime.UtcNow;
}
