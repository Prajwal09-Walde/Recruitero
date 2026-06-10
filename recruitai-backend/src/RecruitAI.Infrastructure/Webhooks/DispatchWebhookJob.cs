using Hangfire;
using Microsoft.Extensions.Logging;
using RecruitAI.Application.Interfaces;
using RecruitAI.Infrastructure.Jobs;
using RecruitAI.Shared.Constants;

namespace RecruitAI.Infrastructure.Webhooks;

/// <summary>
/// Hangfire job that builds and dispatches the ATS webhook after the interview kit is generated.
/// Registered as a Hangfire continuation after GenerateRankingAndKitJob completes.
/// </summary>
[AutomaticRetry(Attempts = 2, DelaysInSeconds = [30, 120])]
[Queue(HangfireQueues.Default)]
public sealed class DispatchWebhookJob(
    IApplicationRepository applicationRepository,
    IJobPostingRepository jobPostingRepository,
    IWebhookConfigurationRepository webhookConfigRepository,
    IWebhookDispatcher webhookDispatcher,
    ILogger<DispatchWebhookJob> logger) : IDispatchWebhookJob
{
    public async Task ExecuteAsync(Guid applicationId, CancellationToken ct = default)
    {
        var application = await applicationRepository.GetByIdAsync(applicationId, ct);
        if (application is null)
        {
            logger.LogWarning("[Webhook] Application {Id} not found", applicationId);
            return;
        }

        var jobPosting = await jobPostingRepository.GetByIdAsync(application.JobId, ct);
        var candidate = application.Candidate;

        if (candidate is null)
        {
            logger.LogWarning("[Webhook] Candidate not loaded for application {Id}", applicationId);
            return;
        }

        // Find all active webhook configs for this tenant/job
        // (In a multi-tenant system, TenantId would come from the Job entity)
        var tenantId = Guid.Empty; // TODO: resolve from JobPosting.TenantId
        var configs = await webhookConfigRepository.GetActiveByTenantAsync(tenantId, ct);

        if (configs.Count == 0)
        {
            logger.LogDebug("[Webhook] No active webhook configs for tenant {TenantId}", tenantId);
            return;
        }

        var payload = new WebhookPayload(
            Event: "candidate.scored",
            Timestamp: DateTime.UtcNow,
            JobId: application.JobId,
            ExternalJobId: configs.FirstOrDefault()?.ExternalJobId,
            Candidate: new WebhookCandidatePayload(
                Name: candidate.FullName,
                Email: candidate.Email,
                FitScore: application.FitScore ?? 0m,
                Recommendation: "Maybe", // TODO: pull from AIRanking.narrative
                TopStrengths: [],         // TODO: pull from narrative.strengths
                Gaps: [],                 // TODO: pull from narrative.gaps
                InterviewKitUrl: $"https://app.recruitai.io/kits/{applicationId}"));

        foreach (var config in configs.Where(c => c.Events.Contains("candidate.scored")))
        {
            logger.LogInformation(
                "[Webhook] Dispatching to {Url} for app {AppId}", config.TargetUrl, applicationId);
            await webhookDispatcher.DispatchAsync(config, payload, ct);
        }
    }
}
