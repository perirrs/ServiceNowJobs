using SNHub.CvParser.Domain.Enums;

namespace SNHub.CvParser.Domain.Entities;

/// <summary>
/// Represents one CV parse attempt. Stores the raw extracted data
/// and confidence scores before the user accepts it into their profile.
/// </summary>
public sealed class CvParseResult
{
    private CvParseResult() { }

    public Guid   Id               { get; private set; }
    public Guid   UserId           { get; private set; }

    // Storage reference
    public string BlobPath         { get; private set; } = string.Empty;
    public string OriginalFileName { get; private set; } = string.Empty;
    public string ContentType      { get; private set; } = string.Empty;
    public long   FileSizeBytes    { get; private set; }

    // Parse status
    public ParseStatus Status      { get; private set; }
    public string?     ErrorMessage{ get; private set; }

    // Extracted fields (all nullable — parser may not find everything)
    public string? FirstName           { get; private set; }
    public string? LastName            { get; private set; }
    public string? Email               { get; private set; }
    public string? Phone               { get; private set; }
    public string? Location            { get; private set; }
    public string? Headline            { get; private set; }
    public string? Summary             { get; private set; }
    public string? CurrentRole         { get; private set; }
    public int?    YearsOfExperience   { get; private set; }
    public string? LinkedInUrl         { get; private set; }
    public string? GitHubUrl           { get; private set; }

    // ServiceNow-specific — stored as JSON arrays
    public string SkillsJson           { get; private set; } = "[]";
    public string CertificationsJson   { get; private set; } = "[]";
    public string ServiceNowVersionsJson { get; private set; } = "[]";

    // Confidence scores (0-100)
    public int OverallConfidence        { get; private set; }
    public string FieldConfidencesJson  { get; private set; } = "{}";

    // Applied to profile?
    public bool   IsApplied            { get; private set; }
    public DateTimeOffset? AppliedAt   { get; private set; }

    public DateTimeOffset CreatedAt    { get; private set; }
    public DateTimeOffset UpdatedAt    { get; private set; }

    public static CvParseResult Create(
        Guid userId, string blobPath, string originalFileName,
        string contentType, long fileSizeBytes) => new()
    {
        Id               = Guid.NewGuid(),
        UserId           = userId,
        BlobPath         = blobPath,
        OriginalFileName = originalFileName,
        ContentType      = contentType,
        FileSizeBytes    = fileSizeBytes,
        Status           = ParseStatus.Pending,
        CreatedAt        = DateTimeOffset.UtcNow,
        UpdatedAt        = DateTimeOffset.UtcNow
    };

    public void SetProcessing()
    {
        Status    = ParseStatus.Processing;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void SetCompleted(
        string? firstName, string? lastName, string? email, string? phone,
        string? location, string? headline, string? summary, string? currentRole,
        int? yearsOfExperience, string? linkedInUrl, string? gitHubUrl,
        string skillsJson, string certificationsJson, string serviceNowVersionsJson,
        int overallConfidence, string fieldConfidencesJson)
    {
        Status                 = ParseStatus.Completed;
        FirstName              = firstName?.Trim();
        LastName               = lastName?.Trim();
        Email                  = email?.Trim().ToLowerInvariant();
        Phone                  = phone?.Trim();
        Location               = location?.Trim();
        Headline               = headline?.Trim();
        Summary                = summary?.Trim();
        CurrentRole            = currentRole?.Trim();
        YearsOfExperience      = yearsOfExperience;
        LinkedInUrl            = linkedInUrl?.Trim();
        GitHubUrl              = gitHubUrl?.Trim();
        SkillsJson             = skillsJson;
        CertificationsJson     = certificationsJson;
        ServiceNowVersionsJson = serviceNowVersionsJson;
        OverallConfidence      = Math.Clamp(overallConfidence, 0, 100);
        FieldConfidencesJson   = fieldConfidencesJson;
        UpdatedAt              = DateTimeOffset.UtcNow;
    }

    public void SetFailed(string error)
    {
        Status       = ParseStatus.Failed;
        ErrorMessage = error;
        UpdatedAt    = DateTimeOffset.UtcNow;
    }

    public void MarkApplied()
    {
        IsApplied = true;
        AppliedAt = DateTimeOffset.UtcNow;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
