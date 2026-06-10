using MediatR;

namespace RecruitAI.Application.Features.InterviewKit.Queries;

/// <summary>Fetches the AI-generated interview kit for a specific application.</summary>
public record GetInterviewKitQuery(Guid ApplicationId) : IRequest<InterviewKitResult>;

public record InterviewKitResult(
    string CandidateName,
    string JobTitle,
    decimal FitScore,
    List<InterviewQuestionDto> Questions
);

public record InterviewQuestionDto(
    string Category,
    string Question,
    string Difficulty,
    string Rationale
);
