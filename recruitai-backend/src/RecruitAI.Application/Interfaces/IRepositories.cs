using RecruitAI.Domain.Entities;

namespace RecruitAI.Application.Interfaces;

/// <summary>Repository for read/write access to core aggregates.</summary>
public interface IApplicationRepository
{
    Task<Domain.Entities.Application?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<List<Domain.Entities.Application>> GetByJobIdAsync(Guid jobId, string? statusFilter, int page, int pageSize, CancellationToken ct = default);
    Task<int> CountByJobIdAsync(Guid jobId, string? statusFilter, CancellationToken ct = default);
    Task<int> CountScoredByJobIdAsync(Guid jobId, CancellationToken ct = default);
    Task AddAsync(Domain.Entities.Application application, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}

public interface IJobRepository
{
    Task<Job?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<bool> ExistsAsync(Guid id, CancellationToken ct = default);
    Task AddAsync(Job job, CancellationToken ct = default);
    Task<List<Job>> GetAllAsync(CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}

public interface ICandidateRepository
{
    Task<Candidate?> GetByEmailAsync(string email, CancellationToken ct = default);
    Task AddAsync(Candidate candidate, CancellationToken ct = default);
}

public interface IInterviewKitRepository
{
    Task<InterviewKit?> GetByApplicationIdAsync(Guid applicationId, CancellationToken ct = default);
    Task AddAsync(InterviewKit kit, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
