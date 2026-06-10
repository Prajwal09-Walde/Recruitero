using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using RecruitAI.Application.Features.Jobs.Queries;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Xunit;

namespace RecruitAI.IntegrationTests;

/// <summary>Integration tests for GET /api/jobs/{jobId}/leaderboard</summary>
public class LeaderboardEndpointTests(RecruitAIWebAppFactory factory)
    : IClassFixture<RecruitAIWebAppFactory>
{
    private readonly HttpClient _client = factory.CreateClient(
        new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private void SetBearerToken(string role = "HRAdmin")
    {
        // In real tests: generate a valid JWT using the same secret as appsettings.Testing.json
        // Here we use a test-helper token generator (stub).
        var token = TestJwtGenerator.Generate(role);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    [Fact]
    public async Task GetLeaderboard_WithValidJobId_Returns200WithCandidates()
    {
        // Arrange
        SetBearerToken("HRAdmin");
        var jobId = RecruitAIWebAppFactory.TestJobId;

        // Act
        var response = await _client.GetAsync($"/api/jobs/{jobId}/leaderboard");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<LeaderboardResult>();
        result.Should().NotBeNull();
        result!.JobId.Should().Be(jobId);
        result.Candidates.Should().HaveCountGreaterThanOrEqualTo(1);
        result.Candidates[0].FitScore.Should().Be(92.5m);
        result.Candidates[0].Rank.Should().Be(1);
    }

    [Fact]
    public async Task GetLeaderboard_WithNonExistentJob_Returns404()
    {
        // Arrange
        SetBearerToken("HRAdmin");
        var nonExistentJobId = Guid.NewGuid();

        // Act
        var response = await _client.GetAsync($"/api/jobs/{nonExistentJobId}/leaderboard");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetLeaderboard_WithoutAuth_Returns401()
    {
        // Arrange — no token set
        _client.DefaultRequestHeaders.Authorization = null;
        var jobId = RecruitAIWebAppFactory.TestJobId;

        // Act
        var response = await _client.GetAsync($"/api/jobs/{jobId}/leaderboard");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetLeaderboard_WithViewerRole_Returns200()
    {
        SetBearerToken("Viewer");
        var response = await _client.GetAsync($"/api/jobs/{RecruitAIWebAppFactory.TestJobId}/leaderboard");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetLeaderboard_Pagination_ReturnsCorrectPage()
    {
        SetBearerToken("HRAdmin");
        var jobId = RecruitAIWebAppFactory.TestJobId;

        var response = await _client.GetAsync($"/api/jobs/{jobId}/leaderboard?page=1&pageSize=5");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<LeaderboardResult>();
        result!.Candidates.Should().HaveCountLessThanOrEqualTo(5);
    }
}
