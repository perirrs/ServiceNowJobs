using SNHub.Notifications.Domain.Entities;
namespace SNHub.Notifications.Application.Interfaces;
public interface INotificationRepository
{
    Task<Notification?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task AddAsync(Notification notification, CancellationToken ct = default);
    Task<(IEnumerable<Notification> Items, int Total, int UnreadCount)> GetByUserAsync(Guid userId, bool? unreadOnly, int page, int pageSize, CancellationToken ct = default);
    Task MarkAllAsReadAsync(Guid userId, CancellationToken ct = default);
}
public interface IUnitOfWork { Task<int> SaveChangesAsync(CancellationToken ct = default); }
