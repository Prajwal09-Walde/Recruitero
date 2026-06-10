using MediatR;
using MongoDB.Driver;
using RecruitAI.Application.Interfaces;
using RecruitAI.Domain.Entities;
namespace RecruitAI.Infrastructure.Persistence.Repositories;

using Application = RecruitAI.Domain.Entities.Application;

public class ApplicationRepository(MongoDbContext mongoContext, IMediator mediator) : IApplicationRepository
{
    private readonly List<Application> _tracked = new();

    public async Task<Application?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var app = await mongoContext.Applications.Find(a => a.Id == id).FirstOrDefaultAsync(ct);
        if (app != null)
        {
            // Resolve Candidate
            var candidate = await mongoContext.Candidates.Find(c => c.Id == app.CandidateId).FirstOrDefaultAsync(ct);
            if (candidate != null)
            {
                SetCandidateReflection(app, candidate);
            }

            // Resolve Job
            var job = await mongoContext.Jobs.Find(j => j.Id == app.JobId).FirstOrDefaultAsync(ct);
            if (job != null)
            {
                SetJobReflection(app, job);
            }

            _tracked.Add(app);
        }
        return app;
    }

    public async Task<List<Application>> GetByJobIdAsync(
        Guid jobId, string? statusFilter, int page, int pageSize, CancellationToken ct = default)
    {
        var filterBuilder = Builders<Application>.Filter;
        var filter = filterBuilder.Eq(a => a.JobId, jobId);

        if (!string.IsNullOrWhiteSpace(statusFilter))
        {
            filter &= filterBuilder.Eq(a => a.Status, statusFilter);
        }

        var apps = await mongoContext.Applications
            .Find(filter)
            .SortByDescending(a => a.FitScore)
            .ThenBy(a => a.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Limit(pageSize)
            .ToListAsync(ct);

        if (apps.Count > 0)
        {
            var candidateIds = apps.Select(a => a.CandidateId).Distinct().ToList();
            var candidates = await mongoContext.Candidates
                .Find(Builders<Candidate>.Filter.In(c => c.Id, candidateIds))
                .ToListAsync(ct);

            var candidateMap = candidates.ToDictionary(c => c.Id);

            foreach (var app in apps)
            {
                if (candidateMap.TryGetValue(app.CandidateId, out var candidate))
                {
                    SetCandidateReflection(app, candidate);
                }
                _tracked.Add(app);
            }
        }

        return apps;
    }

    public async Task<int> CountByJobIdAsync(Guid jobId, string? statusFilter, CancellationToken ct = default)
    {
        var filterBuilder = Builders<Application>.Filter;
        var filter = filterBuilder.Eq(a => a.JobId, jobId);

        if (!string.IsNullOrWhiteSpace(statusFilter))
        {
            filter &= filterBuilder.Eq(a => a.Status, statusFilter);
        }

        return (int)await mongoContext.Applications.CountDocumentsAsync(filter, cancellationToken: ct);
    }

    public async Task<int> CountScoredByJobIdAsync(Guid jobId, CancellationToken ct = default) =>
        (int)await mongoContext.Applications.CountDocumentsAsync(
            a => a.JobId == jobId && a.Status == "Scored", cancellationToken: ct);

    public async Task AddAsync(Application application, CancellationToken ct = default)
    {
        _tracked.Add(application);
        await Task.CompletedTask;
    }

    public async Task SaveChangesAsync(CancellationToken ct = default)
    {
        foreach (var app in _tracked.ToList())
        {
            await mongoContext.Applications.ReplaceOneAsync(
                a => a.Id == app.Id,
                app,
                new ReplaceOptions { IsUpsert = true },
                cancellationToken: ct);

            if (app.DomainEvents.Any())
            {
                var events = app.DomainEvents.ToList();
                app.ClearDomainEvents();
                foreach (var ev in events)
                {
                    await mediator.Publish(ev, ct);
                }
            }
        }
        _tracked.Clear();
    }

    private static void SetCandidateReflection(Application app, Candidate candidate)
    {
        var property = typeof(Application).GetProperty(nameof(Application.Candidate));
        property?.SetValue(app, candidate);
    }

    private static void SetJobReflection(Application app, Job job)
    {
        var property = typeof(Application).GetProperty(nameof(Application.Job));
        property?.SetValue(app, job);
    }
}

public class JobRepository(MongoDbContext mongoContext) : IJobRepository
{
    private readonly List<Job> _tracked = new();

    public async Task<Job?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var job = await mongoContext.Jobs.Find(j => j.Id == id).FirstOrDefaultAsync(ct);
        if (job != null)
            _tracked.Add(job);
        return job;
    }

    public async Task<bool> ExistsAsync(Guid id, CancellationToken ct = default) =>
        await mongoContext.Jobs.Find(j => j.Id == id && j.IsActive).AnyAsync(ct);

    public async Task AddAsync(Job job, CancellationToken ct = default)
    {
        _tracked.Add(job);
        await Task.CompletedTask;
    }

    public async Task<List<Job>> GetAllAsync(CancellationToken ct = default) =>
        await mongoContext.Jobs.Find(j => j.IsActive).ToListAsync(ct);

    public async Task SaveChangesAsync(CancellationToken ct = default)
    {
        foreach (var job in _tracked.ToList())
        {
            await mongoContext.Jobs.ReplaceOneAsync(
                j => j.Id == job.Id,
                job,
                new ReplaceOptions { IsUpsert = true },
                cancellationToken: ct);
        }
        _tracked.Clear();
    }
}

public class CandidateRepository(MongoDbContext mongoContext) : ICandidateRepository
{
    public async Task<Candidate?> GetByEmailAsync(string email, CancellationToken ct = default) =>
        await mongoContext.Candidates.Find(c => c.Email == email).FirstOrDefaultAsync(ct);

    public async Task AddAsync(Candidate candidate, CancellationToken ct = default)
    {
        await mongoContext.Candidates.ReplaceOneAsync(
            c => c.Id == candidate.Id,
            candidate,
            new ReplaceOptions { IsUpsert = true },
            cancellationToken: ct);
    }
}

public class InterviewKitRepository(MongoDbContext mongoContext, IMediator mediator) : IInterviewKitRepository
{
    private readonly List<InterviewKit> _tracked = new();

    public async Task<InterviewKit?> GetByApplicationIdAsync(Guid applicationId, CancellationToken ct = default)
    {
        var kit = await mongoContext.InterviewKits.Find(k => k.ApplicationId == applicationId).FirstOrDefaultAsync(ct);
        if (kit != null)
        {
            _tracked.Add(kit);
        }
        return kit;
    }

    public async Task AddAsync(InterviewKit kit, CancellationToken ct = default)
    {
        _tracked.Add(kit);
        await Task.CompletedTask;
    }

    public async Task SaveChangesAsync(CancellationToken ct = default)
    {
        foreach (var kit in _tracked.ToList())
        {
            await mongoContext.InterviewKits.ReplaceOneAsync(
                k => k.Id == kit.Id,
                kit,
                new ReplaceOptions { IsUpsert = true },
                cancellationToken: ct);

            if (kit.DomainEvents.Any())
            {
                var events = kit.DomainEvents.ToList();
                kit.ClearDomainEvents();
                foreach (var ev in events)
                {
                    await mediator.Publish(ev, ct);
                }
            }
        }
        _tracked.Clear();
    }
}
