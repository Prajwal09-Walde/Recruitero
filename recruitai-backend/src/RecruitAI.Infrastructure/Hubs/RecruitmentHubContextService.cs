using Microsoft.AspNetCore.SignalR;
using RecruitAI.Application.Interfaces;

namespace RecruitAI.Infrastructure.Hubs;

/// <summary>
/// Concrete implementation of IRecruitmentHubContext.
/// Wraps IHubContext<RecruitmentHub, IRecruitmentHubClient> and routes messages to the correct job group.
/// Injected into Hangfire background jobs and MediatR handlers.
/// </summary>
public sealed class RecruitmentHubContextService(
    IHubContext<RecruitmentHub, IRecruitmentHubClient> hubContext)
    : IRecruitmentHubContext
{
    private static string GroupName(Guid jobId) => $"job:{jobId}";

    public Task NotifyResumeUploadedAsync(
        Guid jobId, Guid applicationId, string candidateName, DateTime timestamp) =>
        hubContext.Clients.Group(GroupName(jobId))
            .ResumeUploaded(applicationId, candidateName, timestamp);

    public Task NotifyProcessingStartedAsync(
        Guid jobId, Guid applicationId, string candidateName) =>
        hubContext.Clients.Group(GroupName(jobId))
            .ProcessingStarted(applicationId, candidateName);

    public Task NotifyFitScoreReadyAsync(
        Guid jobId, Guid applicationId, string candidateName, decimal fitScore, int rankPosition) =>
        hubContext.Clients.Group(GroupName(jobId))
            .FitScoreReady(applicationId, candidateName, fitScore, rankPosition);

    public Task NotifyInterviewKitReadyAsync(Guid jobId, Guid applicationId) =>
        hubContext.Clients.Group(GroupName(jobId))
            .InterviewKitReady(applicationId);

    public Task NotifyProcessingFailedAsync(
        Guid jobId, Guid applicationId, string candidateName, string errorMessage) =>
        hubContext.Clients.Group(GroupName(jobId))
            .ProcessingFailed(applicationId, candidateName, errorMessage);

    public Task NotifyLeaderboardUpdatedAsync(
        Guid jobId, IReadOnlyList<RankedCandidateDto> rankedCandidates) =>
        hubContext.Clients.Group(GroupName(jobId))
            .LeaderboardUpdated(jobId, rankedCandidates);
}
