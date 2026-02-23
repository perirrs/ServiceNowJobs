using MediatR;
using SNHub.Jobs.Application.Interfaces;

namespace SNHub.Jobs.Application.Commands.CloseJob;

public sealed record CloseJobCommand(Guid JobId, Guid RequesterId) : IRequest<Unit>;

public sealed class CloseJobCommandHandler : IRequestHandler<CloseJobCommand, Unit>
{
    private readonly IJobRepository _repo;
    private readonly IUnitOfWork _uow;
    public CloseJobCommandHandler(IJobRepository repo, IUnitOfWork uow) { _repo = repo; _uow = uow; }

    public async Task<Unit> Handle(CloseJobCommand req, CancellationToken ct)
    {
        var job = await _repo.GetByIdAsync(req.JobId, ct)
            ?? throw new KeyNotFoundException($"Job {req.JobId} not found.");
        if (job.EmployerId != req.RequesterId) throw new UnauthorizedAccessException();
        job.Close();
        await _repo.UpdateAsync(job, ct);
        await _uow.SaveChangesAsync(ct);
        return Unit.Value;
    }
}
