using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using SNHub.Jobs.Application.Interfaces;
using SNHub.Jobs.Domain.Exceptions;

namespace SNHub.Jobs.Application.Commands.CloseJob;

public sealed record CloseJobCommand(Guid JobId, Guid RequesterId) : IRequest<Unit>;

public sealed class CloseJobCommandValidator : AbstractValidator<CloseJobCommand>
{
    public CloseJobCommandValidator()
    {
        RuleFor(x => x.JobId).NotEmpty();
        RuleFor(x => x.RequesterId).NotEmpty();
    }
}

public sealed class CloseJobCommandHandler : IRequestHandler<CloseJobCommand, Unit>
{
    private readonly IJobRepository _repo;
    private readonly IUnitOfWork _uow;
    private readonly ILogger<CloseJobCommandHandler> _logger;

    public CloseJobCommandHandler(IJobRepository repo, IUnitOfWork uow, ILogger<CloseJobCommandHandler> logger)
    {
        _repo = repo;
        _uow = uow;
        _logger = logger;
    }

    public async Task<Unit> Handle(CloseJobCommand req, CancellationToken ct)
    {
        var job = await _repo.GetByIdAsync(req.JobId, ct)
            ?? throw new JobNotFoundException(req.JobId);

        if (job.EmployerId != req.RequesterId)
            throw new JobAccessDeniedException();

        job.Close();
        await _repo.UpdateAsync(job, ct);
        await _uow.SaveChangesAsync(ct);

        _logger.LogInformation("Job {JobId} closed by {RequesterId}", req.JobId, req.RequesterId);
        return Unit.Value;
    }
}
