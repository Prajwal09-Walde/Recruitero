namespace RecruitAI.Shared.Constants;

/// <summary>JWT role claim values for authorization policies.</summary>
public static class Roles
{
    public const string HrAdmin  = "HRAdmin";
    public const string Recruiter = "Recruiter";
    public const string Viewer    = "Viewer";
}

/// <summary>Application status lifecycle values.</summary>
public static class ApplicationStatus
{
    public const string Queued           = "Queued";
    public const string Processing       = "Processing";
    public const string Scored           = "Scored";
    public const string Failed           = "Failed";
    public const string SentToRecruiter  = "SentToRecruiter";
    public const string Shortlisted      = "Shortlisted";
    public const string Rejected         = "Rejected";
}

/// <summary>SignalR group / event name constants.</summary>
public static class HubEvents
{
    public const string ResumeUploaded      = nameof(ResumeUploaded);
    public const string ProcessingStarted   = nameof(ProcessingStarted);
    public const string FitScoreReady       = nameof(FitScoreReady);
    public const string InterviewKitReady   = nameof(InterviewKitReady);
    public const string ProcessingFailed    = nameof(ProcessingFailed);
    public const string LeaderboardUpdated  = nameof(LeaderboardUpdated);
}

/// <summary>Cache key templates.</summary>
public static class CacheKeys
{
    public static string Leaderboard(Guid jobId) => $"leaderboard:{jobId}";
    public static string InterviewKit(Guid applicationId) => $"interview-kit:{applicationId}";
}

/// <summary>Hangfire queue names.</summary>
public static class HangfireQueues
{
    public const string Default  = "default";
    public const string Critical = "critical";
    public const string Resumes  = "resumes";
}
