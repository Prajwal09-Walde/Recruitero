using MediatR;
using Microsoft.AspNetCore.Http;

namespace RecruitAI.Application.Features.Resumes.Commands;

/// <summary>
/// Command to bulk-upload PDF resumes for a job opening.
/// Triggers S3 upload, candidate/application record creation, and Hangfire job enqueue.
/// </summary>
public record BulkUploadResumesCommand(
    Guid JobId,
    IReadOnlyList<IFormFile> Files,
    string? CandidateEmail = null
) : IRequest<BulkUploadResumesResult>;

public record BulkUploadResumesResult(
    Guid JobId,
    List<Guid> ApplicationIds,
    string EstimatedProcessingTime
);
