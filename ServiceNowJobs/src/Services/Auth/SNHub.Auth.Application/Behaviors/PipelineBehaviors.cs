using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;

namespace SNHub.Auth.Application.Behaviors;

/// <summary>Runs FluentValidation before every command handler executes.</summary>
public sealed class ValidationBehavior<TRequest, TResponse>
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly IEnumerable<IValidator<TRequest>> _validators;

    public ValidationBehavior(IEnumerable<IValidator<TRequest>> validators)
        => _validators = validators;

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (!_validators.Any())
            return await next();

        var context = new ValidationContext<TRequest>(request);

        var results = await Task.WhenAll(
            _validators.Select(v => v.ValidateAsync(context, cancellationToken)));

        var failures = results
            .SelectMany(r => r.Errors)
            .Where(f => f != null)
            .ToList();

        if (failures.Count != 0)
            throw new ValidationException(failures);

        return await next();
    }
}

/// <summary>Logs every command/query with execution time. Flags slow operations.</summary>
public sealed class LoggingBehavior<TRequest, TResponse>
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly ILogger<LoggingBehavior<TRequest, TResponse>> _logger;

    public LoggingBehavior(ILogger<LoggingBehavior<TRequest, TResponse>> logger)
        => _logger = logger;

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var name = typeof(TRequest).Name;
        var start = DateTimeOffset.UtcNow;

        _logger.LogInformation("Executing {RequestName}", name);

        try
        {
            var response = await next();
            var ms = (DateTimeOffset.UtcNow - start).TotalMilliseconds;

            if (ms > 500)
                _logger.LogWarning("Slow request: {RequestName} took {Ms}ms", name, ms);
            else
                _logger.LogInformation("Completed {RequestName} in {Ms}ms", name, ms);

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in {RequestName}", name);
            throw;
        }
    }
}
