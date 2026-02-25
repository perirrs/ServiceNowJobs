using SNHub.Jobs.Domain.Enums;

namespace SNHub.Jobs.Domain.Entities;

public sealed class Job
{
    private Job() { }

    public Guid Id { get; private set; }
    public Guid EmployerId { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public string? Requirements { get; private set; }
    public string? Benefits { get; private set; }
    public string? CompanyName { get; private set; }
    public string? CompanyLogoUrl { get; private set; }
    public string? Location { get; private set; }
    public string? Country { get; private set; }
    public JobType JobType { get; private set; }
    public WorkMode WorkMode { get; private set; }
    public ExperienceLevel ExperienceLevel { get; private set; }
    public JobStatus Status { get; private set; }
    public decimal? SalaryMin { get; private set; }
    public decimal? SalaryMax { get; private set; }
    public string? SalaryCurrency { get; private set; }
    public bool IsSalaryVisible { get; private set; }
    public string SkillsRequired { get; private set; } = "[]"; // JSON array
    public string? ServiceNowVersions { get; private set; }    // JSON array
    public string? CertificationsRequired { get; private set; } // JSON array
    public int ApplicationCount { get; private set; }
    public int ViewCount { get; private set; }
    public DateTimeOffset? ExpiresAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public bool IsActive => Status == JobStatus.Active && (ExpiresAt == null || ExpiresAt > DateTimeOffset.UtcNow);

    public static Job Create(Guid employerId, string title, string description,
        JobType jobType, WorkMode workMode, ExperienceLevel experienceLevel,
        string? location, string? country, string? companyName,
        decimal? salaryMin, decimal? salaryMax, string? currency, bool salaryVisible,
        DateTimeOffset? expiresAt) => new()
    {
        Id               = Guid.NewGuid(),
        EmployerId       = employerId,
        Title            = title.Trim(),
        Description      = description.Trim(),
        JobType          = jobType,
        WorkMode         = workMode,
        ExperienceLevel  = experienceLevel,
        Location         = location?.Trim(),
        Country          = country,
        CompanyName      = companyName?.Trim(),
        SalaryMin        = salaryMin,
        SalaryMax        = salaryMax,
        SalaryCurrency   = currency ?? "USD",
        IsSalaryVisible  = salaryVisible,
        Status           = JobStatus.Draft,
        ExpiresAt        = expiresAt,
        CreatedAt        = DateTimeOffset.UtcNow,
        UpdatedAt        = DateTimeOffset.UtcNow
    };

    public void Publish() { Status = JobStatus.Active; UpdatedAt = DateTimeOffset.UtcNow; }
    public void Pause()   { Status = JobStatus.Paused; UpdatedAt = DateTimeOffset.UtcNow; }
    public void Close()   { Status = JobStatus.Closed; UpdatedAt = DateTimeOffset.UtcNow; }

    public void Update(string title, string description, string? requirements, string? benefits,
        JobType jobType, WorkMode workMode, ExperienceLevel level,
        string? location, string? country, decimal? salaryMin, decimal? salaryMax,
        string? currency, bool salaryVisible, DateTimeOffset? expiresAt)
    {
        Title           = title.Trim();
        Description     = description.Trim();
        Requirements    = requirements?.Trim();
        Benefits        = benefits?.Trim();
        JobType         = jobType;
        WorkMode        = workMode;
        ExperienceLevel = level;
        Location        = location?.Trim();
        Country         = country;
        SalaryMin       = salaryMin;
        SalaryMax       = salaryMax;
        SalaryCurrency  = currency ?? SalaryCurrency;
        IsSalaryVisible = salaryVisible;
        ExpiresAt       = expiresAt;
        UpdatedAt       = DateTimeOffset.UtcNow;
    }

    public void SetSkills(string skillsJson) { SkillsRequired = skillsJson; UpdatedAt = DateTimeOffset.UtcNow; }
    public void SetCertifications(string certsJson) { CertificationsRequired = certsJson; UpdatedAt = DateTimeOffset.UtcNow; }
    public void SetServiceNowVersions(string versionsJson) { ServiceNowVersions = versionsJson; UpdatedAt = DateTimeOffset.UtcNow; }
    public void IncrementViews() { ViewCount++; }
    public void IncrementApplications() { ApplicationCount++; }
    public void DecrementApplications() { if (ApplicationCount > 0) ApplicationCount--; }
}
