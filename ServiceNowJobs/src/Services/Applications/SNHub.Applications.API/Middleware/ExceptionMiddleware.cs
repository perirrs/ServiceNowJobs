using SNHub.Applications.Domain.Exceptions;
using System.Net;
using System.Text.Json;

namespace SNHub.Applications.API.Middleware;

public sealed class ExceptionMiddleware(RequestDelegate next, ILogger<ExceptionMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext ctx)
    {
        try { await next(ctx); }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled exception");
            var (status, code) = ex switch
            {
                ApplicationNotFoundException => (HttpStatusCode.NotFound, "NOT_FOUND"),
                DuplicateApplicationException => (HttpStatusCode.Conflict, "DUPLICATE_APPLICATION"),
                DomainException => (HttpStatusCode.BadRequest, "DOMAIN_ERROR"),
                _ => (HttpStatusCode.InternalServerError, "INTERNAL_ERROR")
            };
            ctx.Response.StatusCode = (int)status;
            ctx.Response.ContentType = "application/json";
            await ctx.Response.WriteAsync(JsonSerializer.Serialize(new { code, message = ex.Message }));
        }
    }
}
