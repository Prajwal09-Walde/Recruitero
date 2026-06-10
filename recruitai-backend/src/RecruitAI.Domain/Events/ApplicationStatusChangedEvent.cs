using RecruitAI.Domain.Common;

namespace RecruitAI.Domain.Events;

/// <summary>
/// Published whenever an Application transitions to a new status.
/// Consumed by SignalR notification handlers to push real-time updates.
/// </summary>
public record ApplicationStatusChangedEvent(
    Guid ApplicationId,
    string NewStatus,
    Guid CandidateId,
    Guid JobId
) : IDomainEvent
{
    public DateTime OccurredOn { get; } = DateTime.UtcNow;
}
