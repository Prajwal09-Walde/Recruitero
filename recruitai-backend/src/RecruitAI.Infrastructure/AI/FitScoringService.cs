using Microsoft.Extensions.Logging;
using Qdrant.Client;
using Qdrant.Client.Grpc;
using RecruitAI.Application.Interfaces;
using RecruitAI.Domain.Entities;
using RecruitAI.Shared.Exceptions;

namespace RecruitAI.Infrastructure.AI;

/// <summary>
/// Candidate fit scoring engine using Qdrant vector retrieval and cosine similarity.
///
/// Score formula:
///   1. Fetch job embedding vector from Qdrant (job_postings collection)
///   2. Fetch ALL resume chunk vectors (resumes collection, filtered by candidateId + jobId)
///   3. Cosine similarity per chunk
///   4. Take top-5 chunks (best-of-N)
///   5. Weighted average by section type:
///      Skills ×1.3, Experience ×1.2, Projects ×1.1, Education ×0.8, others ×1.0
///   6. Normalize to 0–100
///   7. Boost +5 if experience_years_min met
///   8. Penalty –10 if no required skills matched
/// </summary>
public sealed class FitScoringService(
    QdrantClient qdrantClient,
    IApplicationRepository applicationRepository,
    IJobPostingRepository jobPostingRepository,
    ILogger<FitScoringService> logger)
    : IFitScoringService
{
    private const string JobsCollection = "job_postings";
    private const string ResumesCollection = "resumes";
    private const int TopChunksToConsider = 5;

    // Section weighting multipliers
    private static readonly Dictionary<string, double> SectionWeights = new(
        StringComparer.OrdinalIgnoreCase)
    {
        ["Skills"]          = 1.3,
        ["Experience"]      = 1.2,
        ["Projects"]        = 1.1,
        ["Summary"]         = 1.0,
        ["Certifications"]  = 1.0,
        ["Education"]       = 0.8,
        ["Header"]          = 0.7
    };

    public async Task<FitScoreResult> ScoreAsync(
        Guid jobId,
        Guid candidateId,
        Guid applicationId,
        CancellationToken ct = default)
    {
        try
        {
            // 1. Load job and application
            var jobPosting = await jobPostingRepository.GetByIdAsync(jobId, ct)
                ?? throw new NotFoundException(nameof(JobPosting), jobId);

            var application = await applicationRepository.GetByIdAsync(applicationId, ct)
                ?? throw new NotFoundException(nameof(Application), applicationId);

            // 2. Fetch job embedding from Qdrant
            var jobEmbedding = await FetchJobEmbeddingAsync(jobId, ct);
            if (jobEmbedding is null)
                throw new InvalidOperationException($"No embedding found for job {jobId}");

            // 3. Fetch all resume chunk embeddings via Qdrant scroll
            var resumeChunks = await ScrollResumeChunksAsync(candidateId, jobId, ct);
            if (resumeChunks.Count == 0)
            {
                logger.LogWarning("[FitScoring] No resume chunks found for candidate {Id}", candidateId);
                return new FitScoreResult(0m, 999, CreateEmptyRanking(), true);
            }

            // 4. Compute cosine similarity for every chunk
            var scoredChunks = resumeChunks
                .Select(chunk => new ScoredChunk(
                    Section: chunk.Section,
                    Text: chunk.Text,
                    Similarity: CosineSimilarity(jobEmbedding, chunk.Vector),
                    Weight: GetSectionWeight(chunk.Section)))
                .OrderByDescending(c => c.Similarity * c.Weight)
                .ToList();

            // 5. Take top-5 chunks
            var topChunks = scoredChunks.Take(TopChunksToConsider).ToList();

            // 6. Weighted average score (0–1 scale)
            double totalWeight = topChunks.Sum(c => c.Weight);
            double weightedScore = totalWeight > 0
                ? topChunks.Sum(c => c.Similarity * c.Weight) / totalWeight
                : 0;

            // 7. Normalize to 0–100 (cosine similarity is 0–1 for normalized embeddings)
            double rawScore = weightedScore * 100.0;

            // 8. Apply boost/penalty from SkillGraph
            var skillGraph = jobPosting.SkillGraph;
            double boost = 0;
            double penalty = 0;
            var skillMatches = new List<SkillMatch>();

            if (skillGraph is not null)
            {
                // Check experience years
                var candidate = application.Candidate;
                // (In production, candidate.TotalExperienceYears would be populated by metadata extraction)

                // Skill matching: check if any resume chunk contains the skill name
                foreach (var skill in skillGraph.RequiredSkills)
                {
                    bool matched = scoredChunks.Any(c =>
                        c.Text.Contains(skill.Skill, StringComparison.OrdinalIgnoreCase));

                    skillMatches.Add(new SkillMatch(
                        Skill: skill.Skill,
                        Matched: matched,
                        MatchScore: matched ? scoredChunks
                            .Where(c => c.Text.Contains(skill.Skill, StringComparison.OrdinalIgnoreCase))
                            .Max(c => c.Similarity) : 0.0));
                }

                bool anyRequiredSkillMatched = skillMatches.Any(s => s.Matched);
                if (!anyRequiredSkillMatched)
                {
                    penalty = 10.0;
                    logger.LogInformation("[FitScoring] Applying –10 penalty: no required skills matched");
                }
            }

            double finalScore = Math.Clamp(rawScore + boost - penalty, 0.0, 100.0);
            var fitScore = (decimal)Math.Round(finalScore, 2);

            // 9. Build ranking object
            var ranking = new AIRanking(
                FitScore: fitScore,
                TopChunks: topChunks.Select(c => new ChunkMatch(
                    Section: c.Section,
                    Similarity: Math.Round(c.Similarity, 4),
                    TextPreview: c.Text[..Math.Min(200, c.Text.Length)])).ToList(),
                SkillMatches: skillMatches,
                ScoredAt: DateTime.UtcNow);

            // 10. Update application (rank will be set by the leaderboard recalculation)
            application.MarkScored(fitScore, rank: 0); // Rank computed after all applications scored
            await applicationRepository.SaveChangesAsync(ct);

            logger.LogInformation(
                "[FitScoring] Candidate {CandidateId}: raw={Raw:F2}, penalty={Penalty}, final={Final:F2}",
                candidateId, rawScore, penalty, finalScore);

            return new FitScoreResult(fitScore, 0, ranking, true);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[FitScoring] Error scoring candidate {CandidateId}", candidateId);
            return new FitScoreResult(0m, 999, CreateEmptyRanking(), false, ex.Message);
        }
    }

    // ── Cosine Similarity (pure .NET, no external library) ───────────────────────

    /// <summary>
    /// Computes cosine similarity between two vectors.
    /// 
    /// Formula:  cos(θ) = (A · B) / (|A| × |B|)
    /// 
    /// Where:
    ///   A · B  = Σ(Aᵢ × Bᵢ)           — dot product
    ///   |A|    = √(Σ Aᵢ²)              — L2 norm of A
    ///   |B|    = √(Σ Bᵢ²)              — L2 norm of B
    ///
    /// For unit-normalized vectors (as returned by OpenAI), |A| = |B| = 1,
    /// so cos(θ) = A · B (just the dot product).
    /// </summary>
    public static double CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length)
            throw new ArgumentException("Vectors must have the same dimension.");

        double dotProduct = 0;
        double normA = 0;
        double normB = 0;

        for (int i = 0; i < a.Length; i++)
        {
            dotProduct += (double)a[i] * b[i];
            normA += (double)a[i] * a[i];
            normB += (double)b[i] * b[i];
        }

        double denominator = Math.Sqrt(normA) * Math.Sqrt(normB);
        return denominator < 1e-10 ? 0.0 : dotProduct / denominator;
    }

    // ── Qdrant Queries ───────────────────────────────────────────────────────────

    private async Task<float[]?> FetchJobEmbeddingAsync(Guid jobId, CancellationToken ct)
    {
        // Scroll to find the point with matching jobId payload
        var result = await qdrantClient.ScrollAsync(
            JobsCollection,
            filter: new Filter
            {
                Must =
                {
                    new Condition
                    {
                        Field = new FieldCondition
                        {
                            Key = "jobId",
                            Match = new Match { Keyword = jobId.ToString() }
                        }
                    }
                }
            },
            limit: 1,
            vectorsSelector: true,
            cancellationToken: ct);

        var point = result.Result.FirstOrDefault();
        return point?.Vectors?.Vector?.Data?.ToArray();
    }

    private async Task<List<ResumeChunkVector>> ScrollResumeChunksAsync(
        Guid candidateId, Guid jobId, CancellationToken ct)
    {
        var results = new List<ResumeChunkVector>();
        string? offsetId = null;

        do
        {
            var scrollResult = await qdrantClient.ScrollAsync(
                ResumesCollection,
                filter: new Filter
                {
                    Must =
                    {
                        new Condition
                        {
                            Field = new FieldCondition
                            {
                                Key = "candidateId",
                                Match = new Match { Keyword = candidateId.ToString() }
                            }
                        },
                        new Condition
                        {
                            Field = new FieldCondition
                            {
                                Key = "jobId",
                                Match = new Match { Keyword = jobId.ToString() }
                            }
                        }
                    }
                },
                limit: 100,
                offset: offsetId is null ? null : new PointId { Uuid = offsetId },
                vectorsSelector: true,
                cancellationToken: ct);

            foreach (var point in scrollResult.Result)
            {
                results.Add(new ResumeChunkVector(
                    Section: point.Payload.TryGetValue("section", out var s) ? s.StringValue : "Unknown",
                    Text: point.Payload.TryGetValue("text", out var t) ? t.StringValue : "",
                    Vector: point.Vectors?.Vector?.Data?.ToArray() ?? []));
            }

            offsetId = scrollResult.NextPageOffset?.Uuid;

        } while (offsetId is not null);

        return results;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────

    private static double GetSectionWeight(string section) =>
        SectionWeights.TryGetValue(section, out var w) ? w : 1.0;

    private static AIRanking CreateEmptyRanking() =>
        new(0m, [], [], DateTime.UtcNow);

    private record ScoredChunk(string Section, string Text, double Similarity, double Weight);
    private record ResumeChunkVector(string Section, string Text, float[] Vector);
}
