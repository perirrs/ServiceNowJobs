namespace SNHub.Applications.Application.DTOs;

public sealed record ApplicationDto(
    Guid Id,
    Guid JobId,
    Guid CandidateId,
    string Status,
    string? CoverLetter,
    string? CvUrl,
    string? EmployerNotes,
    string? RejectionReason,
    DateTimeOffset AppliedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? StatusChangedAt);

public sealed record PagedResult<T>(
    IEnumerable<T> Items,
    int Total,
    int Page,
    int PageSize,
    bool HasNextPage,
    bool HasPreviousPage)
{
    public int TotalPages => Total == 0 ? 0 : (int)Math.Ceiling((double)Total / PageSize);

    public static PagedResult<T> Create(IEnumerable<T> items, int total, int page, int pageSize)
    {
        int totalPages = total == 0 ? 0 : (int)Math.Ceiling((double)total / pageSize);
        return new PagedResult<T>(items, total, page, pageSize,
            HasNextPage:     page < totalPages,
            HasPreviousPage: page > 1);
    }
}
