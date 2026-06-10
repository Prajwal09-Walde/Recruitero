using RecruitAI.Domain.Entities;

namespace RecruitAI.Application.Interfaces;

/// <summary>
/// Full resume embedding pipeline:
/// chunk → embed → store in Qdrant → extract metadata via GPT-4o
/// </summary>
public interface IResumeEmbeddingService
{
    /// <summary>
    /// Processes a resume for a given application:
    /// 1. Chunks the text into semantic sections
    /// 2. Generates embeddings for each chunk
    /// 3. Upserts into Qdrant 'resumes' collection
    /// 4. Extracts candidate metadata via GPT-4o
    /// 5. Returns metadata to update the Candidate entity
    /// </summary>
    Task<ResumeProcessingResult> ProcessResumeAsync(
        Guid candidateId,
        Guid applicationId,
        Guid jobId,
        string resumeText,
        CancellationToken ct = default);
}

public record ResumeProcessingResult(
    ResumeMetadata Metadata,
    int ChunksStored,
    List<Guid> QdrantPointIds
);

public record ResumeMetadata(
    string Name,
    string? Email,
    string? Phone,
    double TotalExperienceYears,
    List<string> Skills,
    List<EducationEntry> Education,
    string LastRole,
    string LastCompany
);

public record EducationEntry(
    string Degree,
    string Institution,
    int? Year
);
