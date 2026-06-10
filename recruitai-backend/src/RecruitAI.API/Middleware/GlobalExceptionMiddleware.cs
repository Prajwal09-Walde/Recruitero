using Microsoft.AspNetCore.Mvc;
using RecruitAI.Shared.Exceptions;
using System.Text.Json;
using ValidationException = RecruitAI.Shared.Exceptions.ValidationException;

namespace RecruitAI.API.Middleware;

/// <summary>
/// Global exception handling middleware — converts all exceptions to RFC 7807 ProblemDetails.
/// Must be registered BEFORE routing so it catches all unhandled exceptions.
/// </summary>
public sealed class GlobalExceptionMiddleware(
    RequestDelegate next,
    ILogger<GlobalExceptionMiddleware> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        var (statusCode, title, detail, extensions) = exception switch
        {
            ValidationException ve => (
                StatusCodes.Status422UnprocessableEntity,
                "Validation Failed",
                "One or more validation errors occurred.",
                (object?)new { errors = ve.Errors }
            ),
            NotFoundException nfe => (
                StatusCodes.Status404NotFound,
                "Resource Not Found",
                nfe.Message,
                (object?)null
            ),
            ForbiddenException fe => (
                StatusCodes.Status403Forbidden,
                "Forbidden",
                fe.Message,
                (object?)null
            ),
            DomainException de => (
                StatusCodes.Status400BadRequest,
                "Domain Error",
                de.Message,
                (object?)new { code = de.Code }
            ),
            ExternalServiceException ese => (
                StatusCodes.Status502BadGateway,
                "External Service Error",
                ese.Message,
                (object?)new { service = ese.ServiceName }
            ),
            _ => (
                StatusCodes.Status500InternalServerError,
                "Internal Server Error",
                "An unexpected error occurred. Please try again later.",
                (object?)null
            )
        };

        if (statusCode >= 500)
            logger.LogError(exception, "Unhandled exception: {Message}", exception.Message);
        else
            logger.LogWarning("Handled exception {Type}: {Message}", exception.GetType().Name, exception.Message);

        var problem = new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Detail = detail,
            Instance = context.Request.Path
        };

        if (extensions is not null)
            foreach (var prop in extensions.GetType().GetProperties())
                problem.Extensions[prop.Name] = prop.GetValue(extensions);

        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/problem+json";

        await context.Response.WriteAsync(JsonSerializer.Serialize(problem, JsonOptions));
    }
}
