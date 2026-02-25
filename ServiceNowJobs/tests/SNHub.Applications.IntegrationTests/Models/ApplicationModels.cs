namespace SNHub.Applications.IntegrationTests.Models;

// ── Requests ──────────────────────────────────────────────────────────────────

public sealed record ApplyRequest(string? CoverLetter, string? CvUrl);
public sealed record UpdateStatusRequest(int Status, string? Notes, string? RejectionReason);

// ── Responses (classes with setters for reliable STJ deserialization) ─────────

public sealed class ApplicationResponse
{
    public Guid Id { get; set; }
    public Guid JobId { get; set; }
    public Guid CandidateId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? CoverLetter { get; set; }
    public string? CvUrl { get; set; }
    public string? EmployerNotes { get; set; }
    public string? RejectionReason { get; set; }
    public DateTimeOffset AppliedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? StatusChangedAt { get; set; }
}

public sealed class PagedApplicationResponse
{
    public List<ApplicationResponse> Items { get; set; } = [];
    public int Total { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalPages { get; set; }
    public bool HasNextPage { get; set; }
    public bool HasPreviousPage { get; set; }
}
