using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using SNHub.Jobs.Application.DTOs;
using SNHub.Jobs.Application.Interfaces;
using SNHub.Jobs.Domain.Enums;
using SNHub.Jobs.Domain.Exceptions;

namespace SNHub.Jobs.Application.Commands.PauseJob;

public sealed record PauseJobCommand(Guid JobId, Guid RequesterId) : IRequest<JobDto>;

public sealed class PauseJobCommandValidator : AbstractValidator<PauseJobCommand>
{
    public PauseJobCommandValidator()
    {
        RuleFor(x => x.JobId).NotEmpty();
        RuleFor(x => x.RequesterId).NotEmpty();
    }
}

public sealed class PauseJobCommandHandler : IRequestHandler<PauseJobCommand, JobDto>
{
    private readonly IJobRepository _repo;
    private readonly IUnitOfWork _uow;
    private readonly ILogger<PauseJobCommandHandler> _logger;

    public PauseJobCommandHandler(IJobRepository repo, IUnitOfWork uow, ILogger<PauseJobCommandHandler> logger)
    {
        _repo = repo;
        _uow = uow;
        _logger = logger;
    }

    public async Task<JobDto> Handle(PauseJobCommand req, CancellationToken ct)
    {
        var job = await _repo.GetByIdAsync(req.JobId, ct)
            ?? throw new JobNotFoundException(req.JobId);

        if (job.EmployerId != req.RequesterId)
            throw new JobAccessDeniedException();

        if (job.Status != JobStatus.Active)
            throw new DomainException("Only active jobs can be paused.");

        job.Pause();
        await _repo.UpdateAsync(job, ct);
        await _uow.SaveChangesAsync(ct);

        _logger.LogInformation("Job {JobId} paused by {RequesterId}", req.JobId, req.RequesterId);
        return JobMapper.ToDto(job);
    }
}
