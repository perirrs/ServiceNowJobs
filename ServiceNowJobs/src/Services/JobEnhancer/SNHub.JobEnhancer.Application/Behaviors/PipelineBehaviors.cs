using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;

namespace SNHub.JobEnhancer.Application.Behaviors;

public sealed class ValidationBehavior<TRequest, TResponse>
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly IEnumerable<IValidator<TRequest>> _validators;
    public ValidationBehavior(IEnumerable<IValidator<TRequest>> v) => _validators = v;

    public async Task<TResponse> Handle(
        TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        if (!_validators.Any()) return await next();
        var ctx      = new ValidationContext<TRequest>(request);
        var results  = await Task.WhenAll(_validators.Select(v => v.ValidateAsync(ctx, ct)));
        var failures = results.SelectMany(r => r.Errors).Where(f => f != null).ToList();
        if (failures.Count != 0) throw new ValidationException(failures);
        return await next();
    }
}

public sealed class LoggingBehavior<TRequest, TResponse>
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly ILogger<LoggingBehavior<TRequest, TResponse>> _logger;
    public LoggingBehavior(ILogger<LoggingBehavior<TRequest, TResponse>> logger)
        => _logger = logger;

    public async Task<TResponse> Handle(
        TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        var name = typeof(TRequest).Name;
        var sw   = System.Diagnostics.Stopwatch.StartNew();
        _logger.LogInformation("Executing {Request}", name);
        try
        {
            var result = await next();
            sw.Stop();
            if (sw.ElapsedMilliseconds > 20_000) // GPT-4o can be slow
                _logger.LogWarning("{Request} took {Ms}ms", name, sw.ElapsedMilliseconds);
            else
                _logger.LogInformation("Completed {Request} in {Ms}ms", name, sw.ElapsedMilliseconds);
            return result;
        }
        catch (Exception ex) { _logger.LogError(ex, "Error in {Request}", name); throw; }
    }
}
