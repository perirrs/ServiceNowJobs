namespace SNHub.Matching.Application.DTOs;

// ── Match results ─────────────────────────────────────────────────────────────

public sealed record JobMatchDto(
    Guid    JobId,
    string  Title,
    string? CompanyName,
    string? Location,
    string? Country,
    string  WorkMode,
    string  ExperienceLevel,
    decimal? SalaryMin,
    decimal? SalaryMax,
    string? SalaryCurrency,
    string[] SkillsRequired,
    double  Score,          // 0-1 cosine similarity
    int     ScorePercent,   // 0-100 human-readable
    string[] MatchedSkills, // skills in both job and candidate
    DateTimeOffset PostedAt);

public sealed record CandidateMatchDto(
    Guid    UserId,
    string? FullName,
    string? Headline,
    string? CurrentRole,
    string? Location,
    int     YearsOfExperience,
    string  ExperienceLevel,
    string  Availability,
    string[] Skills,
    string[] Certifications,
    double  Score,
    int     ScorePercent,
    string[] MatchedSkills,
    DateTimeOffset ProfileUpdatedAt);

public sealed record MatchResultsDto<T>(
    int    Total,
    int    Page,
    int    PageSize,
    bool   EmbeddingReady,
    T[]    Results);

// ── Embedding status ──────────────────────────────────────────────────────────

public sealed record EmbeddingStatusDto(
    Guid   DocumentId,
    string DocumentType,
    string Status,
    DateTimeOffset? LastIndexedAt,
    int    RetryCount);

// ── Internal search models ────────────────────────────────────────────────────

/// <summary>Document stored in Azure AI Search for a job posting.</summary>
public sealed class JobSearchDocument
{
    public string   Id              { get; set; } = string.Empty;  // JobId
    public string   Title           { get; set; } = string.Empty;
    public string   Description     { get; set; } = string.Empty;
    public string?  Requirements    { get; set; }
    public string?  CompanyName     { get; set; }
    public string?  Location        { get; set; }
    public string?  Country         { get; set; }
    public string   WorkMode        { get; set; } = string.Empty;
    public string   ExperienceLevel { get; set; } = string.Empty;
    public string   JobType         { get; set; } = string.Empty;
    public decimal? SalaryMin       { get; set; }
    public decimal? SalaryMax       { get; set; }
    public string?  SalaryCurrency  { get; set; }
    public bool     IsSalaryVisible { get; set; }
    public string[] Skills          { get; set; } = [];
    public string[] ServiceNowVersions { get; set; } = [];
    public float[]  Embedding       { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Document stored in Azure AI Search for a candidate profile.</summary>
public sealed class CandidateSearchDocument
{
    public string   Id              { get; set; } = string.Empty;  // UserId
    public string?  FullName        { get; set; }
    public string?  Headline        { get; set; }
    public string?  Summary         { get; set; }
    public string?  CurrentRole     { get; set; }
    public string?  Location        { get; set; }
    public string?  Country         { get; set; }
    public int      YearsOfExperience { get; set; }
    public string   ExperienceLevel { get; set; } = string.Empty;
    public string   Availability    { get; set; } = string.Empty;
    public bool     OpenToRemote    { get; set; }
    public string[] Skills          { get; set; } = [];
    public string[] Certifications  { get; set; } = [];
    public string[] ServiceNowVersions { get; set; } = [];
    public float[]  Embedding       { get; set; } = [];
    public DateTimeOffset UpdatedAt { get; set; }
}
