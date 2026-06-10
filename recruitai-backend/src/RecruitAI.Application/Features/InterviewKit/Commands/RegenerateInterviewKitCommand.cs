using Hangfire;
using MediatR;
using Microsoft.Extensions.Logging;
using RecruitAI.Application.Features.Resumes.Commands;
using RecruitAI.Application.Interfaces;
using RecruitAI.Shared.Exceptions;

namespace RecruitAI.Application.Features.InterviewKit.Commands;

/// <summary>Re-triggers AI interview kit generation for an application.</summary>
public record RegenerateInterviewKitCommand(Guid ApplicationId) : IRequest;

public class RegenerateInterviewKitHandler(
    IApplicationRepository applicationRepository,
    IBackgroundJobClient backgroundJobClient,
    ILogger<RegenerateInterviewKitHandler> logger)
    : IRequestHandler<RegenerateInterviewKitCommand>
{
    public async Task Handle(RegenerateInterviewKitCommand request, CancellationToken cancellationToken)
    {
        var application = await applicationRepository.GetByIdAsync(request.ApplicationId, cancellationToken)
            ?? throw new NotFoundException(nameof(Domain.Entities.Application), request.ApplicationId);

        backgroundJobClient.Enqueue<IGenerateInterviewKitJob>(
            job => job.ExecuteAsync(request.ApplicationId, CancellationToken.None));

        logger.LogInformation("Interview kit regeneration enqueued for application {AppId}", request.ApplicationId);
    }
}

public interface IGenerateInterviewKitJob
{
    Task ExecuteAsync(Guid applicationId, CancellationToken ct);
}
