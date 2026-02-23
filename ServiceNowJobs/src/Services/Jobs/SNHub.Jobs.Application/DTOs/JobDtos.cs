using SNHub.Jobs.Domain.Enums;

namespace SNHub.Jobs.Application.DTOs;

public sealed record JobDto(
    Guid Id, Guid EmployerId,
    string Title, string Description, string? Requirements, string? Benefits,
    string? CompanyName, string? CompanyLogoUrl,
    string? Location, string? Country,
    JobType JobType, WorkMode WorkMode, ExperienceLevel ExperienceLevel,
    JobStatus Status,
    decimal? SalaryMin, decimal? SalaryMax, string? SalaryCurrency, bool IsSalaryVisible,
    string SkillsRequired,
    int ApplicationCount, int ViewCount,
    DateTimeOffset? ExpiresAt, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

public sealed record JobSearchResultDto(
    IEnumerable<JobDto> Items, int Total, int Page, int PageSize, int TotalPages);
