namespace SNHub.Notifications.IntegrationTests.Models;

public sealed class NotificationResponse
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? ActionUrl { get; set; }
    public bool IsRead { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ReadAt { get; set; }
}

public sealed class PagedNotificationResponse
{
    public List<NotificationResponse> Items { get; set; } = [];
    public int Total { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int UnreadCount { get; set; }
    public bool HasNextPage { get; set; }
    public bool HasPreviousPage { get; set; }
    public int TotalPages { get; set; }
}

public sealed class UnreadCountResponse
{
    public int Count { get; set; }
}

public sealed class CreateNotificationRequest
{
    public Guid UserId { get; set; }
    public int Type { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? ActionUrl { get; set; }
    public string? MetadataJson { get; set; }
}
