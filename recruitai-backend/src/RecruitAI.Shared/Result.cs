namespace RecruitAI.Shared;

/// <summary>
/// Represents the result of an operation — either success with a value or failure with error details.
/// Eliminates the need for exception-based flow control in application logic.
/// </summary>
public class Result<T>
{
    protected Result(T? value, bool isSuccess, string? error, string? errorCode, int statusCode)
    {
        Value = value;
        IsSuccess = isSuccess;
        Error = error;
        ErrorCode = errorCode;
        StatusCode = statusCode;
    }

    public T? Value { get; }
    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public string? Error { get; }
    public string? ErrorCode { get; }
    public int StatusCode { get; }

    public static Result<T> Success(T value) =>
        new(value, true, null, null, 200);

    public static Result<T> Created(T value) =>
        new(value, true, null, null, 201);

    public static Result<T> Failure(string error, string errorCode = "GENERAL_ERROR", int statusCode = 400) =>
        new(default, false, error, errorCode, statusCode);

    public static Result<T> NotFound(string error) =>
        new(default, false, error, "NOT_FOUND", 404);

    public static Result<T> Unauthorized(string error = "Unauthorized") =>
        new(default, false, error, "UNAUTHORIZED", 401);

    public static Result<T> Forbidden(string error = "Forbidden") =>
        new(default, false, error, "FORBIDDEN", 403);

    public TOut Match<TOut>(Func<T, TOut> onSuccess, Func<string, TOut> onFailure) =>
        IsSuccess ? onSuccess(Value!) : onFailure(Error!);
}

/// <summary>Non-generic result for commands with no return value.</summary>
public class Result : Result<object>
{
    private Result(bool isSuccess, string? error, string? errorCode, int statusCode)
        : base(null, isSuccess, error, errorCode, statusCode) { }

    public static Result Ok() => new(true, null, null, 200);
    public new static Result Failure(string error, string errorCode = "GENERAL_ERROR", int statusCode = 400) =>
        new(false, error, errorCode, statusCode);
    public new static Result NotFound(string error) => new(false, error, "NOT_FOUND", 404);
}
