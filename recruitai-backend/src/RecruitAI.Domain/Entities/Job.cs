using RecruitAI.Domain.Common;

namespace RecruitAI.Domain.Entities;

/// <summary>Represents a job posting that candidates apply to.</summary>
public class Job : BaseEntity
{
    public string Title { get; private set; } = default!;
    public string Description { get; private set; } = default!;
    public string Department { get; private set; } = default!;
    public bool IsActive { get; private set; } = true;

    private readonly List<Application> _applications = [];
    public IReadOnlyCollection<Application> Applications => _applications.AsReadOnly();

    // EF constructor
    private Job() { }

    public Job(string title, string description, string department)
    {
        Title = title;
        Description = description;
        Department = department;
    }

    public void Deactivate()
    {
        IsActive = false;
        MarkUpdated();
    }
}
