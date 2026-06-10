namespace RecruitAI.Application.Interfaces;

/// <summary>
/// Abstraction over SignalR hub context for injecting into background jobs and handlers.
/// Allows pushing real-time notifications without taking a direct dependency on
/// Microsoft.AspNetCore.SignalR from application layer.
/// </summary>
public interface IRecruitmentHubContext
{
    Task NotifyResumeUploadedAsync(Guid jobId, Guid applicationId, string candidateName, DateTime timestamp);
    Task NotifyProcessingStartedAsync(Guid jobId, Guid applicationId, string candidateName);
    Task NotifyFitScoreReadyAsync(Guid jobId, Guid applicationId, string candidateName, decimal fitScore, int rankPosition);
    Task NotifyInterviewKitReadyAsync(Guid jobId, Guid applicationId);
    Task NotifyProcessingFailedAsync(Guid jobId, Guid applicationId, string candidateName, string errorMessage);
    Task NotifyLeaderboardUpdatedAsync(Guid jobId, IReadOnlyList<RankedCandidateDto> rankedCandidates);
}

public record RankedCandidateDto(
    int Rank,
    Guid CandidateId,
    string Name,
    decimal FitScore,
    List<string> TopSkillMatches,
    string Status,
    Guid ApplicationId
);
