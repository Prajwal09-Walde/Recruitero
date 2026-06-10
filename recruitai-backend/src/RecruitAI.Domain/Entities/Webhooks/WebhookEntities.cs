using RecruitAI.Domain.Common;

namespace RecruitAI.Domain.Entities.Webhooks;

/// <summary>Tenant-configured webhook endpoint for outbound ATS notifications.</summary>
public class WebhookConfiguration : BaseEntity
{
    public Guid TenantId { get; private set; }
    public string TargetUrl { get; private set; } = default!;
    public string SecretKey { get; private set; } = default!;  // HMAC signing key (stored encrypted)
    public AtsType AtsType { get; private set; }
    public bool IsActive { get; private set; } = true;
    public List<string> Events { get; private set; } = ["candidate.scored"]; // subscribed events
    public string? ExternalJobId { get; private set; } // ATS-side job reference

    private WebhookConfiguration() { }

    public WebhookConfiguration(
        Guid tenantId, string targetUrl, string secretKey, AtsType atsType, List<string> events)
    {
        TenantId = tenantId;
        TargetUrl = targetUrl;
        SecretKey = secretKey;
        AtsType = atsType;
        Events = events;
    }

    public void Deactivate() { IsActive = false; MarkUpdated(); }
    public void UpdateUrl(string url) { TargetUrl = url; MarkUpdated(); }
    public void SetExternalJobId(string id) { ExternalJobId = id; MarkUpdated(); }
}

public enum AtsType
{
    Greenhouse = 0,
    Lever = 1,
    Custom = 99
}

/// <summary>Log record of a single webhook delivery attempt.</summary>
public class WebhookDelivery : BaseEntity
{
    public Guid ConfigId { get; private set; }
    public string Payload { get; private set; } = default!;      // JSON body sent
    public string EventType { get; private set; } = default!;
    public int? ResponseCode { get; private set; }
    public string? ResponseBody { get; private set; }
    public int AttemptCount { get; private set; }
    public bool DeliveredSuccessfully { get; private set; }
    public DateTime? DeliveredAt { get; private set; }
    public string? ErrorMessage { get; private set; }

    private WebhookDelivery() { }

    public WebhookDelivery(Guid configId, string payload, string eventType)
    {
        ConfigId = configId;
        Payload = payload;
        EventType = eventType;
    }

    public void RecordAttempt(int statusCode, string? body)
    {
        AttemptCount++;
        ResponseCode = statusCode;
        ResponseBody = body?[..Math.Min(500, body.Length)];

        if (statusCode is >= 200 and < 300)
        {
            DeliveredSuccessfully = true;
            DeliveredAt = DateTime.UtcNow;
        }
        MarkUpdated();
    }

    public void RecordFailure(string error)
    {
        AttemptCount++;
        ErrorMessage = error;
        MarkUpdated();
    }
}
