using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RecruitAI.Application.Interfaces;
using RecruitAI.Domain.Entities.Webhooks;
using RecruitAI.Shared.Constants;

namespace RecruitAI.API.Controllers;

/// <summary>
/// Manages outbound ATS webhook configurations and delivery logs.
/// Only HR Admins can configure webhooks.
/// </summary>
[ApiController]
[Route("api/webhooks")]
[Authorize(Roles = Roles.HrAdmin)]
[Produces("application/json")]
public sealed class WebhooksController(
    IWebhookConfigurationRepository configRepository,
    IWebhookDeliveryRepository deliveryRepository) : ControllerBase
{
    // ── POST /api/webhooks — Create webhook config ────────────────────────────────

    /// <summary>Register a new ATS webhook endpoint.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(WebhookConfigDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateWebhook(
        [FromBody] CreateWebhookRequest request,
        CancellationToken ct)
    {
        var config = new WebhookConfiguration(
            tenantId: request.TenantId,
            targetUrl: request.TargetUrl,
            secretKey: request.SecretKey,
            atsType: Enum.Parse<AtsType>(request.AtsType, ignoreCase: true),
            events: request.Events);

        await configRepository.AddAsync(config, ct);
        await configRepository.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(GetWebhook), new { id = config.Id },
            ToDto(config));
    }

    // ── GET /api/webhooks — List all webhook configs ──────────────────────────────

    /// <summary>List all webhook configurations for a tenant.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(List<WebhookConfigDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListWebhooks(
        [FromQuery] Guid tenantId,
        CancellationToken ct)
    {
        var configs = await configRepository.GetActiveByTenantAsync(tenantId, ct);
        return Ok(configs.Select(ToDto).ToList());
    }

    // ── GET /api/webhooks/{id} — Get single config ────────────────────────────────

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(WebhookConfigDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetWebhook([FromRoute] Guid id, CancellationToken ct)
    {
        var config = await configRepository.GetByIdAsync(id, ct);
        return config is null ? NotFound() : Ok(ToDto(config));
    }

    // ── DELETE /api/webhooks/{id} — Soft delete ───────────────────────────────────

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteWebhook([FromRoute] Guid id, CancellationToken ct)
    {
        await configRepository.DeleteAsync(id, ct);
        return NoContent();
    }

    // ── GET /api/webhooks/{id}/deliveries — Delivery history ─────────────────────

    /// <summary>Returns paginated delivery log for a webhook configuration.</summary>
    [HttpGet("{id:guid}/deliveries")]
    [ProducesResponseType(typeof(List<WebhookDeliveryDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetDeliveries(
        [FromRoute] Guid id,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var deliveries = await deliveryRepository.GetByConfigIdAsync(id, page, pageSize, ct);
        return Ok(deliveries.Select(d => new WebhookDeliveryDto(
            d.Id, d.ConfigId, d.EventType, d.ResponseCode, d.AttemptCount,
            d.DeliveredSuccessfully, d.DeliveredAt, d.ErrorMessage, d.CreatedAt)).ToList());
    }

    // ── Mapping helpers ───────────────────────────────────────────────────────────

    private static WebhookConfigDto ToDto(WebhookConfiguration c) =>
        new(c.Id, c.TenantId, c.TargetUrl, c.AtsType.ToString(),
            c.IsActive, c.Events, c.CreatedAt);
}

// ── Request / Response DTOs ──────────────────────────────────────────────────

public record CreateWebhookRequest(
    Guid TenantId,
    string TargetUrl,
    string SecretKey,          // Provided by tenant; never returned in responses
    string AtsType,            // "Greenhouse" | "Lever" | "Custom"
    List<string> Events
);

public record WebhookConfigDto(
    Guid Id,
    Guid TenantId,
    string TargetUrl,
    string AtsType,
    bool IsActive,
    List<string> Events,
    DateTime CreatedAt
);

public record WebhookDeliveryDto(
    Guid Id,
    Guid ConfigId,
    string EventType,
    int? ResponseCode,
    int AttemptCount,
    bool DeliveredSuccessfully,
    DateTime? DeliveredAt,
    string? ErrorMessage,
    DateTime CreatedAt
);
