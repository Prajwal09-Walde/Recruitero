using MediatR;
using Microsoft.Extensions.Logging;
using RecruitAI.Application.Interfaces;
using RecruitAI.Shared.Exceptions;

namespace RecruitAI.Application.Features.InterviewKit.Queries;

public sealed class GetInterviewKitHandler(
    IApplicationRepository applicationRepository,
    IInterviewKitRepository kitRepository,
    IJobRepository jobRepository,
    ILogger<GetInterviewKitHandler> logger)
    : IRequestHandler<GetInterviewKitQuery, InterviewKitResult>
{
    public async Task<InterviewKitResult> Handle(
        GetInterviewKitQuery request,
        CancellationToken cancellationToken)
    {
        var application = await applicationRepository.GetByIdAsync(request.ApplicationId, cancellationToken)
            ?? throw new NotFoundException(nameof(Domain.Entities.Application), request.ApplicationId);

        var kit = await kitRepository.GetByApplicationIdAsync(request.ApplicationId, cancellationToken);
        if (kit is null || !kit.IsGenerated)
            throw new NotFoundException("InterviewKit", request.ApplicationId);

        var job = await jobRepository.GetByIdAsync(application.JobId, cancellationToken);

        return new InterviewKitResult(
            CandidateName: application.Candidate?.FullName ?? "Unknown",
            JobTitle: job?.Title ?? "Unknown",
            FitScore: application.FitScore ?? 0m,
            Questions: kit.Questions.Select(q => new InterviewQuestionDto(
                q.Category, q.Question, q.Difficulty, q.Rationale)).ToList());
    }
}
