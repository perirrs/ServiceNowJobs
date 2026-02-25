using SNHub.Notifications.Domain.Enums;
namespace SNHub.Notifications.Domain.Entities;
public sealed class Notification
{
    private Notification() { }
    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public NotificationType Type { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string Message { get; private set; } = string.Empty;
    public string? ActionUrl { get; private set; }
    public string? MetadataJson { get; private set; }
    public bool IsRead { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ReadAt { get; private set; }

    public static Notification Create(Guid userId, NotificationType type, string title, string message, string? actionUrl = null, string? metadataJson = null) =>
        new() { Id = Guid.NewGuid(), UserId = userId, Type = type, Title = title, Message = message, ActionUrl = actionUrl, MetadataJson = metadataJson, IsRead = false, CreatedAt = DateTimeOffset.UtcNow };

    public void MarkAsRead()
    {
        if (IsRead) return;          // idempotent — ReadAt stays frozen after first call
        IsRead = true;
        ReadAt = DateTimeOffset.UtcNow;
    }
}
