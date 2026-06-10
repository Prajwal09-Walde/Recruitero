namespace RecruitAI.Application.Interfaces;

/// <summary>
/// Computes a FitScore (0–100) for a candidate against a job using vector similarity.
/// </summary>
public interface IFitScoringService
{
    /// <summary>
    /// Full scoring pipeline:
    /// 1. Fetch job embedding from Qdrant
    /// 2. Fetch all candidate resume chunk embeddings
    /// 3. Compute cosine similarity (top-5 best-of-N strategy)
    /// 4. Apply section weights, boosts, and penalties
    /// 5. Persist result to Application.FitScore + Application.AIRanking
    /// </summary>
    Task<FitScoreResult> ScoreAsync(
        Guid jobId,
        Guid candidateId,
        Guid applicationId,
        CancellationToken ct = default);
}

// ── DTOs ─────────────────────────────────────────────────────────────────────

public record FitScoreResult(
    decimal FitScore,           // 0.0 – 100.0
    int Rank,
    AIRanking Ranking,
    bool Success,
    string? Error = null
);

public record AIRanking(
    decimal FitScore,
    List<ChunkMatch> TopChunks,
    List<SkillMatch> SkillMatches,
    DateTime ScoredAt
);

public record ChunkMatch(
    string Section,
    double Similarity,
    string TextPreview       // First 200 chars
);

public record SkillMatch(
    string Skill,
    bool Matched,
    double MatchScore        // Best cosine score for this skill's keyword
);
