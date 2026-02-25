using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using SNHub.JobEnhancer.Application.DTOs;
using SNHub.JobEnhancer.Application.Interfaces;
using SNHub.JobEnhancer.Application.Mappers;
using SNHub.JobEnhancer.Domain.Exceptions;

namespace SNHub.JobEnhancer.Application.Commands.AcceptEnhancement;

public sealed record AcceptEnhancementCommand(
    Guid EnhancementId,
    Guid RequesterId) : IRequest<AcceptEnhancementResponse>;

public sealed class AcceptEnhancementCommandValidator
    : AbstractValidator<AcceptEnhancementCommand>
{
    public AcceptEnhancementCommandValidator()
    {
        RuleFor(x => x.EnhancementId).NotEmpty();
        RuleFor(x => x.RequesterId).NotEmpty();
    }
}

public sealed class AcceptEnhancementCommandHandler
    : IRequestHandler<AcceptEnhancementCommand, AcceptEnhancementResponse>
{
    private readonly IEnhancementResultRepository _repo;
    private readonly IUnitOfWork                  _uow;
    private readonly IJobsServiceClient           _jobs;
    private readonly ILogger<AcceptEnhancementCommandHandler> _logger;

    public AcceptEnhancementCommandHandler(
        IEnhancementResultRepository repo, IUnitOfWork uow,
        IJobsServiceClient jobs,
        ILogger<AcceptEnhancementCommandHandler> logger)
    { _repo = repo; _uow = uow; _jobs = jobs; _logger = logger; }

    public async Task<AcceptEnhancementResponse> Handle(
        AcceptEnhancementCommand req, CancellationToken ct)
    {
        var record = await _repo.GetByIdAsync(req.EnhancementId, ct)
            ?? throw new EnhancementNotFoundException(req.EnhancementId);

        if (record.RequestedBy != req.RequesterId)
            throw new EnhancementAccessDeniedException();

        if (record.Status != Domain.Enums.EnhancementStatus.Completed)
            throw new EnhancementNotCompletedException();

        if (record.IsAccepted)
            throw new EnhancementAlreadyAcceptedException();

        record.Accept();
        await _uow.SaveChangesAsync(ct);

        // Notify Jobs service to apply the enhanced content
        var dto      = EnhancementResultMapper.ToDto(record);
        var skills   = dto.SuggestedSkills;

        await _jobs.ApplyEnhancementAsync(
            record.JobId,
            record.EnhancedTitle,
            record.EnhancedDescription,
            record.EnhancedRequirements,
            skills,
            ct);

        _logger.LogInformation(
            "Enhancement {Id} accepted for job {JobId}", req.EnhancementId, record.JobId);

        return new AcceptEnhancementResponse(
            record.Id, record.JobId, true,
            $"Enhancement accepted. Job updated with AI-improved content. Score: {record.ScoreBefore}→{record.ScoreAfter}.");
    }
}
