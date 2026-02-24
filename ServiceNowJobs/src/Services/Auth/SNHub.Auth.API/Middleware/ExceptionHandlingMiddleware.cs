using FluentValidation;
using SNHub.Auth.Domain.Exceptions;
using SNHub.Shared.Models;
using System.Net;
using System.Text.Json;

namespace SNHub.Auth.API.Middleware;

/// <summary>
/// Catches all unhandled exceptions and returns consistent JSON error responses.
/// Never leaks stack traces to clients in production.
/// </summary>
public sealed class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;
    private readonly IHostEnvironment _env;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public ExceptionHandlingMiddleware(
        RequestDelegate next,
        ILogger<ExceptionHandlingMiddleware> logger,
        IHostEnvironment env)
    {
        _next = next;
        _logger = logger;
        _env = env;
    }

    public async Task InvokeAsync(HttpContext ctx)
    {
        try
        {
            await _next(ctx);
        }
        catch (Exception ex)
        {
            await HandleAsync(ctx, ex);
        }
    }

    private async Task HandleAsync(HttpContext ctx, Exception ex)
    {
        var (status, code, message, errors) = Map(ex);

        if (status >= 500)
            _logger.LogError(ex, "Unhandled exception [{Code}]", code);
        else
            _logger.LogWarning("Client error [{Code}]: {Message}", code, ex.Message);

        var response = new ApiErrorResponse(
            TraceId: ctx.TraceIdentifier,
            StatusCode: status,
            ErrorCode: code,
            Message: message,
            Errors: errors,
            Detail: (_env.IsDevelopment() || _env.IsEnvironment("Testing")) ? ex.ToString() : null);

        ctx.Response.StatusCode = status;
        ctx.Response.ContentType = "application/json";
        await ctx.Response.WriteAsync(JsonSerializer.Serialize(response, JsonOptions));
    }

    private static (int Status, string Code, string Message, IEnumerable<string>? Errors) Map(Exception ex)
        => ex switch
        {
            ValidationException ve => (400, "VALIDATION_FAILED",
                "One or more validation errors occurred.",
                ve.Errors.Select(e => e.ErrorMessage)),

            UserAlreadyExistsException => (409, "USER_ALREADY_EXISTS", ex.Message, null),
            UserNotFoundException      => (404, "USER_NOT_FOUND",      ex.Message, null),
            InvalidCredentialsException => (401, "INVALID_CREDENTIALS", ex.Message, null),
            AccountLockedException     => (423, "ACCOUNT_LOCKED",      ex.Message, null),
            AccountSuspendedException  => (423, "ACCOUNT_SUSPENDED",   ex.Message, null),
            EmailNotVerifiedException  => (403, "EMAIL_NOT_VERIFIED",  ex.Message, null),
            InvalidTokenException      => (400, "INVALID_TOKEN",       ex.Message, null),
            DomainException            => (400, "DOMAIN_ERROR",        ex.Message, null),
            UnauthorizedAccessException => (401, "UNAUTHORIZED", "Authentication required.", null),
            OperationCanceledException  => (499, "CANCELLED", "Request was cancelled.", null),
            _ => (500, "INTERNAL_ERROR", "An unexpected error occurred.", null)
        };
}
