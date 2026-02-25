using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;

namespace SNHub.Applications.Application.Behaviors;

public sealed class ValidationBehavior<TRequest, TResponse>
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly IEnumerable<IValidator<TRequest>> _validators;
    public ValidationBehavior(IEnumerable<IValidator<TRequest>> validators) => _validators = validators;

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        if (!_validators.Any()) return await next();
        var context  = new ValidationContext<TRequest>(request);
        var results  = await Task.WhenAll(_validators.Select(v => v.ValidateAsync(context, ct)));
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
    public LoggingBehavior(ILogger<LoggingBehavior<TRequest, TResponse>> logger) => _logger = logger;

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        var name  = typeof(TRequest).Name;
        var start = DateTimeOffset.UtcNow;
        _logger.LogInformation("Executing {Request}", name);
        try
        {
            var response = await next();
            var ms = (DateTimeOffset.UtcNow - start).TotalMilliseconds;
            if (ms > 500) _logger.LogWarning("Slow: {Request} took {Ms}ms", name, ms);
            else          _logger.LogInformation("Completed {Request} in {Ms}ms", name, ms);
            return response;
        }
        catch (Exception ex) { _logger.LogError(ex, "Error in {Request}", name); throw; }
    }
}
