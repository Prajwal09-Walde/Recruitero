using Amazon.S3;
using Azure;
using Azure.AI.OpenAI;
using Hangfire;
using Hangfire.InMemory;
using Hangfire.Mongo;
using Hangfire.Mongo.Migration.Strategies;
using Hangfire.Mongo.Migration.Strategies.Backup;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using Qdrant.Client;
using RecruitAI.Application.Features.Resumes.Commands;
using RecruitAI.Application.Features.InterviewKit.Commands;
using RecruitAI.Application.Interfaces;
using RecruitAI.Infrastructure.AI;
using RecruitAI.Infrastructure.Hubs;
using RecruitAI.Infrastructure.Jobs;
using RecruitAI.Infrastructure.Persistence;
using RecruitAI.Infrastructure.Persistence.Repositories;
using RecruitAI.Infrastructure.Storage;
using RecruitAI.Infrastructure.Webhooks;

namespace RecruitAI.Infrastructure;

/// <summary>
/// Extension method registering all Infrastructure services.
/// Call from Program.cs: builder.Services.AddInfrastructure(configuration)
/// </summary>
public static class InfrastructureServiceRegistration
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // PostgreSQL and EF Core registrations removed (Option 1)

        // ── Repositories ─────────────────────────────────────────────────────────
        services.AddScoped<IApplicationRepository, ApplicationRepository>();
        services.AddScoped<IJobRepository, JobRepository>();
        services.AddScoped<ICandidateRepository, CandidateRepository>();
        services.AddScoped<IInterviewKitRepository, InterviewKitRepository>();
        services.AddScoped<IJobPostingRepository, JobPostingRepository>();
        services.AddScoped<IWebhookConfigurationRepository, WebhookConfigurationRepository>();
        services.AddScoped<IWebhookDeliveryRepository, WebhookDeliveryRepository>();

        // ── AWS S3 ────────────────────────────────────────────────────────────────
        services.AddAWSService<IAmazonS3>();
        services.AddScoped<IStorageService, S3StorageService>();

        // ── OpenAI (Azure SDK) ────────────────────────────────────────────────────
        services.AddSingleton(sp =>
        {
            var endpoint = configuration["OpenAI:Endpoint"]
                ?? "https://api.openai.com/";
            var apiKey = configuration["OpenAI:ApiKey"]
                ?? throw new InvalidOperationException("OpenAI:ApiKey is not configured.");

            // Azure OpenAI endpoint
            if (endpoint.Contains("azure.com"))
                return new OpenAIClient(new Uri(endpoint), new AzureKeyCredential(apiKey));

            // Direct OpenAI
            return new OpenAIClient(apiKey);
        });

        // ── Qdrant ────────────────────────────────────────────────────────────────
        services.AddSingleton(sp =>
        {
            var host = configuration["Qdrant:Host"] ?? "localhost";
            var port = int.Parse(configuration["Qdrant:Port"] ?? "6334");
            return new QdrantClient(host, port);
        });

        // ── MongoDB ───────────────────────────────────────────────────────────────
        services.AddSingleton<MongoDbContext>();

        // ── AI Services ───────────────────────────────────────────────────────────
        services.AddScoped<ResumeChunker>();
        services.AddScoped<IJobSkillExtractor, OpenAIJobSkillExtractor>();
        services.AddScoped<IResumeEmbeddingService, ResumeEmbeddingService>();
        services.AddScoped<IFitScoringService, FitScoringService>();
        services.AddScoped<ICandidateRankingService, CandidateRankingService>();
        services.AddScoped<IInterviewKitGenerationService, InterviewKitGenerationService>();
        services.AddScoped<IResumeProcessingService, ResumeProcessingService>();
        services.AddScoped<IEmailService, Services.EmailService>();



        // ── Webhook ───────────────────────────────────────────────────────────────
        services.AddScoped<IWebhookDispatcher, WebhookDispatcher>();

        // HttpClient with 30s timeout for webhook delivery
        services.AddHttpClient("WebhookClient", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.Add("User-Agent", "RecruitAI-Webhooks/1.0");
        });

        // ── Hangfire (MongoDB Storage with in-memory fallback) ─────────────────
        // Hangfire.Mongo runs a synchronous migration during its constructor.
        // If the MongoDB Atlas cluster is unreachable (e.g. TLS issue on this network),
        // we fall back to in-memory storage so the API can still start.
        services.AddHangfire((provider, cfg) =>
        {
            cfg
                .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
                .UseSimpleAssemblyNameTypeSerializer()
                .UseRecommendedSerializerSettings();

            var connStr = configuration.GetConnectionString("MongoDB")!;
            var logger = provider.GetService<ILogger<MongoDbContext>>();

            try
            {
                var mongoUrl = new MongoUrl(connStr);
                var settings = MongoClientSettings.FromUrl(mongoUrl);
                // Force TLS 1.2 to work around Windows Schannel TLS 1.3 issues with some Atlas clusters
                if (connStr.Contains("mongodb+srv") || connStr.Contains("ssl=true") || connStr.Contains("tls=true"))
                {
                    settings.SslSettings = new SslSettings
                    {
                        EnabledSslProtocols = System.Security.Authentication.SslProtocols.Tls12
                    };
                }
                settings.ServerSelectionTimeout = TimeSpan.FromSeconds(10);
                settings.ConnectTimeout = TimeSpan.FromSeconds(10);
                // Atlas closes idle connections after ~30 min. Keep connections alive to prevent
                // SocketException 10054 on the main MongoClient pool.
                settings.MaxConnectionIdleTime = TimeSpan.FromMinutes(25);

                cfg.UseMongoStorage(
                    settings,
                    mongoUrl.DatabaseName ?? "hangfire",
                    new MongoStorageOptions
                    {
                        MigrationOptions = new MongoMigrationOptions
                        {
                            MigrationStrategy = new MigrateMongoMigrationStrategy(),
                            BackupStrategy = new NoneMongoBackupStrategy()
                        },
                        Prefix = "hangfire",
                        // Use polling instead of a tailable cursor (TailNotificationsCollection).
                        // A tailable cursor holds a persistent TCP connection that Atlas forcibly
                        // closes after ~30 min of inactivity (MongoConnectionException / 10054).
                        // Polling avoids that long-lived connection entirely.
                        CheckQueuedJobsStrategy = CheckQueuedJobsStrategy.Poll,
                        CheckConnection = false
                    });

                logger?.LogInformation("Hangfire configured with MongoDB storage.");
            }
            catch (Exception ex)
            {
                // Database is unreachable — fall back to in-memory storage.
                // Background jobs will not persist across restarts, but the API boots normally.
                logger?.LogWarning(ex, "Could not connect to MongoDB for Hangfire storage. Falling back to in-memory storage.");
                cfg.UseInMemoryStorage();
            }
        });

        services.AddHangfireServer(options =>
        {
            options.Queues = ["resumes", "critical", "default"];
            options.WorkerCount = Environment.ProcessorCount * 2;
        });

        // Register Hangfire job classes for DI-based activation
        services.AddScoped<IProcessResumeJob, ProcessResumeJob>();
        services.AddScoped<GenerateRankingAndKitJob>();
        services.AddScoped<IGenerateInterviewKitJob, GenerateInterviewKitJob>();
        services.AddScoped<IDispatchWebhookJob, DispatchWebhookJob>();

        // ── SignalR ────────────────────────────────────────────────────────────────
        services.AddScoped<IRecruitmentHubContext, RecruitmentHubContextService>();

        return services;
    }


}
