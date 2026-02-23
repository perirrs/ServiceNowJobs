using System.Net;
using System.Text.Json;
namespace SNHub.Jobs.API.Middleware;
public sealed class ExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionMiddleware> _logger;
    public ExceptionMiddleware(RequestDelegate next, ILogger<ExceptionMiddleware> logger) { _next = next; _logger = logger; }
    public async Task InvokeAsync(HttpContext ctx)
    {
        try { await _next(ctx); }
        catch (KeyNotFoundException ex) { await WriteError(ctx, 404, ex.Message); }
        catch (UnauthorizedAccessException ex) { await WriteError(ctx, 403, ex.Message); }
        catch (Exception ex) { _logger.LogError(ex, "Unhandled exception"); await WriteError(ctx, 500, "An unexpected error occurred."); }
    }
    private static Task WriteError(HttpContext ctx, int status, string msg)
    {
        ctx.Response.StatusCode = status;
        ctx.Response.ContentType = "application/json";
        return ctx.Response.WriteAsync(JsonSerializer.Serialize(new { error = msg }));
    }
}
