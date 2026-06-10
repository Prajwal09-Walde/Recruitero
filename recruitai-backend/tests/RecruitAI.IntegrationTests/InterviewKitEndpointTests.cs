using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using RecruitAI.Application.Features.InterviewKit.Queries;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Xunit;

namespace RecruitAI.IntegrationTests;

/// <summary>Integration tests for GET + POST /api/applications/{id}/interview-kit</summary>
public class InterviewKitEndpointTests(RecruitAIWebAppFactory factory)
    : IClassFixture<RecruitAIWebAppFactory>
{
    private readonly HttpClient _client = factory.CreateClient(
        new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private void SetBearerToken(string role = "HRAdmin")
    {
        var token = TestJwtGenerator.Generate(role);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    [Fact]
    public async Task GetInterviewKit_WithScoredApplication_Returns200WithQuestions()
    {
        // Arrange
        SetBearerToken("HRAdmin");
        var appId = RecruitAIWebAppFactory.TestApplicationId;

        // Act
        var response = await _client.GetAsync($"/api/applications/{appId}/interview-kit");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<InterviewKitResult>();
        result.Should().NotBeNull();
        result!.CandidateName.Should().Be("Jane Doe");
        result.FitScore.Should().Be(92.5m);
        result.Questions.Should().HaveCountGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task GetInterviewKit_WhenKitNotGenerated_Returns404WithRetryAfterHeader()
    {
        // Arrange
        SetBearerToken("HRAdmin");
        var nonExistentAppId = Guid.NewGuid();

        // Act
        var response = await _client.GetAsync($"/api/applications/{nonExistentAppId}/interview-kit");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Headers.Should().ContainKey("Retry-After");
    }

    [Fact]
    public async Task RegenerateInterviewKit_WithHrAdminRole_Returns202()
    {
        SetBearerToken("HRAdmin");
        var appId = RecruitAIWebAppFactory.TestApplicationId;

        var response = await _client.PostAsync(
            $"/api/applications/{appId}/interview-kit/regenerate", null);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
    }

    [Fact]
    public async Task RegenerateInterviewKit_WithRecruiterRole_Returns202()
    {
        // Both HrAdmin and Recruiter can regenerate
        SetBearerToken("Recruiter");
        var appId = RecruitAIWebAppFactory.TestApplicationId;

        var response = await _client.PostAsync(
            $"/api/applications/{appId}/interview-kit/regenerate", null);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
    }

    [Fact]
    public async Task GetInterviewKit_WithViewerRole_Returns403()
    {
        // Viewers cannot access interview kits
        SetBearerToken("Viewer");
        var response = await _client.GetAsync(
            $"/api/applications/{RecruitAIWebAppFactory.TestApplicationId}/interview-kit");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
