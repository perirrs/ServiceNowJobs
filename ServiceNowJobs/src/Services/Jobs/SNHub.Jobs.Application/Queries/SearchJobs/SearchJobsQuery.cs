using MediatR;
using SNHub.Jobs.Application.DTOs;
using SNHub.Jobs.Application.Interfaces;
using SNHub.Jobs.Domain.Enums;

namespace SNHub.Jobs.Application.Queries.SearchJobs;

public sealed record SearchJobsQuery(
    string? Keyword,
    string? Country,
    string? Location,
    JobType? JobType,
    WorkMode? WorkMode,
    ExperienceLevel? Level,
    decimal? SalaryMin,
    decimal? SalaryMax,
    Guid? EmployerId = null,
    int Page = 1,
    int PageSize = 20) : IRequest<JobSearchResultDto>;

public sealed class SearchJobsQueryHandler : IRequestHandler<SearchJobsQuery, JobSearchResultDto>
{
    private readonly IJobRepository _repo;

    public SearchJobsQueryHandler(IJobRepository repo) => _repo = repo;

    public async Task<JobSearchResultDto> Handle(SearchJobsQuery req, CancellationToken ct)
    {
        var (items, total) = await _repo.SearchAsync(
            req.Keyword, req.Country, req.Location,
            req.JobType, req.WorkMode, req.Level,
            req.SalaryMin, req.SalaryMax,
            JobStatus.Active, req.EmployerId,
            req.Page, req.PageSize, ct);

        var dtos = items.Select(JobMapper.ToDto);
        int totalPages = total == 0 ? 0 : (int)Math.Ceiling((double)total / req.PageSize);

        return new JobSearchResultDto(
            dtos, total, req.Page, req.PageSize, totalPages,
            HasNextPage: req.Page < totalPages,
            HasPreviousPage: req.Page > 1);
    }
}
