using System.Text.Json;

namespace SNHub.Profiles.Application.DTOs;

public sealed record CandidateProfileDto(
    Guid Id,
    Guid UserId,
    string? Headline,
    string? Bio,
    string ExperienceLevel,
    int YearsOfExperience,
    string Availability,
    string? CurrentRole,
    string? DesiredRole,
    string? Location,
    string? Country,
    string? TimeZone,
    string? ProfilePictureUrl,
    string? CvUrl,
    string? LinkedInUrl,
    string? GitHubUrl,
    string? WebsiteUrl,
    bool IsPublic,
    decimal? DesiredSalaryMin,
    decimal? DesiredSalaryMax,
    string? SalaryCurrency,
    bool OpenToRemote,
    bool OpenToRelocation,
    IEnumerable<string> Skills,
    IEnumerable<object> Certifications,
    IEnumerable<string> ServiceNowVersions,
    int ProfileCompleteness,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record EmployerProfileDto(
    Guid Id,
    Guid UserId,
    string? CompanyName,
    string? CompanyDescription,
    string? Industry,
    string? CompanySize,
    string? HeadquartersCity,
    string? HeadquartersCountry,
    string? WebsiteUrl,
    string? LinkedInUrl,
    string? LogoUrl,
    bool IsVerified,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record PagedResult<T>(
    IEnumerable<T> Items,
    int Total,
    int Page,
    int PageSize,
    bool HasNextPage,
    bool HasPreviousPage)
{
    public int TotalPages => Total == 0 ? 0 : (int)Math.Ceiling((double)Total / PageSize);

    public static PagedResult<T> Create(IEnumerable<T> items, int total, int page, int pageSize)
    {
        int totalPages = total == 0 ? 0 : (int)Math.Ceiling((double)total / pageSize);
        return new PagedResult<T>(items, total, page, pageSize,
            HasNextPage: page < totalPages, HasPreviousPage: page > 1);
    }
}
