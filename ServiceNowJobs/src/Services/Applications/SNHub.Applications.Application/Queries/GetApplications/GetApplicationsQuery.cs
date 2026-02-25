using MediatR;
using SNHub.Applications.Application.DTOs;
using SNHub.Applications.Application.Interfaces;
using SNHub.Applications.Domain.Enums;
using SNHub.Applications.Domain.Exceptions;

namespace SNHub.Applications.Application.Queries.GetApplications;

// ── Get single application ───────────────────────────────────────────────────

public sealed record GetApplicationByIdQuery(Guid ApplicationId, Guid RequesterId, bool IsEmployer)
    : IRequest<ApplicationDto>;

public sealed class GetApplicationByIdQueryHandler : IRequestHandler<GetApplicationByIdQuery, ApplicationDto>
{
    private readonly IApplicationRepository _repo;
    public GetApplicationByIdQueryHandler(IApplicationRepository repo) => _repo = repo;

    public async Task<ApplicationDto> Handle(GetApplicationByIdQuery req, CancellationToken ct)
    {
        var app = await _repo.GetByIdAsync(req.ApplicationId, ct)
            ?? throw new ApplicationNotFoundException(req.ApplicationId);

        // Candidate can only see their own; employers can see any
        if (!req.IsEmployer && app.CandidateId != req.RequesterId)
            throw new ApplicationAccessDeniedException();

        return ApplicationMapper.ToDto(app);
    }
}

// ── Get candidate's own applications ────────────────────────────────────────

public sealed record GetMyApplicationsQuery(Guid CandidateId, int Page = 1, int PageSize = 20)
    : IRequest<PagedResult<ApplicationDto>>;

public sealed class GetMyApplicationsQueryHandler
    : IRequestHandler<GetMyApplicationsQuery, PagedResult<ApplicationDto>>
{
    private readonly IApplicationRepository _repo;
    public GetMyApplicationsQueryHandler(IApplicationRepository repo) => _repo = repo;

    public async Task<PagedResult<ApplicationDto>> Handle(GetMyApplicationsQuery req, CancellationToken ct)
    {
        var (items, total) = await _repo.GetByCandidateAsync(req.CandidateId, req.Page, req.PageSize, ct);
        return PagedResult<ApplicationDto>.Create(items.Select(ApplicationMapper.ToDto), total, req.Page, req.PageSize);
    }
}

// ── Get applications for a job (employer view) ───────────────────────────────

public sealed record GetJobApplicationsQuery(
    Guid JobId,
    ApplicationStatus? Status,
    int Page = 1,
    int PageSize = 20) : IRequest<PagedResult<ApplicationDto>>;

public sealed class GetJobApplicationsQueryHandler
    : IRequestHandler<GetJobApplicationsQuery, PagedResult<ApplicationDto>>
{
    private readonly IApplicationRepository _repo;
    public GetJobApplicationsQueryHandler(IApplicationRepository repo) => _repo = repo;

    public async Task<PagedResult<ApplicationDto>> Handle(GetJobApplicationsQuery req, CancellationToken ct)
    {
        var (items, total) = await _repo.GetByJobAsync(req.JobId, req.Status, req.Page, req.PageSize, ct);
        return PagedResult<ApplicationDto>.Create(items.Select(ApplicationMapper.ToDto), total, req.Page, req.PageSize);
    }
}
