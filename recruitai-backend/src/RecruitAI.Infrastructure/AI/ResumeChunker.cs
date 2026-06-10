using System.Text.RegularExpressions;

namespace RecruitAI.Infrastructure.AI;

/// <summary>
/// Rule-based resume chunker that splits resume text into semantic sections.
/// 
/// Algorithm:
/// 1. Detect section headers via regex (Experience, Education, Skills, etc.)
/// 2. Split text at each header boundary
/// 3. Enforce max token count (≈512 tokens ≈ 2048 chars at ~4 chars/token)
/// 4. Apply 50-token overlap between adjacent chunks
/// </summary>
public sealed class ResumeChunker
{
    private const int MaxTokens = 512;
    private const int OverlapTokens = 50;
    private const int CharsPerToken = 4; // Approximate for English text

    private static readonly int MaxChars = MaxTokens * CharsPerToken;
    private static readonly int OverlapChars = OverlapTokens * CharsPerToken;

    // Matches common resume section headers (case-insensitive)
    private static readonly Regex SectionHeaderRegex = new(
        @"^(EXPERIENCE|WORK EXPERIENCE|PROFESSIONAL EXPERIENCE|" +
        @"EDUCATION|ACADEMIC BACKGROUND|" +
        @"SKILLS|TECHNICAL SKILLS|CORE COMPETENCIES|" +
        @"PROJECTS|PERSONAL PROJECTS|OPEN SOURCE|" +
        @"CERTIFICATIONS|CERTIFICATES|LICENSES|" +
        @"SUMMARY|PROFILE|OBJECTIVE|ABOUT|" +
        @"PUBLICATIONS|AWARDS|HONORS|" +
        @"VOLUNTEER|LEADERSHIP|ACTIVITIES)\s*:?$",
        RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public List<ResumeChunk> Chunk(string resumeText)
    {
        if (string.IsNullOrWhiteSpace(resumeText))
            return [];

        var lines = resumeText.Split('\n', StringSplitOptions.None);
        var sections = SplitIntoSections(lines);
        var chunks = new List<ResumeChunk>();
        int chunkIndex = 0;

        foreach (var (sectionName, sectionText) in sections)
        {
            // Split oversized sections into sub-chunks
            var subChunks = SplitWithOverlap(sectionText, MaxChars, OverlapChars);
            foreach (var text in subChunks)
            {
                var trimmed = text.Trim();
                if (trimmed.Length < 20) continue; // Skip near-empty chunks

                var tokenCount = EstimateTokenCount(trimmed);
                chunks.Add(new ResumeChunk(
                    Section: NormalizeSection(sectionName),
                    Text: trimmed,
                    TokenCount: tokenCount,
                    ChunkIndex: chunkIndex++));
            }
        }

        return chunks;
    }

    private static List<(string Section, string Text)> SplitIntoSections(string[] lines)
    {
        var sections = new List<(string, string)>();
        var currentSection = "Header";
        var currentLines = new List<string>();

        foreach (var line in lines)
        {
            if (SectionHeaderRegex.IsMatch(line.Trim()))
            {
                if (currentLines.Count > 0)
                    sections.Add((currentSection, string.Join('\n', currentLines)));

                currentSection = line.Trim().TrimEnd(':');
                currentLines = [];
            }
            else
            {
                currentLines.Add(line);
            }
        }

        if (currentLines.Count > 0)
            sections.Add((currentSection, string.Join('\n', currentLines)));

        return sections;
    }

    private static List<string> SplitWithOverlap(string text, int maxChars, int overlapChars)
    {
        if (text.Length <= maxChars)
            return [text];

        var chunks = new List<string>();
        int start = 0;

        while (start < text.Length)
        {
            int end = Math.Min(start + maxChars, text.Length);

            // Try to break at a sentence boundary
            if (end < text.Length)
            {
                int lastPeriod = text.LastIndexOf('.', end - 1, Math.Min(100, end - start));
                if (lastPeriod > start) end = lastPeriod + 1;
            }

            chunks.Add(text[start..end]);

            // Advance with overlap: next chunk starts `overlapChars` before end
            start = Math.Max(start + 1, end - overlapChars);
        }

        return chunks;
    }

    private static string NormalizeSection(string raw) =>
        raw.ToUpperInvariant() switch
        {
            var s when s.Contains("EXPERIENCE") => "Experience",
            var s when s.Contains("EDUCATION")  => "Education",
            var s when s.Contains("SKILL")       => "Skills",
            var s when s.Contains("PROJECT")     => "Projects",
            var s when s.Contains("CERT")        => "Certifications",
            var s when s.Contains("SUMMARY") || s.Contains("PROFILE") || s.Contains("ABOUT") => "Summary",
            _ => raw
        };

    private static int EstimateTokenCount(string text) =>
        (int)Math.Ceiling((double)text.Length / CharsPerToken);
}

public record ResumeChunk(
    string Section,
    string Text,
    int TokenCount,
    int ChunkIndex
);
