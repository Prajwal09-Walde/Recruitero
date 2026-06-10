using RecruitAI.Domain.Common;

namespace RecruitAI.Domain.Entities;

/// <summary>A candidate who has submitted one or more job applications.</summary>
public class Candidate : BaseEntity
{
    public string FullName { get; private set; } = default!;
    public string Email { get; private set; } = default!;

    private readonly List<Application> _applications = [];
    public IReadOnlyCollection<Application> Applications => _applications.AsReadOnly();

    private Candidate() { }

    public Candidate(string fullName, string email)
    {
        FullName = fullName;
        Email = email;
    }

    public void UpdateMetadata(string fullName, string email)
    {
        FullName = fullName;
        Email = email;
        MarkUpdated();
    }
}
