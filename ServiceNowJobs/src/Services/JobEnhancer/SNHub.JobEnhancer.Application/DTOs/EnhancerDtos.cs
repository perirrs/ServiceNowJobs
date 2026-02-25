using SNHub.JobEnhancer.Domain.Enums;

namespace SNHub.JobEnhancer.Application.DTOs;

// ── Main response ─────────────────────────────────────────────────────────────

public sealed record EnhancementResultDto(
    Guid   Id,
    Guid   JobId,
    string Status,

    // Original
    string  OriginalTitle,
    string  OriginalDescription,
    string? OriginalRequirements,

    // Enhanced
    string? EnhancedTitle,
    string? EnhancedDescription,
    string? EnhancedRequirements,

    // Quality
    int  ScoreBefore,
    int  ScoreAfter,
    int  ScoreImprovement,

    // Structured feedback
    BiasIssueDto[]   BiasIssues,
    string[]         MissingFields,
    ImprovementDto[] Improvements,
    string[]         SuggestedSkills,

    // State
    bool            IsAccepted,
    DateTimeOffset? AcceptedAt,
    DateTimeOffset  CreatedAt);

public sealed record BiasIssueDto(
    string Text,        // exact phrase that's biased
    string Reason,      // why it's biased
    string Suggestion,  // alternative wording
    string Severity);   // Low / Medium / High

public sealed record ImprovementDto(
    string Category,    // Clarity / Specificity / Structure / SEO / Inclusivity
    string Description, // what was improved
    string Before,      // original fragment
    string After);      // improved fragment

public sealed record AcceptEnhancementResponse(
    Guid   EnhancementId,
    Guid   JobId,
    bool   Accepted,
    string Message);

// ── What GPT-4o returns (internal parsing model) ─────────────────────────────

public sealed class GptEnhancementResult
{
    public string? EnhancedTitle        { get; set; }
    public string? EnhancedDescription  { get; set; }
    public string? EnhancedRequirements { get; set; }
    public int     ScoreBefore          { get; set; }
    public int     ScoreAfter           { get; set; }
    public GptBiasIssue[]   BiasIssues      { get; set; } = [];
    public string[]          MissingFields   { get; set; } = [];
    public GptImprovement[]  Improvements    { get; set; } = [];
    public string[]          SuggestedSkills { get; set; } = [];
}

public sealed class GptBiasIssue
{
    public string Text       { get; set; } = string.Empty;
    public string Reason     { get; set; } = string.Empty;
    public string Suggestion { get; set; } = string.Empty;
    public string Severity   { get; set; } = "Low";
}

public sealed class GptImprovement
{
    public string Category    { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Before      { get; set; } = string.Empty;
    public string After       { get; set; } = string.Empty;
}
