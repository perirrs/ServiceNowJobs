using MediatR;
using SNHub.Jobs.Application.DTOs;
using SNHub.Jobs.Application.Interfaces;

namespace SNHub.Jobs.Application.Queries.GetJob;

public sealed record GetJobQuery(Guid JobId) : IRequest<JobDto?>;

public sealed class GetJobQueryHandler : IRequestHandler<GetJobQuery, JobDto?>
{
    private readonly IJobRepository _repo;
    private readonly IUnitOfWork _uow;
    public GetJobQueryHandler(IJobRepository repo, IUnitOfWork uow) { _repo = repo; _uow = uow; }

    public async Task<JobDto?> Handle(GetJobQuery req, CancellationToken ct)
    {
        var job = await _repo.GetByIdAsync(req.JobId, ct);
        if (job is null) return null;
        job.IncrementViews();
        await _repo.UpdateAsync(job, ct);
        await _uow.SaveChangesAsync(ct);
        return JobMapper.Map(job);
    }
}
