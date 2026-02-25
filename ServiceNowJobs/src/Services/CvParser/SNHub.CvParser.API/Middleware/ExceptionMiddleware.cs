using FluentValidation;
using SNHub.CvParser.Domain.Exceptions;
using System.Text.Json;

namespace SNHub.CvParser.API.Middleware;

public sealed class ExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionMiddleware> _logger;

    public ExceptionMiddleware(RequestDelegate next, ILogger<ExceptionMiddleware> logger)
    { _next = next; _logger = logger; }

    public async Task InvokeAsync(HttpContext ctx)
    {
        try { await _next(ctx); }
        catch (ValidationException ex)
        {
            var errors = ex.Errors
                .GroupBy(e => e.PropertyName)
                .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());
            await WriteJsonAsync(ctx, 400, new
            {
                type = "ValidationError",
                title = "One or more validation errors occurred.",
                status = 400,
                errors
            });
        }
        catch (ParseResultNotFoundException ex)        { await WriteError(ctx, 404, ex.Message); }
        catch (ParseResultAccessDeniedException ex)    { await WriteError(ctx, 403, ex.Message); }
        catch (ParseAlreadyAppliedException ex)        { await WriteError(ctx, 409, ex.Message); }
        catch (ParseNotCompletedException ex)          { await WriteError(ctx, 409, ex.Message); }
        catch (InvalidFileTypeException ex)            { await WriteError(ctx, 415, ex.Message); }
        catch (FileTooLargeException ex)               { await WriteError(ctx, 413, ex.Message); }
        catch (DomainException ex)                     { await WriteError(ctx, 400, ex.Message); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled: {Method} {Path}", ctx.Request.Method, ctx.Request.Path);
            await WriteError(ctx, 500, "An unexpected error occurred.");
        }
    }

    private static Task WriteError(HttpContext ctx, int status, string message) =>
        WriteJsonAsync(ctx, status, new { type = "Error", title = message, status });

    private static Task WriteJsonAsync(HttpContext ctx, int status, object body)
    {
        ctx.Response.StatusCode  = status;
        ctx.Response.ContentType = "application/json";
        return ctx.Response.WriteAsync(
            JsonSerializer.Serialize(body,
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
    }
}
