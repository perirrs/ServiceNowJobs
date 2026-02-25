using SNHub.JobEnhancer.Domain.Enums;

namespace SNHub.JobEnhancer.Domain.Entities;

/// <summary>
/// Records one AI enhancement attempt for a job description.
/// The employer reviews the suggestions and explicitly accepts them.
/// Multiple attempts per job are allowed (each creates a new record).
/// </summary>
public sealed class EnhancementResult
{
    private EnhancementResult() { }

    public Guid              Id            { get; private set; }
    public Guid              JobId         { get; private set; }
    public Guid              RequestedBy   { get; private set; }
    public EnhancementStatus Status        { get; private set; }
    public string?           ErrorMessage  { get; private set; }

    // Original input
    public string  OriginalTitle       { get; private set; } = string.Empty;
    public string  OriginalDescription { get; private set; } = string.Empty;
    public string? OriginalRequirements{ get; private set; }

    // AI output — enhanced text
    public string? EnhancedTitle       { get; private set; }
    public string? EnhancedDescription { get; private set; }
    public string? EnhancedRequirements{ get; private set; }

    // Quality scores (0-100)
    public int  ScoreBefore      { get; private set; }
    public int  ScoreAfter       { get; private set; }
    public int  ScoreImprovement => ScoreAfter - ScoreBefore;

    // Structured feedback (JSON arrays stored as strings)
    public string BiasIssuesJson        { get; private set; } = "[]"; // BiasIssue[]
    public string MissingFieldsJson     { get; private set; } = "[]"; // string[]
    public string ImprovementsJson      { get; private set; } = "[]"; // Improvement[]
    public string SuggestedSkillsJson   { get; private set; } = "[]"; // string[]

    // Acceptance tracking
    public bool             IsAccepted { get; private set; }
    public DateTimeOffset?  AcceptedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static EnhancementResult Create(
        Guid jobId, Guid requestedBy,
        string title, string description, string? requirements) => new()
    {
        Id                   = Guid.NewGuid(),
        JobId                = jobId,
        RequestedBy          = requestedBy,
        Status               = EnhancementStatus.Processing,
        OriginalTitle        = title.Trim(),
        OriginalDescription  = description.Trim(),
        OriginalRequirements = requirements?.Trim(),
        CreatedAt            = DateTimeOffset.UtcNow,
        UpdatedAt            = DateTimeOffset.UtcNow
    };

    public void SetCompleted(
        string? enhancedTitle, string? enhancedDescription, string? enhancedRequirements,
        int scoreBefore, int scoreAfter,
        string biasJson, string missingJson, string improvementsJson, string suggestedSkillsJson)
    {
        Status               = EnhancementStatus.Completed;
        EnhancedTitle        = enhancedTitle?.Trim();
        EnhancedDescription  = enhancedDescription?.Trim();
        EnhancedRequirements = enhancedRequirements?.Trim();
        ScoreBefore          = Math.Clamp(scoreBefore, 0, 100);
        ScoreAfter           = Math.Clamp(scoreAfter,  0, 100);
        BiasIssuesJson       = biasJson;
        MissingFieldsJson    = missingJson;
        ImprovementsJson     = improvementsJson;
        SuggestedSkillsJson  = suggestedSkillsJson;
        UpdatedAt            = DateTimeOffset.UtcNow;
    }

    public void SetFailed(string error)
    {
        Status       = EnhancementStatus.Failed;
        ErrorMessage = error;
        UpdatedAt    = DateTimeOffset.UtcNow;
    }

    public void Accept()
    {
        if (Status != EnhancementStatus.Completed)
            throw new InvalidOperationException("Cannot accept a non-completed enhancement.");
        if (IsAccepted)
            throw new InvalidOperationException("Enhancement already accepted.");
        IsAccepted = true;
        AcceptedAt = DateTimeOffset.UtcNow;
        UpdatedAt  = DateTimeOffset.UtcNow;
    }
}
