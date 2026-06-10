using MongoDB.Driver;
using RecruitAI.Application.Interfaces;
using RecruitAI.Domain.Entities.Webhooks;
using RecruitAI.Infrastructure.Persistence;

namespace RecruitAI.Infrastructure.Webhooks;

public class WebhookConfigurationRepository(MongoDbContext mongoContext)
    : IWebhookConfigurationRepository
{
    private readonly List<WebhookConfiguration> _tracked = new();

    public async Task<WebhookConfiguration?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var config = await mongoContext.WebhookConfigurations.Find(w => w.Id == id).FirstOrDefaultAsync(ct);
        if (config != null)
        {
            _tracked.Add(config);
        }
        return config;
    }

    public async Task<List<WebhookConfiguration>> GetActiveByTenantAsync(
        Guid tenantId, CancellationToken ct = default)
    {
        var configs = await mongoContext.WebhookConfigurations
            .Find(w => w.TenantId == tenantId && w.IsActive)
            .ToListAsync(ct);

        foreach (var config in configs)
        {
            _tracked.Add(config);
        }

        return configs;
    }

    public async Task AddAsync(WebhookConfiguration config, CancellationToken ct = default)
    {
        _tracked.Add(config);
        await Task.CompletedTask;
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await GetByIdAsync(id, ct);
        if (entity is not null)
        {
            entity.Deactivate(); // Soft delete
            await SaveChangesAsync(ct);
        }
    }

    public async Task SaveChangesAsync(CancellationToken ct = default)
    {
        foreach (var config in _tracked.ToList())
        {
            await mongoContext.WebhookConfigurations.ReplaceOneAsync(
                w => w.Id == config.Id,
                config,
                new ReplaceOptions { IsUpsert = true },
                cancellationToken: ct);
        }
        _tracked.Clear();
    }
}

public class WebhookDeliveryRepository(MongoDbContext mongoContext)
    : IWebhookDeliveryRepository
{
    private readonly List<WebhookDelivery> _tracked = new();

    public async Task<List<WebhookDelivery>> GetByConfigIdAsync(
        Guid configId, int page, int pageSize, CancellationToken ct = default)
    {
        var deliveries = await mongoContext.WebhookDeliveries
            .Find(d => d.ConfigId == configId)
            .SortByDescending(d => d.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Limit(pageSize)
            .ToListAsync(ct);

        foreach (var delivery in deliveries)
        {
            _tracked.Add(delivery);
        }

        return deliveries;
    }

    public async Task AddAsync(WebhookDelivery delivery, CancellationToken ct = default)
    {
        _tracked.Add(delivery);
        await Task.CompletedTask;
    }

    public async Task SaveChangesAsync(CancellationToken ct = default)
    {
        foreach (var delivery in _tracked.ToList())
        {
            await mongoContext.WebhookDeliveries.ReplaceOneAsync(
                d => d.Id == delivery.Id,
                delivery,
                new ReplaceOptions { IsUpsert = true },
                cancellationToken: ct);
        }
        _tracked.Clear();
    }
}
