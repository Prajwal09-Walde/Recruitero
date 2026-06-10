using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;
using RecruitAI.Application.Interfaces;
using RecruitAI.Domain.Entities.Webhooks;

namespace RecruitAI.Infrastructure.Webhooks;

/// <summary>
/// HTTP-based webhook dispatcher with:
/// - HMAC-SHA256 request signing (X-RecruitAI-Signature header)
/// - Polly retry: 3 attempts with 5s / 30s / 120s backoff
/// - Delivery log persistence for auditability
/// </summary>
public sealed class WebhookDispatcher(
    IHttpClientFactory httpClientFactory,
    IWebhookDeliveryRepository deliveryRepository,
    ILogger<WebhookDispatcher> logger) : IWebhookDispatcher
{
    private static readonly TimeSpan[] RetryDelays = [
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(30),
        TimeSpan.FromSeconds(120)
    ];

    private static readonly AsyncRetryPolicy<HttpResponseMessage> RetryPolicy = Policy
        .HandleResult<HttpResponseMessage>(r => !r.IsSuccessStatusCode)
        .Or<HttpRequestException>()
        .WaitAndRetryAsync(RetryDelays);

    public async Task DispatchAsync(
        WebhookConfiguration config,
        WebhookPayload payload,
        CancellationToken ct = default)
    {
        var payloadJson = HmacSigningUtility.SerializePayload(payload);
        var signature = HmacSigningUtility.ComputeSignature(payloadJson, config.SecretKey);

        var delivery = new WebhookDelivery(config.Id, payloadJson, payload.Event);
        await deliveryRepository.AddAsync(delivery, ct);
        await deliveryRepository.SaveChangesAsync(ct);

        var httpClient = httpClientFactory.CreateClient("WebhookClient");

        int attempt = 0;
        HttpResponseMessage? lastResponse = null;
        Exception? lastException = null;

        // ── Custom retry loop for accurate delivery log recording ────────────────
        while (attempt < 3)
        {
            attempt++;
            try
            {
                var request = new HttpRequestMessage(HttpMethod.Post, config.TargetUrl)
                {
                    Content = new StringContent(payloadJson, System.Text.Encoding.UTF8, "application/json")
                };
                request.Headers.Add(HmacSigningUtility.SignatureHeader, signature);
                request.Headers.Add("X-RecruitAI-Event", payload.Event);
                request.Headers.Add("X-RecruitAI-Delivery-Id", delivery.Id.ToString());

                lastResponse = await httpClient.SendAsync(request, ct);
                var responseBody = await lastResponse.Content.ReadAsStringAsync(ct);

                delivery.RecordAttempt((int)lastResponse.StatusCode, responseBody);
                await deliveryRepository.SaveChangesAsync(ct);

                if (lastResponse.IsSuccessStatusCode)
                {
                    logger.LogInformation(
                        "[Webhook] Delivered {Event} to {Url} (attempt {Attempt}): {Code}",
                        payload.Event, config.TargetUrl, attempt, (int)lastResponse.StatusCode);
                    return;
                }

                logger.LogWarning(
                    "[Webhook] Non-success from {Url}: {Code}. Attempt {Attempt}/3",
                    config.TargetUrl, (int)lastResponse.StatusCode, attempt);
            }
            catch (Exception ex)
            {
                lastException = ex;
                delivery.RecordFailure(ex.Message);
                await deliveryRepository.SaveChangesAsync(ct);

                logger.LogWarning(ex,
                    "[Webhook] HTTP error on attempt {Attempt}/3 to {Url}",
                    attempt, config.TargetUrl);
            }

            // Wait before next retry (unless this was the last attempt)
            if (attempt < 3)
                await Task.Delay(RetryDelays[attempt - 1], ct);
        }

        logger.LogError(
            "[Webhook] All {Attempts} delivery attempts failed for {Url}. Event: {Event}",
            attempt, config.TargetUrl, payload.Event);
    }
}
