using SNHub.Jobs.Domain.Enums;

namespace SNHub.Jobs.Application.DTOs;

public sealed record JobDto(
    Guid Id,
    Guid EmployerId,
    string Title,
    string Description,
    string? Requirements,
    string? Benefits,
    string? CompanyName,
    string? CompanyLogoUrl,
    string? Location,
    string? Country,
    string JobType,
    string WorkMode,
    string ExperienceLevel,
    string Status,
    decimal? SalaryMin,
    decimal? SalaryMax,
    string? SalaryCurrency,
    bool IsSalaryVisible,
    IEnumerable<string> SkillsRequired,
    IEnumerable<string> CertificationsRequired,
    IEnumerable<string> ServiceNowVersions,
    int ApplicationCount,
    int ViewCount,
    bool IsActive,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record JobSearchResultDto(
    IEnumerable<JobDto> Items,
    int Total,
    int Page,
    int PageSize,
    int TotalPages,
    bool HasNextPage,
    bool HasPreviousPage);

