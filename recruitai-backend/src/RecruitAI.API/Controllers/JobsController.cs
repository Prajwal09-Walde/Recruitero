using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RecruitAI.Application.Features.Resumes.Commands;
using RecruitAI.Application.Interfaces;
using RecruitAI.Domain.Entities;
using RecruitAI.Infrastructure.Persistence;
using RecruitAI.Shared.Constants;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace RecruitAI.API.Controllers;

/// <summary>
/// Handles job-scoped operations: bulk resume upload, leaderboard, job creation, and previewing skills.
/// </summary>
[ApiController]
[Route("api/jobs")]
[Authorize]
[Produces("application/json")]
public sealed class JobsController(
    IMediator mediator,
    IJobRepository jobRepository,
    IJobPostingRepository jobPostingRepository,
    IJobSkillExtractor skillExtractor,
    IHttpClientFactory httpClientFactory) : ControllerBase
{
    /// <summary>
    /// Bulk upload PDF resumes for a job opening.
    /// Accepts up to 20 PDF files (max 5MB each) via multipart/form-data.
    /// </summary>
    [HttpPost("{jobId:guid}/applications/bulk-upload")]
    [Authorize(Roles = $"{Roles.HrAdmin},{Roles.Viewer}")]
    [RequestSizeLimit(110 * 1024 * 1024)] // 20 files × 5.5MB buffer
    [DisableRequestSizeLimit]
    [ProducesResponseType(typeof(BulkUploadResumesResult), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> BulkUploadResumes(
        [FromRoute] Guid jobId,
        [FromForm] IFormFileCollection files,
        CancellationToken cancellationToken)
    {
        string? candidateEmail = null;
        if (User.IsInRole(Roles.Viewer))
        {
            candidateEmail = User.FindFirst(ClaimTypes.Email)?.Value ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        }

        var command = new BulkUploadResumesCommand(jobId, files.ToList(), candidateEmail);
        var result = await mediator.Send(command, cancellationToken);
        return AcceptedAtAction(nameof(BulkUploadResumes), result);
    }

    /// <summary>
    /// Returns the ranked leaderboard for a job.
    /// Cached for 30 seconds server-side.
    /// </summary>
    [HttpGet("{jobId:guid}/leaderboard")]
    [Authorize(Roles = $"{Roles.HrAdmin},{Roles.Recruiter},{Roles.Viewer}")]
    [ProducesResponseType(typeof(Application.Features.Jobs.Queries.LeaderboardResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetLeaderboard(
        [FromRoute] Guid jobId,
        [FromQuery] string? status = "All",
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var role = User.FindFirst(ClaimTypes.Role)?.Value;
        var email = User.FindFirst(ClaimTypes.Email)?.Value ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        var query = new Application.Features.Jobs.Queries.GetLeaderboardQuery(
            jobId, status, page, pageSize, role, email);
        var result = await mediator.Send(query, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Extracts skills and details from job description text inline (GPT-4o call).
    /// </summary>
    [HttpGet("preview-skills")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(SkillGraph), StatusCodes.Status200OK)]
    public async Task<IActionResult> PreviewSkills(
        [FromQuery] string text,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(text))
            return BadRequest("Text query parameter is required.");

        var skillGraph = await skillExtractor.ExtractOnlyAsync("Draft Job", text, cancellationToken);
        return Ok(skillGraph);
    }

    /// <summary>
    /// Returns all active job openings.
    /// </summary>
    [HttpGet]
    [Authorize(Roles = $"{Roles.HrAdmin},{Roles.Recruiter},{Roles.Viewer}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetJobs(CancellationToken cancellationToken)
    {
        var jobs = await jobRepository.GetAllAsync(cancellationToken);
        return Ok(jobs.Select(j => new
        {
            id = j.Id,
            title = j.Title,
            description = j.Description,
            department = j.Department,
            isActive = j.IsActive,
            createdAt = j.CreatedAt
        }));
    }

    /// <summary>
    /// Creates a new job and job posting, triggering the skill extraction.
    /// </summary>
    [HttpPost]
    [Authorize(Roles = Roles.HrAdmin)]
    [ProducesResponseType(StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateJob(
        [FromBody] CreateJobRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Title) || string.IsNullOrWhiteSpace(request.Description))
            return BadRequest("Title and Description are required.");

        var jobId = Guid.NewGuid();

        var job = new Job(request.Title, request.Description, request.Department);
        job.SetId(jobId);

        var jobPosting = new JobPosting(request.Title, request.Description, request.Department);
        jobPosting.SetId(jobId);

        await jobRepository.AddAsync(job, cancellationToken);
        await jobPostingRepository.AddAsync(jobPosting, cancellationToken);

        await jobRepository.SaveChangesAsync(cancellationToken);
        await jobPostingRepository.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(nameof(GetJob), new { jobId = job.Id }, new
        {
            id = job.Id,
            title = job.Title,
            description = job.Description,
            department = job.Department,
            isActive = job.IsActive,
            createdAt = job.CreatedAt
        });
    }

    /// <summary>
    /// Gets job details including the AI-extracted SkillGraph if ready.
    /// </summary>
    [HttpGet("{jobId:guid}")]
    [Authorize(Roles = $"{Roles.HrAdmin},{Roles.Recruiter},{Roles.Viewer}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetJob(
        [FromRoute] Guid jobId,
        CancellationToken cancellationToken)
    {
        var job = await jobRepository.GetByIdAsync(jobId, cancellationToken);
        if (job is null)
            return NotFound();

        var jobPosting = await jobPostingRepository.GetByIdAsync(jobId, cancellationToken);

        return Ok(new
        {
            id = job.Id,
            title = job.Title,
            description = job.Description,
            department = job.Department,
            isActive = job.IsActive,
            createdAt = job.CreatedAt,
            skillGraph = jobPosting?.SkillGraph
        });
    }

    /// <summary>
    /// Imports/Seeds dummy jobs from an external jobs API or fallback local dataset.
    /// Restricted to HRAdmin.
    /// </summary>
    [HttpPost("import-dummies")]
    [Authorize(Roles = Roles.HrAdmin)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> ImportDummyJobs(CancellationToken cancellationToken)
    {
        var importedJobs = new List<Job>();
        var httpClient = httpClientFactory.CreateClient();
        httpClient.Timeout = TimeSpan.FromSeconds(10);
        
        List<RemotiveJob> fetchedJobs = new();

        try
        {
            // Set User-Agent as Remotive API requires/recommends it
            httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("RecruitAI-App/1.0");
            
            var response = await httpClient.GetFromJsonAsync<RemotiveApiResponse>(
                "https://remotive.com/api/remote-jobs?limit=5", 
                cancellationToken);

            if (response?.Jobs != null && response.Jobs.Count > 0)
            {
                fetchedJobs = response.Jobs;
            }
        }
        catch (Exception ex)
        {
            // Log it but swallow so we can fall back safely
            Console.WriteLine($"[Dummy Seeding] Failed to fetch remote jobs: {ex.Message}");
        }

        // If we didn't fetch any jobs (or failed), populate with high quality fallbacks
        if (fetchedJobs.Count == 0)
        {
            fetchedJobs = GetFallbackDummyJobs();
        }

        foreach (var rJob in fetchedJobs)
        {
            var jobId = Guid.NewGuid();
            var title = rJob.Title;
            
            // Clean description: Html Decode and strip HTML tags
            var decoded = System.Net.WebUtility.HtmlDecode(rJob.Description);
            var cleanDesc = System.Text.RegularExpressions.Regex.Replace(decoded, "<.*?>", " ").Trim();
            cleanDesc = System.Text.RegularExpressions.Regex.Replace(cleanDesc, @"\s+", " ");
            
            // Ensure description satisfies minimum validation (100+ characters)
            if (cleanDesc.Length < 100)
            {
                cleanDesc = cleanDesc.PadRight(100, '.');
            }

            var department = MapCategoryToDepartment(rJob.Category);

            var job = new Job(title, cleanDesc, department);
            job.SetId(jobId);

            var jobPosting = new JobPosting(title, cleanDesc, department);
            jobPosting.SetId(jobId);

            await jobRepository.AddAsync(job, cancellationToken);
            await jobPostingRepository.AddAsync(jobPosting, cancellationToken);

            importedJobs.Add(job);
        }

        await jobRepository.SaveChangesAsync(cancellationToken);
        await jobPostingRepository.SaveChangesAsync(cancellationToken);

        return Ok(importedJobs.Select(j => new
        {
            id = j.Id,
            title = j.Title,
            description = j.Description,
            department = j.Department,
            isActive = j.IsActive,
            createdAt = j.CreatedAt
        }));
    }

    private static List<RemotiveJob> GetFallbackDummyJobs()
    {
        return new List<RemotiveJob>
        {
            new RemotiveJob(
                "QA Automation Engineer (Selenium & Cypress)",
                "We are seeking a QA Automation Engineer to design, build, and maintain our automated testing frameworks. You will work closely with developers to identify testing requirements, write clean test scripts using Selenium and Cypress, and integrate tests into our CI/CD pipelines. The ideal candidate has 3+ years of experience in test automation, strong programming skills in JavaScript or Python, and a passion for software quality and bug prevention.",
                "qa",
                "Remote",
                "RecruitAI Labs"
            ),
            new RemotiveJob(
                "Data Scientist (AI & Machine Learning)",
                "We are looking for a Data Scientist to join our analytics and intelligence team. In this role, you will build predictive models, design recommendation algorithms, and analyze user behavior datasets. You should have 3+ years of experience with Python, SQL, and machine learning libraries like PyTorch or Scikit-Learn. Experience with big data technologies like Spark or Snowflake is a major plus. You will help turn complex data into actionable product features.",
                "data",
                "Remote",
                "RecruitAI Labs"
            ),
            new RemotiveJob(
                "Senior UX/UI Product Designer",
                "We are looking for a Senior UX/UI Product Designer to craft elegant, user-centric experiences for our recruitment platform. You will lead the design process from initial user research and wireframing to high-fidelity mockups and interactive prototypes. You should have a strong portfolio demonstrating visual design excellence, master-level skills in Figma, and experience collaborating with engineering teams. 4+ years of product design experience required.",
                "design",
                "Remote",
                "RecruitAI Labs"
            ),
            new RemotiveJob(
                "DevOps & Cloud Infrastructure Engineer",
                "We are looking for a DevOps Engineer to manage and scale our AWS cloud infrastructure. You will be responsible for automating deployment pipelines, configuring Kubernetes clusters, and monitoring system reliability and performance. Strong experience with Terraform, Docker, AWS, and CI/CD tools like GitHub Actions or GitLab is required. You will help ensure our platform is secure, resilient, and highly available.",
                "engineering",
                "Remote",
                "RecruitAI Labs"
            ),
            new RemotiveJob(
                "Technical Product Manager (AI Platform)",
                "We are looking for a Technical Product Manager to lead the roadmap for our AI-powered resume matching and ranking engine. You will write detailed product requirement documents (PRDs), collaborate with machine learning engineers and frontend developers, and translate user feedback into clear backlog items. 3+ years of product management experience in SaaS or AI products, and strong technical communication skills are required.",
                "product",
                "Remote",
                "RecruitAI Labs"
            )
        };
    }

    private static string MapCategoryToDepartment(string category)
    {
        if (string.IsNullOrEmpty(category)) return "Engineering";
        
        var lower = category.ToLowerInvariant();
        if (lower.Contains("software") || lower.Contains("developer") || lower.Contains("engineer") || lower.Contains("engineering") || lower.Contains("dev"))
            return "Engineering";
        if (lower.Contains("design") || lower.Contains("ux") || lower.Contains("ui") || lower.Contains("creative"))
            return "Design";
        if (lower.Contains("product"))
            return "Product";
        if (lower.Contains("data") || lower.Contains("science") || lower.Contains("analytics") || lower.Contains("analyst"))
            return "Data";
        if (lower.Contains("qa") || lower.Contains("test") || lower.Contains("quality") || lower.Contains("automation"))
            return "QA";
        
        return "Management";
    }
}

public record CreateJobRequest(
    string Title,
    string Description,
    string Department,
    string ExperienceLevel,
    string Location,
    bool IsRemote,
    DateTime? Deadline
);

public record RemotiveApiResponse(
    [property: JsonPropertyName("jobs")] List<RemotiveJob> Jobs
);

public record RemotiveJob(
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("category")] string Category,
    [property: JsonPropertyName("candidate_required_location")] string Location,
    [property: JsonPropertyName("company_name")] string CompanyName
);
