namespace SNHub.Jobs.IntegrationTests.Models;

// ── Request models ────────────────────────────────────────────────────────────

public sealed record CreateJobRequest(
    string Title,
    string Description,
    string? Requirements,
    string? Benefits,
    string? CompanyName,
    int JobType,         // 1=FullTime,2=PartTime,3=Contract,4=Freelance,5=Internship
    int WorkMode,        // 1=Remote,2=Hybrid,3=OnSite
    int ExperienceLevel, // 1=Junior,2=MidLevel,3=Senior,4=Lead,5=Principal
    string? Location,
    string? Country,
    decimal? SalaryMin,
    decimal? SalaryMax,
    string? SalaryCurrency,
    bool IsSalaryVisible,
    IReadOnlyList<string>? SkillsRequired,
    IReadOnlyList<string>? CertificationsRequired,
    IReadOnlyList<string>? ServiceNowVersions,
    bool PublishImmediately,
    DateTimeOffset? ExpiresAt);

public sealed record UpdateJobRequest(
    string Title,
    string Description,
    string? Requirements,
    string? Benefits,
    int JobType,
    int WorkMode,
    int ExperienceLevel,
    string? Location,
    string? Country,
    decimal? SalaryMin,
    decimal? SalaryMax,
    string? SalaryCurrency,
    bool IsSalaryVisible,
    IReadOnlyList<string>? SkillsRequired,
    IReadOnlyList<string>? CertificationsRequired,
    IReadOnlyList<string>? ServiceNowVersions,
    DateTimeOffset? ExpiresAt);

// ── Response models ───────────────────────────────────────────────────────────

public sealed class JobResponse
{
    public Guid Id { get; set; }
    public Guid EmployerId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? Requirements { get; set; }
    public string? Benefits { get; set; }
    public string? CompanyName { get; set; }
    public string? Location { get; set; }
    public string? Country { get; set; }
    public string JobType { get; set; } = string.Empty;
    public string WorkMode { get; set; } = string.Empty;
    public string ExperienceLevel { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal? SalaryMin { get; set; }
    public decimal? SalaryMax { get; set; }
    public string? SalaryCurrency { get; set; }
    public bool IsSalaryVisible { get; set; }
    public List<string> SkillsRequired { get; set; } = [];
    public List<string> CertificationsRequired { get; set; } = [];
    public List<string> ServiceNowVersions { get; set; } = [];
    public int ApplicationCount { get; set; }
    public int ViewCount { get; set; }
    public bool IsActive { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class JobSearchResponse
{
    public List<JobResponse> Items { get; set; } = [];
    public int Total { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalPages { get; set; }
    public bool HasNextPage { get; set; }
    public bool HasPreviousPage { get; set; }
}
