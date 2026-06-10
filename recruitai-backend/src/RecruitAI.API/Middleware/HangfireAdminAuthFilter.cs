using Hangfire.Dashboard;
using Microsoft.AspNetCore.Authorization;

namespace RecruitAI.API.Middleware;

/// <summary>
/// Hangfire dashboard auth filter — only allows users with HRAdmin role.
/// Reads the authenticated user from the HttpContext set up by JWT middleware.
/// </summary>
public sealed class HangfireAdminAuthFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context)
    {
        var httpContext = context.GetHttpContext();

        if (!httpContext.User.Identity?.IsAuthenticated ?? true)
            return false;

        return httpContext.User.IsInRole("HRAdmin");
    }
}
