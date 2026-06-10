using RecruitAI.Domain.Entities.Webhooks;

namespace RecruitAI.Application.Interfaces;

/// <summary>Dispatches webhook notifications with retry and HMAC signing.</summary>
public interface IWebhookDispatcher
{
    Task DispatchAsync(
        WebhookConfiguration config,
        WebhookPayload payload,
        CancellationToken ct = default);
}

public interface IWebhookConfigurationRepository
{
    Task<WebhookConfiguration?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<List<WebhookConfiguration>> GetActiveByTenantAsync(Guid tenantId, CancellationToken ct = default);
    Task AddAsync(WebhookConfiguration config, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}

public interface IWebhookDeliveryRepository
{
    Task<List<WebhookDelivery>> GetByConfigIdAsync(Guid configId, int page, int pageSize, CancellationToken ct = default);
    Task AddAsync(WebhookDelivery delivery, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}

// ── Webhook Payload DTO ───────────────────────────────────────────────────────

public record WebhookPayload(
    string Event,                    // "candidate.scored"
    DateTime Timestamp,
    Guid JobId,
    string? ExternalJobId,
    WebhookCandidatePayload Candidate
);

public record WebhookCandidatePayload(
    string Name,
    string Email,
    decimal FitScore,
    string Recommendation,           // "Strong Yes" | "Yes" | "Maybe" | "No"
    List<string> TopStrengths,
    List<string> Gaps,
    string? InterviewKitUrl
);
