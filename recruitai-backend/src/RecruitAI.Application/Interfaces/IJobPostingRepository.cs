using RecruitAI.Domain.Entities;

namespace RecruitAI.Application.Interfaces;

public interface IJobPostingRepository
{
    Task<JobPosting?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task AddAsync(JobPosting posting, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
