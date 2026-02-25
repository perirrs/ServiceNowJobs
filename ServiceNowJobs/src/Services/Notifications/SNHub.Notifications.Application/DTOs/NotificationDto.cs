namespace SNHub.Notifications.Application.DTOs;

public sealed record NotificationDto(
    Guid Id,
    Guid UserId,
    string Type,
    string Title,
    string Message,
    string? ActionUrl,
    bool IsRead,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ReadAt);

public sealed record PagedResult<T>(
    IEnumerable<T> Items,
    int Total,
    int Page,
    int PageSize,
    int UnreadCount,
    bool HasNextPage,
    bool HasPreviousPage)
{
    public int TotalPages => Total == 0 ? 0 : (int)Math.Ceiling((double)Total / PageSize);

    public static PagedResult<T> Create(
        IEnumerable<T> items, int total, int page, int pageSize, int unreadCount)
    {
        int totalPages = total == 0 ? 0 : (int)Math.Ceiling((double)total / pageSize);
        return new PagedResult<T>(items, total, page, pageSize, unreadCount,
            HasNextPage: page < totalPages,
            HasPreviousPage: page > 1);
    }
}
