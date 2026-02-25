using MediatR;
using SNHub.Jobs.Application.DTOs;
using SNHub.Jobs.Application.Interfaces;
using SNHub.Jobs.Domain.Enums;

namespace SNHub.Jobs.Application.Queries.GetMyJobs;

public sealed record GetMyJobsQuery(
    Guid EmployerId,
    JobStatus? Status,
    int Page = 1,
    int PageSize = 20) : IRequest<JobSearchResultDto>;

public sealed class GetMyJobsQueryHandler : IRequestHandler<GetMyJobsQuery, JobSearchResultDto>
{
    private readonly IJobRepository _repo;

    public GetMyJobsQueryHandler(IJobRepository repo) => _repo = repo;

    public async Task<JobSearchResultDto> Handle(GetMyJobsQuery req, CancellationToken ct)
    {
        // Status = null means return all statuses (employer sees draft, paused, closed too)
        var (items, total) = await _repo.SearchAsync(
            keyword: null, country: null, location: null,
            jobType: null, workMode: null, level: null,
            salaryMin: null, salaryMax: null,
            status: req.Status,    // null = all statuses for employer view
            employerId: req.EmployerId,
            page: req.Page, pageSize: req.PageSize, ct);

        var dtos = items.Select(JobMapper.ToDto);
        int totalPages = total == 0 ? 0 : (int)Math.Ceiling((double)total / req.PageSize);

        return new JobSearchResultDto(
            dtos, total, req.Page, req.PageSize, totalPages,
            HasNextPage: req.Page < totalPages,
            HasPreviousPage: req.Page > 1);
    }
}
