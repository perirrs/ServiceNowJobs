namespace SNHub.Shared.Models;

public sealed record ApiResponse<T>(
    bool Success,
    T? Data,
    string? Message,
    string? TraceId = null)
{
    public static ApiResponse<T> Ok(T data, string? message = null)
        => new(true, data, message);
}

public sealed record ApiResponse(bool Success, string? Message)
{
    public static ApiResponse Ok(string? message = null)
        => new(true, message);
}

public sealed record ApiErrorResponse(
    string TraceId,
    int StatusCode,
    string ErrorCode,
    string Message,
    IEnumerable<string>? Errors = null,
    string? Detail = null)
{
    public bool Success => false;
}
