using RecruitAI.Application.Interfaces;

namespace RecruitAI.Application.Interfaces;

/// <summary>Generates a GPT-4o hiring narrative for a scored candidate.</summary>
public interface ICandidateRankingService
{
    Task<RankingNarrative> GenerateNarrativeAsync(
        RankingContext context,
        CancellationToken ct = default);
}

/// <summary>Generates 8 targeted interview questions using GPT-4o.</summary>
public interface IInterviewKitGenerationService
{
    Task<GeneratedInterviewKit> GenerateAsync(
        RankingContext context,
        CancellationToken ct = default);
}

// ── Input/Output DTOs ────────────────────────────────────────────────────────

public record RankingContext(
    Guid ApplicationId,
    Guid JobId,
    Guid CandidateId,
    string JobTitle,
    string CandidateName,
    double ExperienceYears,
    decimal FitScore,
    List<string> RequiredSkills,
    List<ChunkMatch> TopChunks,
    List<SkillMatch> SkillMatches,
    string Seniority
);

public record RankingNarrative(
    string Summary,
    List<string> Strengths,
    List<string> Gaps,
    string Recommendation,     // "Strong Yes" | "Yes" | "Maybe" | "No"
    double Confidence,
    bool ParseSucceeded,
    string? RawJson = null
);

public record GeneratedInterviewKit(
    List<GeneratedQuestion> Questions,
    bool ParseSucceeded,
    string? RawJson = null
);

public record GeneratedQuestion(
    string Category,
    string Question,
    string Difficulty,
    string WhatToListenFor,
    string? TargetedGap
);
