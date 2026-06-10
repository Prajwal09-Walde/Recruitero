using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using RecruitAI.Application.Interfaces;
using RecruitAI.Shared.Constants;

namespace RecruitAI.Infrastructure.Hubs;

/// <summary>
/// Strongly-typed client contract for RecruitmentHub.
/// All methods here are callable from server → connected client.
/// </summary>
public interface IRecruitmentHubClient
{
    Task ResumeUploaded(Guid applicationId, string candidateName, DateTime timestamp);
    Task ProcessingStarted(Guid applicationId, string candidateName);
    Task FitScoreReady(Guid applicationId, string candidateName, decimal fitScore, int rankPosition);
    Task InterviewKitReady(Guid applicationId);
    Task ProcessingFailed(Guid applicationId, string candidateName, string errorMessage);
    Task LeaderboardUpdated(Guid jobId, IReadOnlyList<RankedCandidateDto> candidates);
}

/// <summary>
/// Real-time SignalR hub for recruitment processing updates.
/// - JWT bearer auth enforced
/// - Redis backplane for horizontal scaling
/// - Clients join named groups per jobId
/// - Rate limited: max 100 concurrent connections per jobId group
/// </summary>
[Authorize]
public sealed class RecruitmentHub(ILogger<RecruitmentHub> logger)
    : Hub<IRecruitmentHubClient>
{
    private static readonly Dictionary<string, int> GroupConnectionCounts = new();
    private const int MaxConnectionsPerGroup = 100;

    public async Task JoinJobRoom(string jobId)
    {
        lock (GroupConnectionCounts)
        {
            GroupConnectionCounts.TryGetValue(jobId, out var count);
            if (count >= MaxConnectionsPerGroup)
            {
                logger.LogWarning(
                    "Connection {ConnId} rejected: job room {JobId} at capacity ({Max})",
                    Context.ConnectionId, jobId, MaxConnectionsPerGroup);
                throw new HubException($"Job room {jobId} is at maximum capacity ({MaxConnectionsPerGroup}).");
            }
            GroupConnectionCounts[jobId] = count + 1;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, $"job:{jobId}");
        logger.LogInformation("Connection {ConnId} joined job room {JobId}", Context.ConnectionId, jobId);
    }

    public async Task LeaveJobRoom(string jobId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"job:{jobId}");

        lock (GroupConnectionCounts)
        {
            if (GroupConnectionCounts.TryGetValue(jobId, out var count))
                GroupConnectionCounts[jobId] = Math.Max(0, count - 1);
        }

        logger.LogInformation("Connection {ConnId} left job room {JobId}", Context.ConnectionId, jobId);
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        // Cleanup is handled by the client explicitly calling LeaveJobRoom
        // For robustness, implement group tracking with connection IDs here
        await base.OnDisconnectedAsync(exception);
    }
}
