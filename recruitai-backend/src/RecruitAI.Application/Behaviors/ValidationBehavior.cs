using FluentValidation;
using MediatR;
using RecruitAI.Shared.Exceptions;
using ValidationException = RecruitAI.Shared.Exceptions.ValidationException;

namespace RecruitAI.Application.Behaviors;

/// <summary>
/// MediatR pipeline behavior that runs all registered FluentValidation validators
/// before the handler executes. If any validation fails, a ValidationException is thrown,
/// which is then caught by the global exception middleware and mapped to RFC 7807 ProblemDetails.
/// </summary>
public sealed class ValidationBehavior<TRequest, TResponse>(
    IEnumerable<IValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (!validators.Any())
            return await next();

        var context = new ValidationContext<TRequest>(request);
        var failures = validators
            .Select(v => v.Validate(context))
            .SelectMany(r => r.Errors)
            .Where(f => f is not null)
            .ToList();

        if (failures.Count != 0)
        {
            var errors = failures
                .GroupBy(f => f.PropertyName)
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(e => e.ErrorMessage).ToArray());

            throw new ValidationException(errors);
        }

        return await next();
    }
}
