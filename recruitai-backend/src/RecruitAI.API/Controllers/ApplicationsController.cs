using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RecruitAI.Application.Features.InterviewKit.Commands;
using RecruitAI.Application.Features.InterviewKit.Queries;
using RecruitAI.Application.Interfaces;
using RecruitAI.Infrastructure.Persistence;
using RecruitAI.Shared.Constants;

namespace RecruitAI.API.Controllers;

/// <summary>
/// Application-level operations: interview kit retrieval and regeneration, and status updates.
/// </summary>
[ApiController]
[Route("api/applications")]
[Authorize(Roles = $"{Roles.HrAdmin},{Roles.Recruiter}")]
[Produces("application/json")]
public sealed class ApplicationsController(
    IMediator mediator,
    IApplicationRepository applicationRepository) : ControllerBase
{
    /// <summary>
    /// Returns the AI-generated interview kit for a candidate application.
    /// Returns 404 with Retry-After header if kit has not been generated yet.
    /// </summary>
    [HttpGet("{applicationId:guid}/interview-kit")]
    [ProducesResponseType(typeof(InterviewKitResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetInterviewKit(
        [FromRoute] Guid applicationId,
        CancellationToken cancellationToken)
    {
        try
        {
            var query = new GetInterviewKitQuery(applicationId);
            var result = await mediator.Send(query, cancellationToken);
            return Ok(result);
        }
        catch (Shared.Exceptions.NotFoundException)
        {
            // Interview kit not yet generated — tell the client when to retry
            Response.Headers["Retry-After"] = "120"; // ~2 minutes for AI processing
            return NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Interview Kit Not Ready",
                Detail = $"The interview kit for application {applicationId} has not been generated yet. " +
                         "Please retry after the processing completes.",
                Instance = Request.Path
            });
        }
    }

    /// <summary>
    /// Re-triggers GPT-4o interview question generation for a candidate.
    /// Enqueues a Hangfire background job and returns 202 Accepted.
    /// </summary>
    [HttpPost("{applicationId:guid}/interview-kit/regenerate")]
    [Authorize(Roles = $"{Roles.HrAdmin},{Roles.Recruiter}")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RegenerateInterviewKit(
        [FromRoute] Guid applicationId,
        CancellationToken cancellationToken)
    {
        var command = new RegenerateInterviewKitCommand(applicationId);
        await mediator.Send(command, cancellationToken);
        return AcceptedAtAction(
            nameof(GetInterviewKit),
            new { applicationId },
            new { message = "Interview kit regeneration has been queued.", applicationId });
    }

    /// <summary>
    /// Updates the application status (e.g., Shortlisted, Rejected).
    /// </summary>
    [HttpPatch("{applicationId:guid}/status")]
    public async Task<IActionResult> UpdateApplicationStatus(
        [FromRoute] Guid applicationId,
        [FromBody] UpdateStatusRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Status))
            return BadRequest("Status is required.");

        var role = User.FindFirst(ClaimTypes.Role)?.Value;
        if (role == Roles.Viewer)
        {
            return Forbid();
        }

        var application = await applicationRepository.GetByIdAsync(applicationId, cancellationToken);
        if (application is null)
            return NotFound();

        if (role == Roles.Recruiter)
        {
            // Recruiter can only move to Shortlisted or Rejected, and current status must be SentToRecruiter, Shortlisted, or Rejected
            if (request.Status != ApplicationStatus.Shortlisted && request.Status != ApplicationStatus.Rejected)
            {
                return BadRequest("Recruiter can only shortlist or reject candidates.");
            }

            if (application.Status != ApplicationStatus.SentToRecruiter &&
                application.Status != ApplicationStatus.Shortlisted &&
                application.Status != ApplicationStatus.Rejected)
            {
                return BadRequest("Candidate has not been sent to the recruiter yet.");
            }
        }

        application.UpdateStatus(request.Status);
        await applicationRepository.SaveChangesAsync(cancellationToken);

        return Ok(new { id = application.Id, status = application.Status });
    }
}

public record UpdateStatusRequest(string Status);
