using Azure.AI.OpenAI;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;
using Qdrant.Client;
using Qdrant.Client.Grpc;
using RecruitAI.Application.Interfaces;
using System.Text.Json;

namespace RecruitAI.Infrastructure.AI;

/// <summary>
/// Full resume embedding pipeline implementation:
/// Chunking → Batch Embedding → Qdrant Upsert → GPT-4o Metadata Extraction
/// </summary>
public sealed class ResumeEmbeddingService(
    OpenAIClient openAiClient,
    QdrantClient qdrantClient,
    ResumeChunker chunker,
    ILogger<ResumeEmbeddingService> logger)
    : IResumeEmbeddingService
{
    private const string ResumesCollection = "resumes";
    private const string EmbeddingModel = "text-embedding-ada-002";
    private const string ChatModel = "gpt-4o";
    private const int EmbeddingDimensions = 1536;
    private const int QdrantBatchSize = 100;

    private static readonly AsyncRetryPolicy RetryPolicy = Policy
        .Handle<Exception>(ex => ex is not OperationCanceledException)
        .WaitAndRetryAsync(3, attempt => TimeSpan.FromSeconds(Math.Pow(4, attempt - 1)));

    public async Task<ResumeProcessingResult> ProcessResumeAsync(
        Guid candidateId,
        Guid applicationId,
        Guid jobId,
        string resumeText,
        CancellationToken ct = default)
    {
        // Step 1: Ensure collection exists
        await EnsureCollectionExistsAsync(ct);

        // Step 2: Chunk the resume
        var chunks = chunker.Chunk(resumeText);
        logger.LogInformation(
            "[ResumeEmbedding] {Count} chunks for candidate {CandidateId}",
            chunks.Count, candidateId);

        if (chunks.Count == 0)
        {
            logger.LogWarning("[ResumeEmbedding] No chunks produced for candidate {Id}", candidateId);
            return new ResumeProcessingResult(
                new ResumeMetadata("Unknown", null, null, 0, [], [], "Unknown", "Unknown"),
                0, []);
        }

        // Step 3: Batch embed all chunks
        var embeddings = await RetryPolicy.ExecuteAsync(
            async () => await BatchEmbedAsync(chunks.Select(c => c.Text).ToList(), ct));

        // Step 4: Upsert to Qdrant in batches of 100
        var pointIds = await UpsertChunksAsync(
            chunks, embeddings, candidateId, applicationId, jobId, ct);

        // Step 5: Extract metadata via GPT-4o
        var metadata = await RetryPolicy.ExecuteAsync(
            async () => await ExtractMetadataAsync(resumeText, ct));

        logger.LogInformation(
            "[ResumeEmbedding] Stored {Points} points in Qdrant for candidate {Id}",
            pointIds.Count, candidateId);

        return new ResumeProcessingResult(metadata, chunks.Count, pointIds);
    }

    // ── Batch Embedding ──────────────────────────────────────────────────────────

    private async Task<List<float[]>> BatchEmbedAsync(List<string> texts, CancellationToken ct)
    {
        var options = new EmbeddingsOptions(EmbeddingModel, texts);
        var response = await openAiClient.GetEmbeddingsAsync(options, ct);

        return response.Value.Data
            .OrderBy(d => d.Index)
            .Select(d => d.Embedding.ToArray())
            .ToList();
    }

    // ── Qdrant Upsert ────────────────────────────────────────────────────────────

    private async Task<List<Guid>> UpsertChunksAsync(
        List<ResumeChunk> chunks,
        List<float[]> embeddings,
        Guid candidateId,
        Guid applicationId,
        Guid jobId,
        CancellationToken ct)
    {
        var allPointIds = new List<Guid>();
        var allPoints = new List<PointStruct>();

        for (int i = 0; i < chunks.Count; i++)
        {
            var pointId = Guid.NewGuid();
            allPointIds.Add(pointId);

            allPoints.Add(new PointStruct
            {
                Id = new PointId { Uuid = pointId.ToString() },
                Vectors = embeddings[i],
                Payload =
                {
                    ["candidateId"]  = candidateId.ToString(),
                    ["applicationId"] = applicationId.ToString(),
                    ["jobId"]        = jobId.ToString(),
                    ["section"]      = chunks[i].Section,
                    ["chunkIndex"]   = chunks[i].ChunkIndex,
                    ["tokenCount"]   = chunks[i].TokenCount,
                    ["text"]         = chunks[i].Text[..Math.Min(500, chunks[i].Text.Length)] // preview
                }
            });
        }

        // Batch upsert in groups of QdrantBatchSize
        foreach (var batch in allPoints.Chunk(QdrantBatchSize))
        {
            await qdrantClient.UpsertAsync(ResumesCollection, [.. batch], cancellationToken: ct);
        }

        return allPointIds;
    }

    // ── Metadata Extraction via GPT-4o ───────────────────────────────────────────

    private async Task<ResumeMetadata> ExtractMetadataAsync(string resumeText, CancellationToken ct)
    {
        // Truncate to ~6000 chars to fit within context window economically
        var truncated = resumeText.Length > 6000 ? resumeText[..6000] : resumeText;

        var options = new ChatCompletionsOptions
        {
            DeploymentName = ChatModel,
            Temperature = 0f,
            MaxTokens = 1000,
            Messages =
            {
                new ChatRequestSystemMessage(AiPrompts.ResumeMetadataSystem),
                new ChatRequestUserMessage($"Resume:\n{truncated}")
            },
            Functions =
            {
                new FunctionDefinition
                {
                    Name = "extract_resume_metadata",
                    Description = "Extract structured metadata from a resume",
                    Parameters = BinaryData.FromString(AiPrompts.ResumeMetadataFunctionSchema)
                }
            },
            FunctionCall = FunctionDefinition.Auto
        };

        var response = await openAiClient.GetChatCompletionsAsync(options, ct);
        var funcArgs = response.Value.Choices[0].Message.FunctionCall?.Arguments
            ?? "{}";

        return ParseResumeMetadata(funcArgs);
    }

    private static ResumeMetadata ParseResumeMetadata(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var skills = root.TryGetProperty("skills", out var skillsEl)
            ? skillsEl.EnumerateArray().Select(s => s.GetString() ?? "").ToList()
            : [];

        var education = root.TryGetProperty("education", out var eduEl)
            ? eduEl.EnumerateArray().Select(e => new EducationEntry(
                Degree: SafeString(e, "degree"),
                Institution: SafeString(e, "institution"),
                Year: e.TryGetProperty("year", out var yr) ? yr.GetInt32() : null))
              .ToList()
            : [];

        return new ResumeMetadata(
            Name: SafeString(root, "name", "Unknown"),
            Email: SafeString(root, "email"),
            Phone: SafeString(root, "phone"),
            TotalExperienceYears: root.TryGetProperty("total_experience_years", out var exp)
                ? exp.GetDouble() : 0,
            Skills: skills,
            Education: education,
            LastRole: SafeString(root, "last_role", "Unknown"),
            LastCompany: SafeString(root, "last_company", "Unknown"));
    }

    private static string? SafeString(JsonElement el, string prop, string? defaultVal = null) =>
        el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : defaultVal;

    // ── Qdrant Collection Setup ──────────────────────────────────────────────────

    private async Task EnsureCollectionExistsAsync(CancellationToken ct)
    {
        var collections = await qdrantClient.ListCollectionsAsync(ct);
        if (collections.Any(c => c == ResumesCollection))
            return;

        logger.LogInformation("[Qdrant] Creating collection '{Collection}'", ResumesCollection);

        await qdrantClient.CreateCollectionAsync(
            ResumesCollection,
            new VectorParams
            {
                Size = EmbeddingDimensions,
                Distance = Distance.Cosine,
                OnDisk = true // Large collection — store on disk
            },
            cancellationToken: ct);

        // Create payload index for efficient filtering
        await qdrantClient.CreatePayloadIndexAsync(
            ResumesCollection, "candidateId",
            PayloadSchemaType.Keyword, cancellationToken: ct);

        await qdrantClient.CreatePayloadIndexAsync(
            ResumesCollection, "jobId",
            PayloadSchemaType.Keyword, cancellationToken: ct);

        await qdrantClient.CreatePayloadIndexAsync(
            ResumesCollection, "applicationId",
            PayloadSchemaType.Keyword, cancellationToken: ct);
    }
}
