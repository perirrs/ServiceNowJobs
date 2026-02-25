using SNHub.Notifications.Application.DTOs;
using SNHub.Notifications.Domain.Entities;

namespace SNHub.Notifications.Application.Commands.CreateNotification;

public static class NotificationMapper
{
    public static NotificationDto ToDto(Notification n) => new(
        Id:        n.Id,
        UserId:    n.UserId,
        Type:      n.Type.ToString(),
        Title:     n.Title,
        Message:   n.Message,
        ActionUrl: n.ActionUrl,
        IsRead:    n.IsRead,
        CreatedAt: n.CreatedAt,
        ReadAt:    n.ReadAt);
}
