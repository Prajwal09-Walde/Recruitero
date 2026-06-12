using Azure.AI.OpenAI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;
using Qdrant.Client;
using Qdrant.Client.Grpc;
using RecruitAI.Application.Interfaces;
using RecruitAI.Domain.Entities;
using RecruitAI.Shared.Exceptions;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace RecruitAI.Infrastructure.AI;

/// <summary>
/// Implements IJobSkillExtractor using:
/// - GPT-4o function calling for structured SkillGraph extraction
/// - text-embedding-ada-002 for semantic job embedding
/// - Qdrant for vector storage
/// - Polly for resilience (3 retries, exponential backoff)
/// </summary>
public sealed class OpenAIJobSkillExtractor(
    OpenAIClient openAiClient,
    QdrantClient qdrantClient,
    IConfiguration configuration,
    ILogger<OpenAIJobSkillExtractor> logger)
    : IJobSkillExtractor
{
    private const string QdrantCollection = "job_postings";
    private const int EmbeddingDimensions = 1536;
    private const string EmbeddingModel = "text-embedding-ada-002";
    private const string ChatModel = "gpt-4o";

    // ── Polly: 3 retries with exponential backoff (1s, 4s, 16s) ─────────────────
    private static readonly AsyncRetryPolicy RetryPolicy = Policy
        .Handle<Exception>(ex => ex is not OperationCanceledException)
        .WaitAndRetryAsync(
            retryCount: 3,
            sleepDurationProvider: attempt => TimeSpan.FromSeconds(Math.Pow(4, attempt - 1)),
            onRetry: (ex, delay, attempt, _) =>
            {
                // Logger captured via closure — done in calling method
            });

    public async Task<SkillExtractionResult> ExtractAndEmbedAsync(
        Guid jobPostingId,
        string title,
        string description,
        CancellationToken ct = default)
    {
        // Step 1: Ensure Qdrant collection exists
        bool qdrantFailed = false;
        try
        {
            await EnsureCollectionExistsAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[SkillExtractor] Qdrant connection failed. Storing job vector skipped.");
            qdrantFailed = true;
        }

        // Step 2: Extract SkillGraph via GPT-4o function calling
        var skillGraph = await RetryPolicy.ExecuteAsync(
            async () => await ExtractSkillGraphAsync(title, description, ct));

        logger.LogInformation(
            "[SkillExtractor] Extracted graph: {Req} required, {Nice} nice-to-have, seniority={Seniority}",
            skillGraph.RequiredSkills.Count, skillGraph.NiceToHaveSkills.Count, skillGraph.Seniority);

        var embedding = Array.Empty<float>();
        var pointId = Guid.Empty;

        if (!qdrantFailed)
        {
            try
            {
                // Step 3: Generate embedding of job_embedding_text
                embedding = await RetryPolicy.ExecuteAsync(
                    async () => await GenerateEmbeddingAsync(skillGraph.JobEmbeddingText, ct));

                // Step 4: Upsert into Qdrant
                pointId = Guid.NewGuid();
                await UpsertToQdrantAsync(pointId, jobPostingId, embedding, ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "[SkillExtractor] Qdrant upsert failed. Continuing without vector.");
                qdrantFailed = true;
            }
        }

        return new SkillExtractionResult(skillGraph, pointId, embedding);
    }

    public async Task<SkillGraph> ExtractOnlyAsync(
        string title,
        string description,
        CancellationToken ct = default)
    {
        return await RetryPolicy.ExecuteAsync(
            async () => await ExtractSkillGraphAsync(title, description, ct));
    }

    // ── GPT-4o Function Calling ──────────────────────────────────────────────────

    private async Task<SkillGraph> ExtractSkillGraphAsync(
        string title, string description, CancellationToken ct)
    {
        var chatOptions = new ChatCompletionsOptions
        {
            DeploymentName = ChatModel,
            Temperature = 0f, // Deterministic for extraction
            MaxTokens = 2000,
            Messages =
            {
                new ChatRequestSystemMessage(AiPrompts.JobSkillExtractorSystem),
                new ChatRequestUserMessage(
                    $"Job Title: {title}\n\nJob Description:\n{description}")
            },
            Functions =
            {
                new FunctionDefinition
                {
                    Name = "extract_skill_graph",
                    Description = "Extract a structured skill graph from a job description",
                    Parameters = BinaryData.FromString(AiPrompts.SkillGraphFunctionSchema)
                }
            },
            FunctionCall = FunctionDefinition.Auto
        };

        var response = await openAiClient.GetChatCompletionsAsync(chatOptions, ct);
        var choice = response.Value.Choices[0];

        if (choice.Message.FunctionCall is null)
            throw new ExternalServiceException("OpenAI", "GPT-4o did not return a function call.");

        var json = choice.Message.FunctionCall.Arguments;
        return ParseSkillGraph(json);
    }

    private static SkillGraph ParseSkillGraph(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        return new SkillGraph
        {
            RequiredSkills = ParseSkillWeights(root, "required_skills"),
            NiceToHaveSkills = ParseSkillWeights(root, "nice_to_have_skills"),
            ExperienceYearsMin = root.GetProperty("experience_years_min").GetInt32(),
            Seniority = root.GetProperty("seniority").GetString() ?? "mid",
            DomainKeywords = root.GetProperty("domain_keywords")
                .EnumerateArray()
                .Select(e => e.GetString() ?? "")
                .Where(s => !string.IsNullOrEmpty(s))
                .ToList(),
            JobEmbeddingText = root.GetProperty("job_embedding_text").GetString() ?? ""
        };
    }

    private static List<SkillWeight> ParseSkillWeights(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var arr))
            return [];

        return arr.EnumerateArray()
            .Select(item => new SkillWeight(
                Skill: item.GetProperty("skill").GetString() ?? "",
                Weight: item.GetProperty("weight").GetDouble(),
                Category: item.GetProperty("category").GetString() ?? "other"))
            .ToList();
    }

    // ── Embedding Generation ─────────────────────────────────────────────────────

    private async Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken ct)
    {
        var options = new EmbeddingsOptions(EmbeddingModel, [text]);
        var response = await openAiClient.GetEmbeddingsAsync(options, ct);
        return response.Value.Data[0].Embedding.ToArray();
    }

    // ── Qdrant Operations ────────────────────────────────────────────────────────

    private async Task EnsureCollectionExistsAsync(CancellationToken ct)
    {
        var collections = await qdrantClient.ListCollectionsAsync(ct);
        if (collections.Any(c => c == QdrantCollection))
            return;

        logger.LogInformation("[Qdrant] Creating collection '{Collection}'", QdrantCollection);
        await qdrantClient.CreateCollectionAsync(
            QdrantCollection,
            new VectorParams
            {
                Size = EmbeddingDimensions,
                Distance = Distance.Cosine
            },
            cancellationToken: ct);
    }

    private async Task UpsertToQdrantAsync(
        Guid pointId,
        Guid jobPostingId,
        float[] embedding,
        CancellationToken ct)
    {
        var point = new PointStruct
        {
            Id = new PointId { Uuid = pointId.ToString() },
            Vectors = embedding,
            Payload =
            {
                ["jobId"] = jobPostingId.ToString(),
                ["type"] = "job_posting",
                ["indexedAt"] = DateTime.UtcNow.ToString("O")
            }
        };

        await qdrantClient.UpsertAsync(QdrantCollection, [point], cancellationToken: ct);

        logger.LogInformation(
            "[Qdrant] Upserted job embedding for JobPosting {Id} as point {PointId}",
            jobPostingId, pointId);
    }
}
