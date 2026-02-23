namespace SNHub.Applications.Application.DTOs;

public sealed record ApplicationDto(
    Guid Id, Guid JobId, Guid CandidateId,
    string Status, string? CoverLetter, string? CvUrl,
    string? EmployerNotes, string? RejectionReason,
    DateTimeOffset AppliedAt, DateTimeOffset UpdatedAt,
    DateTimeOffset? StatusChangedAt);

public sealed record PagedResult<T>(IEnumerable<T> Items, int Total, int Page, int PageSize)
{
    public int TotalPages => (int)Math.Ceiling((double)Total / PageSize);
}
