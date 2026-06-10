using RecruitAI.Domain.Common;
using RecruitAI.Domain.Events;
using RecruitAI.Shared.Constants;

namespace RecruitAI.Domain.Entities;

/// <summary>
/// Represents a single candidate's application for a job.
/// Central aggregate tracking the full lifecycle from upload to scoring.
/// </summary>
public class Application : BaseEntity
{
    public Guid JobId { get; private set; }
    public Guid CandidateId { get; private set; }
    public string ResumeS3Key { get; private set; } = default!;
    public string Status { get; private set; } = ApplicationStatus.Queued;
    public decimal? FitScore { get; private set; }
    public int? Rank { get; private set; }
    public string? ErrorMessage { get; private set; }
    public int RetryCount { get; private set; }

    // Navigation properties
    public Job Job { get; private set; } = default!;
    public Candidate Candidate { get; private set; } = default!;
    public InterviewKit? InterviewKit { get; private set; }

    private Application() { }

    public Application(Guid jobId, Guid candidateId, string resumeS3Key)
    {
        JobId = jobId;
        CandidateId = candidateId;
        ResumeS3Key = resumeS3Key;
        Status = ApplicationStatus.Queued;
        AddDomainEvent(new ApplicationStatusChangedEvent(Id, Status, CandidateId, JobId));
    }

    public void MarkProcessing()
    {
        Status = ApplicationStatus.Processing;
        MarkUpdated();
        AddDomainEvent(new ApplicationStatusChangedEvent(Id, Status, CandidateId, JobId));
    }

    public void MarkScored(decimal fitScore, int rank)
    {
        FitScore = fitScore;
        Rank = rank;
        Status = ApplicationStatus.Scored;
        MarkUpdated();
        AddDomainEvent(new ApplicationStatusChangedEvent(Id, Status, CandidateId, JobId));
    }

    public void MarkFailed(string errorMessage)
    {
        ErrorMessage = errorMessage;
        Status = ApplicationStatus.Failed;
        RetryCount++;
        MarkUpdated();
        AddDomainEvent(new ApplicationStatusChangedEvent(Id, Status, CandidateId, JobId));
    }

    public void UpdateRank(int rank)
    {
        Rank = rank;
        MarkUpdated();
    }

    public void UpdateStatus(string status)
    {
        Status = status;
        MarkUpdated();
        AddDomainEvent(new ApplicationStatusChangedEvent(Id, Status, CandidateId, JobId));
    }
}
