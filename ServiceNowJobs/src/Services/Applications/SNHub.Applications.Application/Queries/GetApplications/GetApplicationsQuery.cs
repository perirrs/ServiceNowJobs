using MediatR;
using SNHub.Applications.Application.Commands.ApplyToJob;
using SNHub.Applications.Application.DTOs;
using SNHub.Applications.Application.Interfaces;

namespace SNHub.Applications.Application.Queries.GetApplications;

public sealed record GetMyApplicationsQuery(Guid CandidateId, int Page = 1, int PageSize = 20)
    : IRequest<PagedResult<ApplicationDto>>;

public sealed class GetMyApplicationsQueryHandler : IRequestHandler<GetMyApplicationsQuery, PagedResult<ApplicationDto>>
{
    private readonly IApplicationRepository _repo;
    public GetMyApplicationsQueryHandler(IApplicationRepository repo) => _repo = repo;
    public async Task<PagedResult<ApplicationDto>> Handle(GetMyApplicationsQuery req, CancellationToken ct)
    {
        var (items, total) = await _repo.GetByCandidateAsync(req.CandidateId, req.Page, req.PageSize, ct);
        return new PagedResult<ApplicationDto>(items.Select(ApplyToJobCommandHandler.ToDto), total, req.Page, req.PageSize);
    }
}

public sealed record GetJobApplicationsQuery(Guid JobId, string? Status, int Page = 1, int PageSize = 20)
    : IRequest<PagedResult<ApplicationDto>>;

public sealed class GetJobApplicationsQueryHandler : IRequestHandler<GetJobApplicationsQuery, PagedResult<ApplicationDto>>
{
    private readonly IApplicationRepository _repo;
    public GetJobApplicationsQueryHandler(IApplicationRepository repo) => _repo = repo;
    public async Task<PagedResult<ApplicationDto>> Handle(GetJobApplicationsQuery req, CancellationToken ct)
    {
        Domain.Enums.ApplicationStatus? status = null;
        if (!string.IsNullOrWhiteSpace(req.Status) &&
            Enum.TryParse<Domain.Enums.ApplicationStatus>(req.Status, true, out var s))
            status = s;
        var (items, total) = await _repo.GetByJobAsync(req.JobId, status, req.Page, req.PageSize, ct);
        return new PagedResult<ApplicationDto>(items.Select(ApplyToJobCommandHandler.ToDto), total, req.Page, req.PageSize);
    }
}
