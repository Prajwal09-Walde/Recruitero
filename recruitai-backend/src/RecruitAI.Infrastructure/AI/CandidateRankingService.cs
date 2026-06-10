using Azure.AI.OpenAI;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;
using RecruitAI.Application.Interfaces;
using System.Text;
using System.Text.Json;

namespace RecruitAI.Infrastructure.AI;

/// <summary>
/// Generates a GPT-4o hiring manager narrative for a scored candidate.
/// Uses response_format: json_object for guaranteed JSON output.
/// On parse failure: retries once with corrective prompt, then stores raw + flags failure.
/// </summary>
public sealed class CandidateRankingService(
    OpenAIClient openAiClient,
    ILogger<CandidateRankingService> logger)
    : ICandidateRankingService
{
    private const string ChatModel = "gpt-4o";

    private static readonly AsyncRetryPolicy RetryPolicy = Policy
        .Handle<Exception>(ex => ex is not OperationCanceledException)
        .WaitAndRetryAsync(3, attempt => TimeSpan.FromSeconds(Math.Pow(4, attempt - 1)));

    public async Task<RankingNarrative> GenerateNarrativeAsync(
        RankingContext context,
        CancellationToken ct = default)
    {
        var userMessage = BuildNarrativeUserMessage(context);

        try
        {
            var rawJson = await RetryPolicy.ExecuteAsync(
                async () => await CallGptJsonAsync(AiPrompts.CandidateRankingSystem, userMessage, 800, ct));

            var narrative = TryParseNarrative(rawJson);
            if (narrative is not null)
                return narrative;

            // ── Corrective retry on parse failure ────────────────────────────────
            logger.LogWarning("[Ranking] Parse failed, attempting corrective prompt");
            var correctedJson = await CallGptJsonAsync(
                AiPrompts.CandidateRankingSystem,
                $"Your previous response was invalid JSON. Here was the response:\n{rawJson}\n\n" +
                $"Please re-generate following EXACTLY this schema:\n{AiPrompts.CandidateRankingResponseSchema}",
                800, ct);

            var corrected = TryParseNarrative(correctedJson);
            if (corrected is not null)
                return corrected;

            // Store raw + flag
            logger.LogError("[Ranking] Both parse attempts failed for application {Id}", context.ApplicationId);
            return new RankingNarrative("Parse failed", [], ["Parse failed"], "Maybe", 0.0,
                false, rawJson);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[Ranking] GPT call failed for application {Id}", context.ApplicationId);
            return new RankingNarrative(ex.Message, [], [], "Maybe", 0.0, false);
        }
    }

    private static string BuildNarrativeUserMessage(RankingContext ctx)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Job: {ctx.JobTitle}");
        sb.AppendLine($"Seniority level: {ctx.Seniority}");
        sb.AppendLine("Required skills:");
        foreach (var skill in ctx.RequiredSkills)
            sb.AppendLine($"  • {skill}");

        sb.AppendLine($"\nCandidate: {ctx.CandidateName}, {ctx.ExperienceYears:F1} years experience");
        sb.AppendLine($"Fit score: {ctx.FitScore}/100");

        sb.AppendLine("\nSkill match results:");
        foreach (var m in ctx.SkillMatches.Take(10))
            sb.AppendLine($"  • {m.Skill}: {(m.Matched ? "✓ Matched" : "✗ Not found")}");

        sb.AppendLine("\nTop matching resume sections:");
        foreach (var chunk in ctx.TopChunks.Take(3))
            sb.AppendLine($"  [{chunk.Section}] (similarity={chunk.Similarity:F3}): {chunk.TextPreview}...");

        sb.AppendLine($"\nGenerate a JSON object matching this schema:\n{AiPrompts.CandidateRankingResponseSchema}");
        sb.AppendLine("Generate: 1) 3-sentence summary, 2) Top 3 strengths, 3) Top 2 gaps, 4) Hire recommendation.");

        return sb.ToString();
    }

    private static RankingNarrative? TryParseNarrative(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var strengths = root.TryGetProperty("strengths", out var s)
                ? s.EnumerateArray().Select(x => x.GetString() ?? "").ToList()
                : [];

            var gaps = root.TryGetProperty("gaps", out var g)
                ? g.EnumerateArray().Select(x => x.GetString() ?? "").ToList()
                : [];

            return new RankingNarrative(
                Summary: root.TryGetProperty("summary", out var sum) ? sum.GetString() ?? "" : "",
                Strengths: strengths,
                Gaps: gaps,
                Recommendation: root.TryGetProperty("recommendation", out var rec)
                    ? rec.GetString() ?? "Maybe" : "Maybe",
                Confidence: root.TryGetProperty("confidence", out var conf)
                    ? conf.GetDouble() : 0.7,
                ParseSucceeded: true);
        }
        catch
        {
            return null;
        }
    }

    private async Task<string> CallGptJsonAsync(
        string system, string user, int maxTokens, CancellationToken ct)
    {
        var options = new ChatCompletionsOptions
        {
            DeploymentName = ChatModel,
            Temperature = 0.3f,
            MaxTokens = maxTokens,
            ResponseFormat = ChatCompletionsResponseFormat.JsonObject,
            Messages =
            {
                new ChatRequestSystemMessage(system),
                new ChatRequestUserMessage(user)
            }
        };

        var response = await openAiClient.GetChatCompletionsAsync(options, ct);
        return response.Value.Choices[0].Message.Content ?? "{}";
    }
}
