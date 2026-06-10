using Azure.AI.OpenAI;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;
using RecruitAI.Application.Interfaces;
using System.Text;
using System.Text.Json;

namespace RecruitAI.Infrastructure.AI;

/// <summary>
/// Generates 8 targeted interview questions via GPT-4o.
/// Uses response_format: json_object + JSON schema validation.
/// </summary>
public sealed class InterviewKitGenerationService(
    OpenAIClient openAiClient,
    IInterviewKitRepository kitRepository,
    IApplicationRepository applicationRepository,
    ILogger<InterviewKitGenerationService> logger)
    : IInterviewKitGenerationService
{
    private const string ChatModel = "gpt-4o";

    private static readonly AsyncRetryPolicy RetryPolicy = Policy
        .Handle<Exception>(ex => ex is not OperationCanceledException)
        .WaitAndRetryAsync(3, attempt => TimeSpan.FromSeconds(Math.Pow(4, attempt - 1)));

    public async Task<GeneratedInterviewKit> GenerateAsync(
        RankingContext context,
        CancellationToken ct = default)
    {
        var userMessage = BuildInterviewKitUserMessage(context);

        try
        {
            var rawJson = await RetryPolicy.ExecuteAsync(
                async () => await CallGptAsync(userMessage, 2000, ct));

            var kit = TryParseInterviewKit(rawJson);
            if (kit is not null)
            {
                await PersistInterviewKitAsync(context.ApplicationId, kit, ct);
                return kit;
            }

            // ── Corrective retry ──────────────────────────────────────────────────
            logger.LogWarning("[InterviewKit] Parse failed, attempting corrective prompt");
            var corrected = await CallGptAsync(
                $"Your previous JSON was invalid. Here it was:\n{rawJson}\n\n" +
                $"Re-generate EXACTLY following this schema with 8 questions:\n" +
                $"{AiPrompts.InterviewKitResponseSchema}",
                2000, ct);

            var correctedKit = TryParseInterviewKit(corrected);
            if (correctedKit is not null)
            {
                await PersistInterviewKitAsync(context.ApplicationId, correctedKit, ct);
                return correctedKit;
            }

            logger.LogError("[InterviewKit] Both parse attempts failed for app {Id}", context.ApplicationId);
            return new GeneratedInterviewKit([], false, rawJson);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[InterviewKit] Failed for application {Id}", context.ApplicationId);
            return new GeneratedInterviewKit([], false, ex.Message);
        }
    }

    private static string BuildInterviewKitUserMessage(RankingContext ctx)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Job Title: {ctx.JobTitle} ({ctx.Seniority} level)");
        sb.AppendLine($"Candidate: {ctx.CandidateName}, {ctx.ExperienceYears:F1} years exp");
        sb.AppendLine($"Fit Score: {ctx.FitScore}/100");
        sb.AppendLine("\nRequired skills (probe these):");
        foreach (var s in ctx.RequiredSkills.Take(8))
            sb.AppendLine($"  • {s}");

        sb.AppendLine("\nIdentified skill gaps:");
        foreach (var m in ctx.SkillMatches.Where(m => !m.Matched).Take(3))
            sb.AppendLine($"  • {m.Skill} (not found in resume)");

        sb.AppendLine("\nTop resume excerpts:");
        foreach (var chunk in ctx.TopChunks.Take(2))
            sb.AppendLine($"  [{chunk.Section}]: {chunk.TextPreview}");

        sb.AppendLine("\nGenerate exactly 8 interview questions as JSON.");
        sb.AppendLine("Mix: 3 Technical, 2 Behavioral, 2 System Design, 1 Domain.");
        sb.AppendLine($"Match difficulty to '{ctx.Seniority}' level.");
        sb.AppendLine($"Schema:\n{AiPrompts.InterviewKitResponseSchema}");

        return sb.ToString();
    }

    private static GeneratedInterviewKit? TryParseInterviewKit(string json)
    {
        try
        {
            // ── JSON Schema Validation ────────────────────────────────────────────
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("questions", out var questionsEl))
                return null;

            if (questionsEl.ValueKind != JsonValueKind.Array)
                return null;

            var questions = new List<GeneratedQuestion>();

            foreach (var q in questionsEl.EnumerateArray())
            {
                // Validate required fields
                if (!q.TryGetProperty("question", out var questionText) ||
                    !q.TryGetProperty("category", out _) ||
                    !q.TryGetProperty("difficulty", out _))
                    continue;

                questions.Add(new GeneratedQuestion(
                    Category: SafeString(q, "category", "Technical"),
                    Question: questionText.GetString() ?? "",
                    Difficulty: SafeString(q, "difficulty", "Medium"),
                    WhatToListenFor: SafeString(q, "what_to_listen_for", ""),
                    TargetedGap: q.TryGetProperty("targeted_gap", out var gap) &&
                                 gap.ValueKind == JsonValueKind.String
                        ? gap.GetString()
                        : null));
            }

            if (questions.Count == 0) return null;

            return new GeneratedInterviewKit(questions, true);
        }
        catch
        {
            return null;
        }
    }

    private async Task PersistInterviewKitAsync(
        Guid applicationId,
        GeneratedInterviewKit generated,
        CancellationToken ct)
    {
        var existing = await kitRepository.GetByApplicationIdAsync(applicationId, ct);
        var domainQuestions = generated.Questions.Select(q =>
            new Domain.Entities.InterviewQuestion(q.Category, q.Question, q.Difficulty, q.WhatToListenFor))
            .ToList();

        if (existing is not null)
        {
            existing.Regenerate(domainQuestions);
        }
        else
        {
            var newKit = new Domain.Entities.InterviewKit(applicationId, domainQuestions);
            await kitRepository.AddAsync(newKit, ct);
        }

        await kitRepository.SaveChangesAsync(ct);
    }

    private async Task<string> CallGptAsync(string userMessage, int maxTokens, CancellationToken ct)
    {
        var options = new ChatCompletionsOptions
        {
            DeploymentName = ChatModel,
            Temperature = 0.7f,
            MaxTokens = maxTokens,
            ResponseFormat = ChatCompletionsResponseFormat.JsonObject,
            Messages =
            {
                new ChatRequestSystemMessage(AiPrompts.InterviewKitSystem),
                new ChatRequestUserMessage(userMessage)
            }
        };

        var response = await openAiClient.GetChatCompletionsAsync(options, ct);
        return response.Value.Choices[0].Message.Content ?? "{}";
    }

    private static string SafeString(JsonElement el, string prop, string fallback = "") =>
        el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() ?? fallback : fallback;
}
