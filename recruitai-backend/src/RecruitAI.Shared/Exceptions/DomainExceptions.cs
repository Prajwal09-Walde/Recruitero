namespace RecruitAI.Shared.Exceptions;

/// <summary>Thrown when a requested resource is not found.</summary>
public class NotFoundException : Exception
{
    public string ResourceName { get; }
    public object ResourceKey { get; }

    public NotFoundException(string resourceName, object resourceKey)
        : base($"Resource '{resourceName}' with key '{resourceKey}' was not found.")
    {
        ResourceName = resourceName;
        ResourceKey = resourceKey;
    }
}

/// <summary>Thrown when a FluentValidation pipeline check fails.</summary>
public class ValidationException : Exception
{
    public IDictionary<string, string[]> Errors { get; }

    public ValidationException(IDictionary<string, string[]> errors)
        : base("One or more validation errors occurred.")
    {
        Errors = errors;
    }
}

/// <summary>Thrown when the caller lacks permission for the requested operation.</summary>
public class ForbiddenException : Exception
{
    public ForbiddenException(string message = "You do not have permission to perform this action.")
        : base(message) { }
}

/// <summary>Thrown when a business rule is violated.</summary>
public class DomainException : Exception
{
    public string Code { get; }
    public DomainException(string message, string code = "DOMAIN_ERROR")
        : base(message)
    {
        Code = code;
    }
}

/// <summary>Thrown when an external dependency (S3, OpenAI, etc.) fails.</summary>
public class ExternalServiceException : Exception
{
    public string ServiceName { get; }
    public ExternalServiceException(string serviceName, string message, Exception? inner = null)
        : base($"[{serviceName}] {message}", inner)
    {
        ServiceName = serviceName;
    }
}
