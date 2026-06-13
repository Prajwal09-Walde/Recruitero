using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;
using RecruitAI.Domain.Entities;
using RecruitAI.Infrastructure.Persistence;
using Mongo2Go;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace RecruitAI.IntegrationTests;

using Application = RecruitAI.Domain.Entities.Application;

/// <summary>
/// Custom WebApplicationFactory that replaces the real MongoDB database
/// with a test database for isolated, fast integration tests.
/// </summary>
public class RecruitAIWebAppFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private static readonly Type _dummyRateLimit = typeof(AspNetCoreRateLimit.IpRateLimitOptions);
    private MongoDbRunner? _mongoRunner;

    public RecruitAIWebAppFactory()
    {
    }

    public Task InitializeAsync()
    {
        // Start the ephemeral MongoDB instance
        _mongoRunner = MongoDbRunner.Start();

        // Force local MongoDB instance, dummy OpenAI key, and mock JWT secret for integration tests
        // to prevent connecting to production or Atlas database during startup / Hangfire server initialization.
        Environment.SetEnvironmentVariable("ConnectionStrings__MongoDB", _mongoRunner.ConnectionString);
        Environment.SetEnvironmentVariable("OpenAI__ApiKey", "test-api-key-for-tests");
        Environment.SetEnvironmentVariable("Jwt__Secret", "REPLACE_WITH_32+_CHAR_SECRET_KEY_HERE!!");

        return Task.CompletedTask;
    }

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        _mongoRunner?.Dispose();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            // Remove the real MongoDbContext registration
            var descriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(MongoDbContext));
            if (descriptor is not null)
                services.Remove(descriptor);

            // Re-register MongoDbContext to point to a test database (recruitai_integration_tests)
            services.AddSingleton(sp =>
            {
                var inMemoryConfig = new ConfigurationBuilder()
                    .AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["ConnectionStrings:MongoDB"] = Environment.GetEnvironmentVariable("ConnectionStrings__MongoDB")
                    })
                    .Build();

                return new MongoDbContext(inMemoryConfig);
            });

            // Seed test data
            var sp = services.BuildServiceProvider();
            using var scope = sp.CreateScope();
            var mongoContext = scope.ServiceProvider.GetRequiredService<MongoDbContext>();
            
            // Drop database to ensure a clean test run
            mongoContext.Jobs.Database.Client.DropDatabase("recruitai_integration_tests");

            SeedTestData(mongoContext);
        });

        builder.UseEnvironment("Testing");
    }

    public static Guid TestJobId { get; } = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static Guid TestApplicationId { get; } = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static void SeedTestData(MongoDbContext db)
    {
        var job = new Job("Senior .NET Engineer", "Build AI products", "Engineering");
        typeof(RecruitAI.Domain.Common.BaseEntity).GetProperty("Id")!.SetValue(job, TestJobId);
        db.Jobs.InsertOne(job);

        var candidate = new Candidate("Jane Doe", "jane@example.com");
        db.Candidates.InsertOne(candidate);

        var application = new Application(TestJobId, candidate.Id, "resumes/test/jane.pdf");
        typeof(RecruitAI.Domain.Common.BaseEntity).GetProperty("Id")!.SetValue(application, TestApplicationId);
        application.MarkScored(92.5m, 1);
        db.Applications.InsertOne(application);

        var questions = new List<InterviewQuestion>
        {
            new("Technical", "Explain the CQRS pattern.", "Medium", "Tests architecture knowledge."),
            new("Behavioral", "Describe a time you improved CI/CD.", "Easy", "Assesses DevOps mindset.")
        };
        var kit = new InterviewKit(TestApplicationId, questions);
        db.InterviewKits.InsertOne(kit);
    }
}
