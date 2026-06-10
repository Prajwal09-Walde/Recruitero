using RecruitAI.Domain.Common;

namespace RecruitAI.Domain.Entities;

/// <summary>AI-generated interview kit associated with a scored application.</summary>
public class InterviewKit : BaseEntity
{
    public Guid ApplicationId { get; private set; }
    public List<InterviewQuestion> Questions { get; private set; } = [];
    public bool IsGenerated => Questions.Count > 0;

    // Navigation
    public Application Application { get; private set; } = default!;

    private InterviewKit() { }

    public InterviewKit(Guid applicationId, List<InterviewQuestion> questions)
    {
        ApplicationId = applicationId;
        Questions = questions;
    }

    public void Regenerate(List<InterviewQuestion> questions)
    {
        Questions = questions;
        MarkUpdated();
    }
}

public record InterviewQuestion(
    string Category,
    string Question,
    string Difficulty,
    string Rationale
);
