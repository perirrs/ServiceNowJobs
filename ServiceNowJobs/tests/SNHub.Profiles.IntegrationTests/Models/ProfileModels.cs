namespace SNHub.Profiles.IntegrationTests.Models;

// ── Requests ──────────────────────────────────────────────────────────────────

public sealed record UpsertCandidateRequest(
    string? Headline, string? Bio,
    int ExperienceLevel, int YearsOfExperience, int Availability,
    string? CurrentRole, string? DesiredRole,
    string? Location, string? Country, string? TimeZone,
    string? LinkedInUrl, string? GitHubUrl, string? WebsiteUrl,
    bool IsPublic = true,
    decimal? DesiredSalaryMin = null, decimal? DesiredSalaryMax = null,
    string? SalaryCurrency = "USD",
    bool OpenToRemote = false, bool OpenToRelocation = false,
    List<string>? Skills = null,
    string? CertificationsJson = null,
    List<string>? ServiceNowVersions = null);

public sealed record UpsertEmployerRequest(
    string? CompanyName, string? CompanyDescription,
    string? Industry, string? CompanySize,
    string? City, string? Country,
    string? WebsiteUrl, string? LinkedInUrl);

// ── Responses ─────────────────────────────────────────────────────────────────

public sealed class CandidateProfileResponse
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string? Headline { get; set; }
    public string? Bio { get; set; }
    public string ExperienceLevel { get; set; } = string.Empty;
    public int YearsOfExperience { get; set; }
    public string Availability { get; set; } = string.Empty;
    public string? CurrentRole { get; set; }
    public string? Location { get; set; }
    public string? Country { get; set; }
    public bool IsPublic { get; set; }
    public bool OpenToRemote { get; set; }
    public List<string> Skills { get; set; } = [];
    public List<string> ServiceNowVersions { get; set; } = [];
    public int ProfileCompleteness { get; set; }
    public string? ProfilePictureUrl { get; set; }
    public string? CvUrl { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class EmployerProfileResponse
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string? CompanyName { get; set; }
    public string? Industry { get; set; }
    public string? CompanySize { get; set; }
    public bool IsVerified { get; set; }
}

public sealed class PagedCandidateResponse
{
    public List<CandidateProfileResponse> Items { get; set; } = [];
    public int Total { get; set; }
    public int Page { get; set; }
    public int TotalPages { get; set; }
    public bool HasNextPage { get; set; }
}

public sealed class UploadedFileResponse
{
    public string Url { get; set; } = string.Empty;
}
