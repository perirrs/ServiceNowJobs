using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using SNHub.Matching.Application.DTOs;
using SNHub.Matching.Application.Interfaces;
using SNHub.Matching.Domain.Entities;
using SNHub.Matching.Domain.Enums;

namespace SNHub.Matching.Application.Commands.RequestEmbedding;

/// <summary>
/// Creates or resets an EmbeddingRecord so the background worker will
/// (re)generate and index the embedding for this document.
/// Called whenever a job or candidate profile is created or updated.
/// </summary>
public sealed record RequestEmbeddingCommand(
    Guid         DocumentId,
    DocumentType DocumentType) : IRequest<EmbeddingStatusDto>;

public sealed class RequestEmbeddingCommandValidator
    : AbstractValidator<RequestEmbeddingCommand>
{
    public RequestEmbeddingCommandValidator()
    {
        RuleFor(x => x.DocumentId).NotEmpty();
        RuleFor(x => x.DocumentType).IsInEnum();
    }
}

public sealed class RequestEmbeddingCommandHandler
    : IRequestHandler<RequestEmbeddingCommand, EmbeddingStatusDto>
{
    private readonly IEmbeddingRecordRepository _repo;
    private readonly IUnitOfWork                _uow;
    private readonly ILogger<RequestEmbeddingCommandHandler> _logger;

    public RequestEmbeddingCommandHandler(
        IEmbeddingRecordRepository repo, IUnitOfWork uow,
        ILogger<RequestEmbeddingCommandHandler> logger)
    { _repo = repo; _uow = uow; _logger = logger; }

    public async Task<EmbeddingStatusDto> Handle(
        RequestEmbeddingCommand req, CancellationToken ct)
    {
        var existing = await _repo.GetByDocumentIdAsync(req.DocumentId, req.DocumentType, ct);

        if (existing is null)
        {
            existing = EmbeddingRecord.Create(req.DocumentId, req.DocumentType);
            await _repo.AddAsync(existing, ct);
        }
        else
        {
            // Reset to pending so worker re-indexes
            existing.ResetToPending();
        }

        await _uow.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Embedding requested for {Type} {DocumentId}",
            req.DocumentType, req.DocumentId);

        return new EmbeddingStatusDto(
            existing.DocumentId, existing.DocumentType.ToString(),
            existing.Status.ToString(), existing.LastIndexedAt, existing.RetryCount);
    }
}
