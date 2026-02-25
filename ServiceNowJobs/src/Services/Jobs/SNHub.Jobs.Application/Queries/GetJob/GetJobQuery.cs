using MediatR;
using SNHub.Jobs.Application.DTOs;
using SNHub.Jobs.Application.Interfaces;
using SNHub.Jobs.Domain.Exceptions;

namespace SNHub.Jobs.Application.Queries.GetJob;

public sealed record GetJobQuery(Guid JobId) : IRequest<JobDto>;

public sealed class GetJobQueryHandler : IRequestHandler<GetJobQuery, JobDto>
{
    private readonly IJobRepository _repo;
    private readonly IUnitOfWork _uow;

    public GetJobQueryHandler(IJobRepository repo, IUnitOfWork uow)
    {
        _repo = repo;
        _uow = uow;
    }

    public async Task<JobDto> Handle(GetJobQuery req, CancellationToken ct)
    {
        var job = await _repo.GetByIdAsync(req.JobId, ct)
            ?? throw new JobNotFoundException(req.JobId);

        job.IncrementViews();
        await _repo.UpdateAsync(job, ct);
        await _uow.SaveChangesAsync(ct);

        return JobMapper.ToDto(job);
    }
}
