using MediatR;
using MongoDB.Driver;
using RecruitAI.Application.Interfaces;
using RecruitAI.Domain.Entities;
using RecruitAI.Infrastructure.Persistence;

namespace RecruitAI.Infrastructure.Persistence.Repositories;

public class JobPostingRepository(MongoDbContext mongoContext, IMediator mediator) : IJobPostingRepository
{
    private readonly List<JobPosting> _tracked = new();

    public async Task<JobPosting?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var posting = await mongoContext.JobPostings.Find(j => j.Id == id).FirstOrDefaultAsync(ct);
        if (posting != null)
        {
            _tracked.Add(posting);
        }
        return posting;
    }

    public async Task AddAsync(JobPosting posting, CancellationToken ct = default)
    {
        _tracked.Add(posting);
        await Task.CompletedTask;
    }

    public async Task SaveChangesAsync(CancellationToken ct = default)
    {
        foreach (var posting in _tracked.ToList())
        {
            await mongoContext.JobPostings.ReplaceOneAsync(
                j => j.Id == posting.Id,
                posting,
                new ReplaceOptions { IsUpsert = true },
                cancellationToken: ct);

            if (posting.DomainEvents.Any())
            {
                var events = posting.DomainEvents.ToList();
                posting.ClearDomainEvents();
                foreach (var ev in events)
                {
                    await mediator.Publish(ev, ct);
                }
            }
        }
        _tracked.Clear();
    }
}
