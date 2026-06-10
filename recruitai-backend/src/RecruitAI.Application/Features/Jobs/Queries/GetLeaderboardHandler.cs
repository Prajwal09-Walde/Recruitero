using MediatR;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using RecruitAI.Application.Interfaces;
using RecruitAI.Shared.Constants;
using RecruitAI.Shared.Exceptions;

namespace RecruitAI.Application.Features.Jobs.Queries;

/// <summary>
/// Returns paginated, ranked leaderboard for a job.
/// Results cached for 30 seconds using IMemoryCache.
/// Cache is keyed per jobId and is invalidated by the scoring job after each update.
/// </summary>
public sealed class GetLeaderboardHandler(
    IJobRepository jobRepository,
    IApplicationRepository applicationRepository,
    ICandidateRepository candidateRepository,
    IMemoryCache cache,
    ILogger<GetLeaderboardHandler> logger)
    : IRequestHandler<GetLeaderboardQuery, LeaderboardResult>
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(30);

    public async Task<LeaderboardResult> Handle(
        GetLeaderboardQuery request,
        CancellationToken cancellationToken)
    {
        var cacheKey = $"{CacheKeys.Leaderboard(request.JobId)}:{request.StatusFilter}:{request.Page}:{request.PageSize}:{request.UserRole}:{request.UserEmail}";

        if (cache.TryGetValue(cacheKey, out LeaderboardResult? cached) && cached is not null)
        {
            logger.LogDebug("Leaderboard cache HIT for job {JobId}", request.JobId);
            return cached;
        }

        var job = await jobRepository.GetByIdAsync(request.JobId, cancellationToken)
            ?? throw new NotFoundException(nameof(Domain.Entities.Job), request.JobId);

        var statusFilter = request.StatusFilter?.ToLowerInvariant() == "all" ? null : request.StatusFilter;

        LeaderboardResult result;

        if (request.UserRole == Roles.Viewer)
        {
            var candidate = await candidateRepository.GetByEmailAsync(request.UserEmail ?? "", cancellationToken);
            if (candidate is null)
            {
                result = new LeaderboardResult(request.JobId, job.Title, 0, 0, new());
            }
            else
            {
                var allApps = await applicationRepository.GetByJobIdAsync(request.JobId, null, 1, 1000, cancellationToken);
                var userApp = allApps.FirstOrDefault(a => a.CandidateId == candidate.Id);
                if (userApp is null)
                {
                    result = new LeaderboardResult(request.JobId, job.Title, 0, 0, new());
                }
                else
                {
                    var candidatesList = new List<LeaderboardCandidateDto>
                    {
                        new LeaderboardCandidateDto(
                            Rank: userApp.Rank ?? 1,
                            CandidateId: userApp.CandidateId,
                            Name: candidate.FullName,
                            FitScore: userApp.FitScore ?? 0m,
                            TopSkillMatches: [],
                            Status: userApp.Status,
                            ApplicationId: userApp.Id
                        )
                    };
                    result = new LeaderboardResult(request.JobId, job.Title, 1, userApp.Status == "Scored" ? 1 : 0, candidatesList);
                }
            }
        }
        else if (request.UserRole == Roles.Recruiter)
        {
            if (!string.IsNullOrWhiteSpace(statusFilter) &&
                statusFilter != ApplicationStatus.SentToRecruiter &&
                statusFilter != ApplicationStatus.Shortlisted &&
                statusFilter != ApplicationStatus.Rejected)
            {
                result = new LeaderboardResult(request.JobId, job.Title, 0, 0, new());
            }
            else
            {
                var allApps = await applicationRepository.GetByJobIdAsync(request.JobId, null, 1, 1000, cancellationToken);
                var recruiterApps = allApps.Where(a =>
                    a.Status == ApplicationStatus.SentToRecruiter ||
                    a.Status == ApplicationStatus.Shortlisted ||
                    a.Status == ApplicationStatus.Rejected).ToList();

                if (!string.IsNullOrWhiteSpace(statusFilter))
                {
                    recruiterApps = recruiterApps.Where(a => a.Status == statusFilter).ToList();
                }

                var totalRecruiterCount = recruiterApps.Count;
                var processedRecruiterCount = recruiterApps.Count(a => a.Status == ApplicationStatus.Shortlisted || a.Status == ApplicationStatus.Rejected);

                var pagedRecruiterApps = recruiterApps
                    .Skip((request.Page - 1) * request.PageSize)
                    .Take(request.PageSize)
                    .ToList();

                var candidates = pagedRecruiterApps.Select((app, idx) => new LeaderboardCandidateDto(
                    Rank: app.Rank ?? (idx + 1),
                    CandidateId: app.CandidateId,
                    Name: app.Candidate?.FullName ?? "Unknown",
                    FitScore: app.FitScore ?? 0m,
                    TopSkillMatches: [],
                    Status: app.Status,
                    ApplicationId: app.Id
                )).ToList();

                result = new LeaderboardResult(
                    JobId: request.JobId,
                    JobTitle: job.Title,
                    TotalApplicants: totalRecruiterCount,
                    ProcessedCount: processedRecruiterCount,
                    Candidates: candidates);
            }
        }
        else
        {
            var applications = await applicationRepository.GetByJobIdAsync(
                request.JobId, statusFilter, request.Page, request.PageSize, cancellationToken);

            var totalApplicants = await applicationRepository.CountByJobIdAsync(request.JobId, null, cancellationToken);
            var processedCount = await applicationRepository.CountScoredByJobIdAsync(request.JobId, cancellationToken);

            var candidates = applications.Select((app, idx) => new LeaderboardCandidateDto(
                Rank: app.Rank ?? (idx + 1),
                CandidateId: app.CandidateId,
                Name: app.Candidate?.FullName ?? "Unknown",
                FitScore: app.FitScore ?? 0m,
                TopSkillMatches: [],
                Status: app.Status,
                ApplicationId: app.Id
            )).ToList();

            result = new LeaderboardResult(
                JobId: request.JobId,
                JobTitle: job.Title,
                TotalApplicants: totalApplicants,
                ProcessedCount: processedCount,
                Candidates: candidates);
        }

        cache.Set(cacheKey, result, CacheDuration);
        return result;
    }
}
