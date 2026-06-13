using Microsoft.Extensions.Configuration;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Conventions;
using MongoDB.Driver;
using RecruitAI.Domain.Entities;
using RecruitAI.Domain.Entities.Webhooks;
namespace RecruitAI.Infrastructure.Persistence;

using Application = RecruitAI.Domain.Entities.Application;

/// <summary>
/// Context class for MongoDB operations (MongoDB Atlas / Local).
/// Manages connection lifetime, collections, and domain type BSON mappings.
/// </summary>
public class MongoDbContext
{
    private readonly IMongoDatabase _database;

    static MongoDbContext()
    {
        // Set up MongoDB conventions
        var conventionPack = new ConventionPack
        {
            new CamelCaseElementNameConvention(),
            new IgnoreExtraElementsConvention(true)
        };
        ConventionRegistry.Register("RecruitAIConventions", conventionPack, _ => true);

        // Mappings to ignore navigation properties and events during BSON serialization
        BsonClassMap.RegisterClassMap<Domain.Common.BaseEntity>(cm =>
        {
            cm.AutoMap();
            cm.MapIdProperty(c => c.Id);
            cm.UnmapProperty(c => c.DomainEvents);
        });

        BsonClassMap.RegisterClassMap<Job>(cm =>
        {
            cm.AutoMap();
            cm.UnmapProperty(c => c.Applications);
        });

        BsonClassMap.RegisterClassMap<Candidate>(cm =>
        {
            cm.AutoMap();
            cm.UnmapProperty(c => c.Applications);
        });

        BsonClassMap.RegisterClassMap<Application>(cm =>
        {
            cm.AutoMap();
            cm.UnmapProperty(c => c.Job);
            cm.UnmapProperty(c => c.Candidate);
            cm.UnmapProperty(c => c.InterviewKit);
        });

        BsonClassMap.RegisterClassMap<InterviewKit>(cm =>
        {
            cm.AutoMap();
            cm.UnmapProperty(c => c.Application);
        });

        BsonClassMap.RegisterClassMap<JobPosting>(cm =>
        {
            cm.AutoMap();
            cm.UnmapProperty(c => c.Applications);
        });

        BsonClassMap.RegisterClassMap<AppUser>(cm =>
        {
            cm.AutoMap();
        });
    }

    public MongoDbContext(IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("MongoDB")
            ?? throw new InvalidOperationException("MongoDB connection string is not configured.");

        var mongoUrl = new MongoUrl(connectionString);
        var settings = MongoClientSettings.FromUrl(mongoUrl);

        // Force TLS 1.2 to bypass Windows Schannel/TLS 1.3 bugs (internal TLS alert internal error)
        if (connectionString.Contains("mongodb+srv") || connectionString.Contains("ssl=true") || connectionString.Contains("tls=true"))
        {
            settings.SslSettings = new SslSettings
            {
                EnabledSslProtocols = System.Security.Authentication.SslProtocols.Tls12
            };
        }

        // Optimize database connection latency and pool reuse
        settings.ServerSelectionTimeout = TimeSpan.FromSeconds(5);
        settings.ConnectTimeout = TimeSpan.FromSeconds(5);
        settings.MaxConnectionIdleTime = TimeSpan.FromMinutes(25); // Atlas closed connection prevention
        settings.MinConnectionPoolSize = 5;                        // Warm connections ready
        settings.MaxConnectionPoolSize = 100;

        var client = new MongoClient(settings);
        
        var databaseName = mongoUrl.DatabaseName ?? "recruitai";
        _database = client.GetDatabase(databaseName);

        // Run index creation in the background to prevent blocking application startup and initial requests
        Task.Run(CreateIndexes);
    }

    private void CreateIndexes()
    {
        try
        {
            // Unique sparse index on Candidate Email
            var candidateEmailKeys = Builders<Candidate>.IndexKeys.Ascending(c => c.Email);
            var candidateEmailOptions = new CreateIndexOptions { Unique = true, Sparse = true };
            Candidates.Indexes.CreateOne(new CreateIndexModel<Candidate>(candidateEmailKeys, candidateEmailOptions));

            // Compound index on Application JobId + FitScore + CreatedAt
            var appJobScoreKeys = Builders<Application>.IndexKeys
                .Ascending(a => a.JobId)
                .Descending(a => a.FitScore)
                .Ascending(a => a.CreatedAt);
            Applications.Indexes.CreateOne(new CreateIndexModel<Application>(appJobScoreKeys));

            // Compound index on Application JobId + Status + FitScore + CreatedAt
            var appJobStatusScoreKeys = Builders<Application>.IndexKeys
                .Ascending(a => a.JobId)
                .Ascending(a => a.Status)
                .Descending(a => a.FitScore)
                .Ascending(a => a.CreatedAt);
            Applications.Indexes.CreateOne(new CreateIndexModel<Application>(appJobStatusScoreKeys));

            // Unique index on AppUser Email
            var userEmailKeys = Builders<AppUser>.IndexKeys.Ascending(u => u.Email);
            var userEmailOptions = new CreateIndexOptions { Unique = true };
            Users.Indexes.CreateOne(new CreateIndexModel<AppUser>(userEmailKeys, userEmailOptions));

            // Unique sparse index on InterviewKit ApplicationId
            var kitAppIdKeys = Builders<InterviewKit>.IndexKeys.Ascending(k => k.ApplicationId);
            var kitAppIdOptions = new CreateIndexOptions { Unique = true, Sparse = true };
            InterviewKits.Indexes.CreateOne(new CreateIndexModel<InterviewKit>(kitAppIdKeys, kitAppIdOptions));
        }
        catch (Exception ex)
        {
            // Fail-safe to avoid crashing startup
            System.Console.WriteLine($"[MongoDB Indexing] Failed to create indexes: {ex.Message}");
        }
    }

    // Collections exposing domain aggregates
    public IMongoCollection<AppUser> Users => _database.GetCollection<AppUser>("users");
    public IMongoCollection<Job> Jobs => _database.GetCollection<Job>("jobs");
    public IMongoCollection<Candidate> Candidates => _database.GetCollection<Candidate>("candidates");
    public IMongoCollection<Application> Applications => _database.GetCollection<Application>("applications");
    public IMongoCollection<InterviewKit> InterviewKits => _database.GetCollection<InterviewKit>("interview_kits");
    public IMongoCollection<JobPosting> JobPostings => _database.GetCollection<JobPosting>("job_postings");
    public IMongoCollection<WebhookConfiguration> WebhookConfigurations => _database.GetCollection<WebhookConfiguration>("webhook_configurations");
    public IMongoCollection<WebhookDelivery> WebhookDeliveries => _database.GetCollection<WebhookDelivery>("webhook_deliveries");
}
