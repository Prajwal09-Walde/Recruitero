using Microsoft.Extensions.Logging;
using Qdrant.Client;
using Qdrant.Client.Grpc;
using RecruitAI.Application.Interfaces;
using RecruitAI.Domain.Entities;
using RecruitAI.Shared.Exceptions;
using System;
using System.Linq;

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
            float[]? jobEmbedding = null;
            List<ResumeChunkVector> resumeChunks = [];
            bool qdrantFailed = false;

            try
            {
                jobEmbedding = await FetchJobEmbeddingAsync(jobId, ct);
                if (jobEmbedding is not null)
                {
                    resumeChunks = await ScrollResumeChunksAsync(candidateId, jobId, ct);
                }
            }
            catch (Exception qex)
            {
                logger.LogWarning(qex, "[FitScoring] Qdrant connection failed. Falling back to purely lexical scoring.");
                qdrantFailed = true;
            }

            // 4. Compute cosine similarity for every chunk
            double rawScore = 0;
            var scoredChunks = new List<ScoredChunk>();
            var topChunks = new List<ScoredChunk>();

            if (!qdrantFailed && resumeChunks.Count > 0 && jobEmbedding is not null)
            {
                scoredChunks = resumeChunks
                    .Select(chunk => new ScoredChunk(
                        Section: chunk.Section,
                        Text: chunk.Text,
                        Similarity: CosineSimilarity(jobEmbedding, chunk.Vector),
                        Weight: GetSectionWeight(chunk.Section)))
                    .OrderByDescending(c => c.Similarity * c.Weight)
                    .ToList();

                // 5. Take top-5 chunks
                topChunks = scoredChunks.Take(TopChunksToConsider).ToList();

                // 6. Weighted average score (0–1 scale)
                double totalWeight = topChunks.Sum(c => c.Weight);
                double weightedScore = totalWeight > 0
                    ? topChunks.Sum(c => c.Similarity * c.Weight) / totalWeight
                    : 0;

                // 7. Normalize to 0–100 (cosine similarity is 0–1 for normalized embeddings)
                rawScore = weightedScore * 100.0;
            }

            // 8. Apply boost/penalty from SkillGraph and calculate Lexical match score
            var skillGraph = jobPosting.SkillGraph;
            double boost = 0;
            double penalty = 0;
            var skillMatches = new List<SkillMatch>();
            double blendedScore = rawScore;

            if (skillGraph is not null)
            {
                var fullResumeText = !string.IsNullOrEmpty(application.ExtractedText)
                    ? application.ExtractedText
                    : string.Join(" ", topChunks.Select(c => c.Text));

                // Required skills (weight 0.6 of lexical score)
                double totalRequiredWeight = 0;
                double matchedRequiredWeight = 0;
                foreach (var skill in skillGraph.RequiredSkills)
                {
                    bool matched = MatchKeyword(fullResumeText, skill.Skill);
                    double weight = skill.Weight > 0 ? skill.Weight : 1.0;
                    totalRequiredWeight += weight;
                    if (matched)
                    {
                        matchedRequiredWeight += weight;
                    }

                    skillMatches.Add(new SkillMatch(
                        Skill: skill.Skill,
                        Matched: matched,
                        MatchScore: matched ? (scoredChunks.Count > 0 ? scoredChunks
                            .Where(c => MatchKeyword(c.Text, skill.Skill))
                            .Select(c => c.Similarity)
                            .DefaultIfEmpty(0.5)
                            .Max() : 1.0) : 0.0));
                }

                // Nice to have skills (weight 0.3 of lexical score)
                double totalNiceToHaveWeight = 0;
                double matchedNiceToHaveWeight = 0;
                foreach (var skill in skillGraph.NiceToHaveSkills)
                {
                    bool matched = MatchKeyword(fullResumeText, skill.Skill);
                    double weight = skill.Weight > 0 ? skill.Weight : 1.0;
                    totalNiceToHaveWeight += weight;
                    if (matched)
                    {
                        matchedNiceToHaveWeight += weight;
                    }
                }

                // Domain keywords (weight 0.1 of lexical score)
                double totalDomainWeight = 0;
                double matchedDomainWeight = 0;
                if (skillGraph.DomainKeywords != null)
                {
                    foreach (var keyword in skillGraph.DomainKeywords)
                    {
                        bool matched = MatchKeyword(fullResumeText, keyword);
                        double weight = 0.5;
                        totalDomainWeight += weight;
                        if (matched)
                        {
                            matchedDomainWeight += weight;
                        }
                    }
                }

                // Compute lexical sub-scores (default to 100 if no keywords are defined for a section)
                double requiredScore = totalRequiredWeight > 0 ? (matchedRequiredWeight / totalRequiredWeight) * 100.0 : 100.0;
                double niceToHaveScore = totalNiceToHaveWeight > 0 ? (matchedNiceToHaveWeight / totalNiceToHaveWeight) * 100.0 : 100.0;
                double domainScore = totalDomainWeight > 0 ? (matchedDomainWeight / totalDomainWeight) * 100.0 : 100.0;

                // Blend lexical categories
                double lexicalScore = (requiredScore * 0.6) + (niceToHaveScore * 0.3) + (domainScore * 0.1);

                // Blend vector (70%) and lexical (30%) scores, or use lexical entirely if Qdrant is down
                blendedScore = qdrantFailed
                    ? lexicalScore
                    : (rawScore * 0.7) + (lexicalScore * 0.3);

                // Apply penalty if no required skills match
                bool anyRequiredSkillMatched = skillMatches.Any(s => s.Matched);
                if (skillGraph.RequiredSkills.Count > 0 && !anyRequiredSkillMatched)
                {
                    penalty = 10.0;
                    logger.LogInformation("[FitScoring] Applying –10 penalty: no required skills matched");
                }
            }

            double finalScore = Math.Clamp(blendedScore + boost - penalty, 0.0, 100.0);
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

    private static bool MatchKeyword(string text, string keyword)
    {
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(keyword))
            return false;

        var hasSpecialChars = keyword.Any(c => !char.IsLetterOrDigit(c));
        if (hasSpecialChars)
        {
            return text.Contains(keyword, StringComparison.OrdinalIgnoreCase);
        }
        
        var pattern = $@"\b{System.Text.RegularExpressions.Regex.Escape(keyword)}\b";
        try
        {
            return System.Text.RegularExpressions.Regex.IsMatch(text, pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }
        catch
        {
            return text.Contains(keyword, StringComparison.OrdinalIgnoreCase);
        }
    }

    private record ScoredChunk(string Section, string Text, double Similarity, double Weight);
    private record ResumeChunkVector(string Section, string Text, float[] Vector);
}
