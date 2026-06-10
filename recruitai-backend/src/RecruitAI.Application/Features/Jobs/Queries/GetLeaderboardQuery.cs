using MediatR;

namespace RecruitAI.Application.Features.Jobs.Queries;

/// <summary>Returns ranked candidates for a job, optionally filtered by status. Cached 30s.</summary>
public record GetLeaderboardQuery(
    Guid JobId,
    string? StatusFilter,  // "Scored" | "Failed" | "All" (default)
    int Page,
    int PageSize,
    string? UserRole = null,
    string? UserEmail = null
) : IRequest<LeaderboardResult>;

public record LeaderboardResult(
    Guid JobId,
    string JobTitle,
    int TotalApplicants,
    int ProcessedCount,
    List<LeaderboardCandidateDto> Candidates
);

public record LeaderboardCandidateDto(
    int Rank,
    Guid CandidateId,
    string Name,
    decimal FitScore,
    List<string> TopSkillMatches,
    string Status,
    Guid ApplicationId
);
