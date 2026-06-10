namespace RecruitAI.Application.Interfaces;

/// <summary>
/// Core AI processing pipeline for a resume.
/// Called by the Hangfire background job after text extraction.
/// </summary>
public interface IResumeProcessingService
{
    /// <summary>
    /// Runs the full AI pipeline: embedding → vector search → GPT-4o scoring.
    /// Updates the Application entity status and persists results.
    /// </summary>
    Task<ProcessingResult> ProcessAsync(
        Guid applicationId,
        string extractedText,
        CancellationToken ct = default);
}

public record ProcessingResult(
    decimal FitScore,
    int Rank,
    List<string> TopSkillMatches,
    bool Success,
    string? Error = null
);
