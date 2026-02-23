namespace SNHub.Notifications.Application.DTOs;
public sealed record NotificationDto(Guid Id, Guid UserId, string Type, string Title, string Message, string? ActionUrl, bool IsRead, DateTimeOffset CreatedAt, DateTimeOffset? ReadAt);
public sealed record PagedResult<T>(IEnumerable<T> Items, int Total, int Page, int PageSize, int UnreadCount);
