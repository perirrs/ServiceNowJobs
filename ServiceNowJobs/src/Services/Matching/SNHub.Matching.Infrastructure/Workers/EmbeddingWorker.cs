using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using SNHub.Matching.Application.Commands.ProcessEmbedding;
using SNHub.Matching.Application.Interfaces;
using SNHub.Matching.Domain.Enums;

namespace SNHub.Matching.Infrastructure.Workers;

/// <summary>
/// Polls the embedding_records table every N seconds for Pending records
/// and processes them in batches. Decouples embedding generation from
/// the write path — profile/job updates complete immediately, indexing
/// happens asynchronously in the background.
/// </summary>
public sealed class EmbeddingWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<EmbeddingWorker> _logger;
    private readonly TimeSpan _interval;
    private readonly int _batchSize;

    public EmbeddingWorker(
        IServiceScopeFactory scopes,
        IConfiguration config,
        ILogger<EmbeddingWorker> logger)
    {
        _scopes    = scopes;
        _logger    = logger;
        _interval  = TimeSpan.FromSeconds(
            config.GetValue<int>("EmbeddingWorker:IntervalSeconds", 15));
        _batchSize = config.GetValue<int>("EmbeddingWorker:BatchSize", 10);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "EmbeddingWorker started — polling every {Interval}s, batch size {Batch}",
            _interval.TotalSeconds, _batchSize);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessBatchAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "EmbeddingWorker batch failed");
            }

            await Task.Delay(_interval, stoppingToken);
        }

        _logger.LogInformation("EmbeddingWorker stopped.");
    }

    private async Task ProcessBatchAsync(CancellationToken ct)
    {
        await using var scope    = _scopes.CreateAsyncScope();
        var repo     = scope.ServiceProvider.GetRequiredService<IEmbeddingRecordRepository>();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        var pending = (await repo.GetPendingAsync(_batchSize, ct)).ToList();

        if (pending.Count == 0) return;

        _logger.LogInformation(
            "EmbeddingWorker processing {Count} pending record(s)", pending.Count);

        var tasks = pending.Select(record =>
            mediator.Send(new ProcessEmbeddingCommand(
                record.DocumentId, record.DocumentType), ct));

        var results = await Task.WhenAll(tasks);
        var succeeded = results.Count(r => r);
        var failed    = results.Length - succeeded;

        _logger.LogInformation(
            "EmbeddingWorker batch complete — {Succeeded} indexed, {Failed} failed",
            succeeded, failed);
    }
}
