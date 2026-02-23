using SNHub.Profiles.Domain.Enums;

namespace SNHub.Profiles.Domain.Entities;

/// <summary>
/// Extended ServiceNow-specific candidate profile.
/// Skills, certifications, and experience stored as JSON.
/// </summary>
public sealed class CandidateProfile
{
    private CandidateProfile() { }

    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public string? Headline { get; private set; }
    public string? Bio { get; private set; }
    public ExperienceLevel ExperienceLevel { get; private set; }
    public int YearsOfExperience { get; private set; }
    public AvailabilityStatus Availability { get; private set; }
    public string? CurrentRole { get; private set; }
    public string? DesiredRole { get; private set; }
    public string? Location { get; private set; }
    public string? Country { get; private set; }
    public string? TimeZone { get; private set; }
    public string? ProfilePictureUrl { get; private set; }
    public string? CvUrl { get; private set; }
    public string? LinkedInUrl { get; private set; }
    public string? GitHubUrl { get; private set; }
    public string? WebsiteUrl { get; private set; }
    public bool IsPublic { get; private set; } = true;
    public decimal? DesiredSalaryMin { get; private set; }
    public decimal? DesiredSalaryMax { get; private set; }
    public string? SalaryCurrency { get; private set; }
    public bool OpenToRemote { get; private set; }
    public bool OpenToRelocation { get; private set; }
    // ServiceNow-specific — stored as JSON arrays
    public string SkillsJson { get; private set; } = "[]";          // ["ITSM","HRSD","CSM"]
    public string CertificationsJson { get; private set; } = "[]";  // [{"type":1,"name":"CSA","year":2023}]
    public string ServiceNowVersionsJson { get; private set; } = "[]"; // ["Xanadu","Washington"]
    public int ProfileCompleteness { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static CandidateProfile Create(Guid userId) => new()
    {
        Id = Guid.NewGuid(), UserId = userId,
        Availability = AvailabilityStatus.OpenToOpportunities,
        ExperienceLevel = ExperienceLevel.Mid,
        IsPublic = true, SalaryCurrency = "USD",
        CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
    };

    public void Update(string? headline, string? bio, ExperienceLevel level, int years,
        AvailabilityStatus availability, string? currentRole, string? desiredRole,
        string? location, string? country, string? timeZone,
        string? linkedIn, string? gitHub, string? website,
        bool isPublic, decimal? salMin, decimal? salMax, string? salCurrency,
        bool remote, bool relocation)
    {
        Headline = headline?.Trim(); Bio = bio?.Trim();
        ExperienceLevel = level; YearsOfExperience = years;
        Availability = availability; CurrentRole = currentRole?.Trim(); DesiredRole = desiredRole?.Trim();
        Location = location?.Trim(); Country = country; TimeZone = timeZone;
        LinkedInUrl = linkedIn?.Trim(); GitHubUrl = gitHub?.Trim(); WebsiteUrl = website?.Trim();
        IsPublic = isPublic;
        DesiredSalaryMin = salMin; DesiredSalaryMax = salMax; SalaryCurrency = salCurrency ?? "USD";
        OpenToRemote = remote; OpenToRelocation = relocation;
        UpdatedAt = DateTimeOffset.UtcNow;
        RecalculateCompleteness();
    }

    public void SetSkills(string skillsJson) { SkillsJson = skillsJson; UpdatedAt = DateTimeOffset.UtcNow; RecalculateCompleteness(); }
    public void SetCertifications(string certsJson) { CertificationsJson = certsJson; UpdatedAt = DateTimeOffset.UtcNow; RecalculateCompleteness(); }
    public void SetServiceNowVersions(string versionsJson) { ServiceNowVersionsJson = versionsJson; UpdatedAt = DateTimeOffset.UtcNow; }
    public void SetProfilePicture(string url) { ProfilePictureUrl = url; UpdatedAt = DateTimeOffset.UtcNow; RecalculateCompleteness(); }
    public void SetCvUrl(string url) { CvUrl = url; UpdatedAt = DateTimeOffset.UtcNow; RecalculateCompleteness(); }

    private void RecalculateCompleteness()
    {
        int score = 0;
        if (!string.IsNullOrWhiteSpace(Headline)) score += 10;
        if (!string.IsNullOrWhiteSpace(Bio)) score += 15;
        if (!string.IsNullOrWhiteSpace(ProfilePictureUrl)) score += 10;
        if (!string.IsNullOrWhiteSpace(CvUrl)) score += 20;
        if (SkillsJson != "[]") score += 20;
        if (CertificationsJson != "[]") score += 15;
        if (!string.IsNullOrWhiteSpace(LinkedInUrl)) score += 5;
        if (!string.IsNullOrWhiteSpace(Location)) score += 5;
        ProfileCompleteness = Math.Min(score, 100);
    }
}
