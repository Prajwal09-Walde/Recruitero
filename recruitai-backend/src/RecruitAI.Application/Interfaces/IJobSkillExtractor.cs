using RecruitAI.Domain.Entities;

namespace RecruitAI.Application.Interfaces;

/// <summary>
/// Extracts a structured SkillGraph from a job description using GPT-4o function calling,
/// then generates a job embedding and upserts it into Qdrant.
/// </summary>
public interface IJobSkillExtractor
{
    /// <summary>
    /// Full pipeline: GPT-4o extraction → embedding → Qdrant upsert.
    /// Returns the extracted SkillGraph and the Qdrant point ID.
    /// </summary>
    Task<SkillExtractionResult> ExtractAndEmbedAsync(
        Guid jobPostingId,
        string title,
        string description,
        CancellationToken ct = default);

    /// <summary>
    /// Lightweight extraction only, without saving to Qdrant or DB.
    /// </summary>
    Task<SkillGraph> ExtractOnlyAsync(
        string title,
        string description,
        CancellationToken ct = default);
}

public record SkillExtractionResult(
    SkillGraph SkillGraph,
    Guid QdrantPointId,
    float[] Embedding
);
