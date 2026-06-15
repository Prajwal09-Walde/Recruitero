using FluentValidation;
using Hangfire;
using MediatR;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using RecruitAI.API.Middleware;
using RecruitAI.Application.Behaviors;
using RecruitAI.Infrastructure;
using RecruitAI.Infrastructure.Hubs;
using Serilog;
using System.Text;
using MongoDB.Driver;
using RecruitAI.Domain.Entities;
using RecruitAI.Infrastructure.Persistence;

// ── Bootstrap Serilog before the host starts so startup errors are captured ──
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

// ── Load .env file ──
LoadDotEnv();
MapEnvironmentVariables();

// ── Free port if already in use ──
FreePortIfBusy();

try
{
    var builder = WebApplication.CreateBuilder(args);

    // ── Serilog ───────────────────────────────────────────────────────────────────
    builder.Host.UseSerilog((ctx, services, cfg) => cfg
        .ReadFrom.Configuration(ctx.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .Enrich.WithProperty("Application", "RecruitAI")
        .WriteTo.Console()
        .WriteTo.Seq(ctx.Configuration["Seq:ServerUrl"] ?? "http://localhost:5341"));

    // ── MediatR + Pipeline Behaviors ─────────────────────────────────────────────
    builder.Services.AddMediatR(cfg =>
    {
        cfg.RegisterServicesFromAssemblyContaining<RecruitAI.Application.Features.Resumes.Commands.BulkUploadResumesCommand>();
    });
    builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
    builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));

    // ── FluentValidation ─────────────────────────────────────────────────────────
    builder.Services.AddValidatorsFromAssemblyContaining<
        RecruitAI.Application.Features.Resumes.Commands.BulkUploadResumesValidator>();

    // ── Memory Cache (leaderboard) ────────────────────────────────────────────────
    builder.Services.AddMemoryCache();

    // ── Infrastructure (EF, S3, Hangfire, Repos) ──────────────────────────────────
    builder.Services.AddInfrastructure(builder.Configuration);
    // ── User auth service (MongoDB-backed registration/login) ──────────────────────
    builder.Services.AddScoped<RecruitAI.API.Services.IUserService, RecruitAI.API.Services.UserService>();
    builder.Services.AddSignalR(opts =>
    {
        opts.EnableDetailedErrors = false;
        opts.MaximumReceiveMessageSize = 102_400;
    });

    // ── JWT Bearer Authentication ─────────────────────────────────────────────────
    var jwtSettings = builder.Configuration.GetSection("Jwt");
    var secretKey   = Encoding.UTF8.GetBytes(jwtSettings["Secret"] ?? "REPLACE_WITH_32+_CHAR_SECRET_KEY_HERE!!");

    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(opts =>
        {
            opts.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
            opts.SaveToken = true;
            opts.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer           = true,
                ValidateAudience         = true,
                ValidateLifetime         = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer              = jwtSettings["Issuer"],
                ValidAudience            = jwtSettings["Audience"],
                IssuerSigningKey         = new SymmetricSecurityKey(secretKey),
                ClockSkew                = TimeSpan.FromSeconds(30)
            };

            // Allow JWT via query string for SignalR WebSocket connections
            opts.Events = new JwtBearerEvents
            {
                OnMessageReceived = ctx =>
                {
                    var accessToken = ctx.Request.Query["access_token"];
                    var path = ctx.HttpContext.Request.Path;
                    if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
                        ctx.Token = accessToken;
                    return Task.CompletedTask;
                }
            };
        });

    // ── Authorization Policies ────────────────────────────────────────────────────
    builder.Services.AddAuthorization(opts =>
    {
        opts.AddPolicy("HrAdminOnly",  p => p.RequireRole("HRAdmin"));
        opts.AddPolicy("TeamLeadUp",  p => p.RequireRole("HRAdmin", "TeamLead"));
        opts.AddPolicy("ViewerUp",     p => p.RequireRole("HRAdmin", "TeamLead", "Viewer"));
    });

    // ── Controllers ───────────────────────────────────────────────────────────────
    builder.Services.AddControllers();
    builder.Services.AddEndpointsApiExplorer();

    // ── Swagger with JWT support ──────────────────────────────────────────────────
    builder.Services.AddSwaggerGen(c =>
    {
        c.SwaggerDoc("v1", new OpenApiInfo
        {
            Title   = "Recruitero API",
            Version = "v1",
            Description = "AI-powered Recruitment Intelligence Platform"
        });

        var jwtScheme = new OpenApiSecurityScheme
        {
            Name         = "Authorization",
            Type         = SecuritySchemeType.Http,
            Scheme       = "bearer",
            BearerFormat = "JWT",
            In           = ParameterLocation.Header,
            Description  = "Enter **Bearer {token}** in the field below.",
            Reference    = new OpenApiReference
            {
                Id   = JwtBearerDefaults.AuthenticationScheme,
                Type = ReferenceType.SecurityScheme
            }
        };

        c.AddSecurityDefinition(jwtScheme.Reference.Id, jwtScheme);
        c.AddSecurityRequirement(new OpenApiSecurityRequirement
        {
            { jwtScheme, Array.Empty<string>() }
        });
    });

    // ── CORS ───────────────────────────────────────────────────────────────────────
    builder.Services.AddCors(opts =>
    {
        opts.AddPolicy("FrontendPolicy", p => p
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials()
            .SetIsOriginAllowed(_ => true)); // Allow any origin to prevent CORS blocks during testing
    });

    // ─────────────────────────────────────────────────────────────────────────────
    var app = builder.Build();
    // ─────────────────────────────────────────────────────────────────────────────

    // ── Request pipeline ─────────────────────────────────────────────────────────
    if (app.Environment.IsDevelopment())
    {
        var mongoContext = app.Services.GetRequiredService<MongoDbContext>();
        _ = Task.Run(async () => await SeedMongoDataAsync(mongoContext));
    }

    app.UseMiddleware<GlobalExceptionMiddleware>(); // Must be first!

    // Enable Swagger in all environments (including production) to support load balancer health checks
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "Recruitero API v1");
        c.DisplayRequestDuration();
        c.EnableFilter();
    });

    app.UseSerilogRequestLogging(opts =>
    {
        opts.EnrichDiagnosticContext = (diag, ctx) =>
        {
            diag.Set("RequestHost", ctx.Request.Host.Value);
            diag.Set("UserAgent", ctx.Request.Headers.UserAgent.ToString());
        };
    });

    app.UseCors("FrontendPolicy");
    app.UseAuthentication();
    app.UseAuthorization();

    // ── Hangfire Dashboard (Admin only) ───────────────────────────────────────────
    app.UseHangfireDashboard("/hangfire", new DashboardOptions
    {
        Authorization = [new HangfireAdminAuthFilter()],
        AppPath = "/swagger"
    });

    // ── SignalR Hub ───────────────────────────────────────────────────────────────
    app.MapHub<RecruitmentHub>("/hubs/recruitment");

    app.MapControllers();
    app.MapGet("/health", () => Results.Ok(new { status = "Healthy" }));

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Recruitero host terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}

    static async Task SeedMongoDataAsync(MongoDbContext mongoContext)
    {
        try
        {
            // Use a short timeout of 5 seconds for seeding to prevent app hanging on startup if DB is unreachable
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var ct = cts.Token;

            var hasJobs = await mongoContext.Jobs.Find(_ => true).AnyAsync(ct);
            if (hasJobs) return;

            var jobId1 = Guid.Parse("1a2b3c4d-5e6f-7a8b-9c0d-1e2f3a4b5c6d");
            var jobId2 = Guid.Parse("2a3b4c5d-6e7f-8a9b-0c1d-2e3f4a5b6c7d");

            var job1 = new Job("Senior React Developer", "We are looking for a Senior React Developer with 5+ years of experience in TypeScript, TailwindCSS, and Next.js. You will build highly responsive UI components and integrate with AI services.", "Engineering");
            job1.SetId(jobId1);

            var job2 = new Job("Product Manager", "We are looking for a Product Manager to lead our Core AI platform team. You should have experience writing PRDs, managing backlogs, and collaborating with engineering teams.", "Product");
            job2.SetId(jobId2);

            await mongoContext.Jobs.InsertManyAsync(new[] { job1, job2 }, cancellationToken: ct);

            var posting1 = new JobPosting("Senior React Developer", job1.Description, "Engineering");
            posting1.SetId(jobId1);
            
            var skillGraph1 = new SkillGraph
            {
                RequiredSkills = new List<SkillWeight>
                {
                    new("React", 0.9, "frontend"),
                    new("TypeScript", 0.8, "frontend")
                },
                NiceToHaveSkills = new List<SkillWeight>
                {
                    new("TailwindCSS", 0.6, "frontend")
                },
                ExperienceYearsMin = 5,
                Seniority = "senior",
                DomainKeywords = new List<string> { "frontend", "web" },
                JobEmbeddingText = "Senior React Developer with experience in TypeScript and TailwindCSS.",
                ExtractedAt = DateTime.UtcNow
            };
            posting1.ApplySkillGraph(skillGraph1, Guid.NewGuid());

            var posting2 = new JobPosting("Product Manager", job2.Description, "Product");
            posting2.SetId(jobId2);
            
            var skillGraph2 = new SkillGraph
            {
                RequiredSkills = new List<SkillWeight>
                {
                    new("Product Management", 0.95, "management")
                },
                NiceToHaveSkills = new List<SkillWeight>
                {
                    new("Agile", 0.7, "management")
                },
                ExperienceYearsMin = 4,
                Seniority = "mid",
                DomainKeywords = new List<string> { "product", "ai" },
                JobEmbeddingText = "Product Manager leading Core AI platform team.",
                ExtractedAt = DateTime.UtcNow
            };
            posting2.ApplySkillGraph(skillGraph2, Guid.NewGuid());
            await mongoContext.JobPostings.InsertManyAsync(new[] { posting1, posting2 }, cancellationToken: ct);

            Log.Information("Development MongoDB database seeded successfully.");
        }
        catch (OperationCanceledException)
        {
            Log.Warning("MongoDB seeding operation timed out. Continuing application startup without seeding.");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not seed MongoDB data because the database is unreachable or connection timed out.");
        }
    }

    static void LoadDotEnv()
    {
        var dir = Directory.GetCurrentDirectory();
        while (dir != null)
        {
            var envPath = Path.Combine(dir, ".env");
            if (File.Exists(envPath))
            {
                foreach (var line in File.ReadAllLines(envPath))
                {
                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#"))
                        continue;

                    var parts = line.Split('=', 2);
                    if (parts.Length != 2)
                        continue;

                    var key = parts[0].Trim();
                    var val = parts[1].Trim().Trim('"', '\'');

                    // Map generic .env keys to standard ASP.NET Core environment variable keys
                    if (key == "JWT_SECRET")
                    {
                        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("Jwt__Secret")))
                        {
                            Environment.SetEnvironmentVariable("Jwt__Secret", val);
                        }
                    }
                    else if (key == "MONGODB_URI")
                    {
                        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ConnectionStrings__MongoDB")))
                        {
                            Environment.SetEnvironmentVariable("ConnectionStrings__MongoDB", val);
                        }
                    }
                    else if (key == "OPENAI_API_KEY")
                    {
                        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("OpenAI__ApiKey")))
                        {
                            Environment.SetEnvironmentVariable("OpenAI__ApiKey", val);
                        }
                    }
                    else
                    {
                        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(key)))
                        {
                            Environment.SetEnvironmentVariable(key, val);
                        }
                    }
                }
                break;
            }
            dir = Directory.GetParent(dir)?.FullName;
        }
    }

    static void FreePortIfBusy()
    {
        try
        {
            var urls = Environment.GetEnvironmentVariable("ASPNETCORE_URLS");
            var ports = new List<int> { 5000, 5001 }; // standard default ports
            if (!string.IsNullOrEmpty(urls))
            {
                foreach (var url in urls.Split(';'))
                {
                    if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
                    {
                        ports.Add(uri.Port);
                    }
                }
            }

            foreach (var port in ports.Distinct())
            {
                KillProcessOnPort(port);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Error checking or freeing up ports during startup.");
        }
    }

    static void KillProcessOnPort(int port)
    {
        try
        {
            var currentPid = System.Diagnostics.Process.GetCurrentProcess().Id;
            if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
            {
                var process = new System.Diagnostics.Process
                {
                    StartInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        Arguments = $"/c netstat -ano | findstr LISTENING | findstr :{port}",
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };
                process.Start();
                string output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();

                var lines = output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 5)
                    {
                        var pidStr = parts[parts.Length - 1].Trim();
                        if (int.TryParse(pidStr, out var pid) && pid != currentPid && pid > 0)
                        {
                            try
                            {
                                var procToKill = System.Diagnostics.Process.GetProcessById(pid);
                                string procName = procToKill.ProcessName.ToLower();
                                if (procName.Contains("recruitai") || procName.Contains("dotnet"))
                                {
                                    procToKill.Kill();
                                    procToKill.WaitForExit(1000);
                                    Log.Information("Killed orphaned process {ProcessName} (PID {Pid}) on port {Port}", procToKill.ProcessName, pid, port);
                                }
                            }
                            catch { }
                        }
                    }
                }
            }
            else
            {
                // Linux/macOS
                var process = new System.Diagnostics.Process
                {
                    StartInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "sh",
                        Arguments = $"-c \"lsof -t -i:{port}\"",
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };
                process.Start();
                string output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();

                var lines = output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    if (int.TryParse(line.Trim(), out var pid) && pid != currentPid && pid > 0)
                    {
                        try
                        {
                            var procToKill = System.Diagnostics.Process.GetProcessById(pid);
                            string procName = procToKill.ProcessName.ToLower();
                            if (procName.Contains("recruitai") || procName.Contains("dotnet"))
                            {
                                procToKill.Kill();
                                procToKill.WaitForExit(1000);
                                Log.Information("Killed orphaned process {ProcessName} (PID {Pid}) on port {Port}", procToKill.ProcessName, pid, port);
                            }
                        }
                        catch { }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to kill process on port {Port}", port);
        }
    }

    static void MapEnvironmentVariables()
    {
        MapKey("JWT_SECRET", "Jwt__Secret");
        MapKey("MONGODB_URI", "ConnectionStrings__MongoDB");
        MapKey("OPENAI_API_KEY", "OpenAI__ApiKey");
        MapKey("OPENAI_ENDPOINT", "OpenAI__Endpoint");
        MapKey("QDRANT_URL", "Qdrant__Url");
        MapKey("QDRANT_API_KEY", "Qdrant__ApiKey");
    }

    static void MapKey(string rawKey, string aspNetKey)
    {
        var val = Environment.GetEnvironmentVariable(rawKey);
        if (!string.IsNullOrEmpty(val) && string.IsNullOrEmpty(Environment.GetEnvironmentVariable(aspNetKey)))
        {
            Environment.SetEnvironmentVariable(aspNetKey, val);
        }
    }

public partial class Program { } // Exposes Program class for WebApplicationFactory in tests
