using FluentValidation;
using Microsoft.AspNetCore.Http;

namespace RecruitAI.Application.Features.Resumes.Commands;

/// <summary>
/// Validates the bulk upload command:
/// - 1–20 files
/// - Each file ≤ 5MB
/// - PDF content type only
/// </summary>
public class BulkUploadResumesValidator : AbstractValidator<BulkUploadResumesCommand>
{
    private const int MaxFiles = 20;
    private const long MaxFileSizeBytes = 5 * 1024 * 1024; // 5 MB

    public BulkUploadResumesValidator()
    {
        RuleFor(x => x.JobId)
            .NotEmpty()
            .WithMessage("JobId is required.");

        RuleFor(x => x.Files)
            .NotEmpty()
            .WithMessage("At least one file must be provided.")
            .Must(f => f.Count <= MaxFiles)
            .WithMessage($"Maximum {MaxFiles} files allowed per request.");

        RuleForEach(x => x.Files)
            .ChildRules(file =>
            {
                file.RuleFor(f => f.Length)
                    .LessThanOrEqualTo(MaxFileSizeBytes)
                    .WithMessage(f => $"File '{f.FileName}' exceeds the 5MB size limit.");

                file.RuleFor(f => f.ContentType)
                    .Must(ct => ct == "application/pdf")
                    .WithMessage(f => $"File '{f.FileName}' must be a PDF (got: {f.ContentType}).");
            });
    }
}
