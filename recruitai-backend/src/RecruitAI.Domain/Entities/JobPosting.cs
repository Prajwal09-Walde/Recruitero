using RecruitAI.Domain.Common;
using RecruitAI.Domain.Events;

namespace RecruitAI.Domain.Entities;

/// <summary>
/// A job posting with AI-extracted skill graph and Qdrant embedding reference.
/// Extends Job entity with AI intelligence fields.
/// </summary>
public class JobPosting : BaseEntity
{
    public string Title { get; private set; } = default!;
    public string Description { get; private set; } = default!;
    public string Department { get; private set; } = default!;
    public bool IsActive { get; private set; } = true;

    /// <summary>AI-extracted skill graph stored in MongoDB.</summary>
    public SkillGraph? SkillGraph { get; private set; }

    /// <summary>Qdrant point ID for the job embedding (UUID).</summary>
    public Guid? EmbeddingPointId { get; private set; }

    private readonly List<Application> _applications = [];
    public IReadOnlyCollection<Application> Applications => _applications.AsReadOnly();

    private JobPosting() { }

    public JobPosting(string title, string description, string department)
    {
        Title = title;
        Description = description;
        Department = department;

        // Raise domain event so the AI pipeline triggers automatically
        AddDomainEvent(new JobPostingCreatedEvent(Id, title, description));
    }

    public void UpdateDescription(string description)
    {
        Description = description;
        MarkUpdated();
        AddDomainEvent(new JobPostingCreatedEvent(Id, Title, description)); // re-extract
    }

    public void ApplySkillGraph(SkillGraph graph, Guid embeddingPointId)
    {
        SkillGraph = graph;
        EmbeddingPointId = embeddingPointId;
        MarkUpdated();
    }

    public void Deactivate()
    {
        IsActive = false;
        MarkUpdated();
    }
}

/// <summary>
/// AI-extracted structured skill graph for a job posting.
/// Stored in MongoDB — no separate PostgreSQL table needed.
/// </summary>
public class SkillGraph
{
    public List<SkillWeight> RequiredSkills { get; init; } = [];
    public List<SkillWeight> NiceToHaveSkills { get; init; } = [];
    public int ExperienceYearsMin { get; init; }
    public string Seniority { get; init; } = "mid"; // junior/mid/senior/staff/principal
    public List<string> DomainKeywords { get; init; } = [];
    public string JobEmbeddingText { get; init; } = string.Empty;
    public DateTime ExtractedAt { get; init; } = DateTime.UtcNow;
}

public record SkillWeight(
    string Skill,
    double Weight,  // 0.0–1.0
    string Category // frontend/backend/cloud/data/soft/domain
);
