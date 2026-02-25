using Microsoft.EntityFrameworkCore;
using SNHub.Notifications.Application.Interfaces;
using SNHub.Notifications.Domain.Entities;

namespace SNHub.Notifications.Infrastructure.Persistence.Repositories;

public sealed class NotificationRepository : INotificationRepository
{
    private readonly NotificationsDbContext _db;
    public NotificationRepository(NotificationsDbContext db) => _db = db;

    public Task<Notification?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => _db.Notifications.FindAsync([id], ct).AsTask();

    public async Task AddAsync(Notification n, CancellationToken ct = default)
        => await _db.Notifications.AddAsync(n, ct);

    public async Task<(IEnumerable<Notification> Items, int Total, int UnreadCount)> GetByUserAsync(
        Guid userId, bool? unreadOnly, int page, int pageSize, CancellationToken ct = default)
    {
        var q = _db.Notifications.AsNoTracking().Where(n => n.UserId == userId);
        if (unreadOnly == true) q = q.Where(n => !n.IsRead);

        var total  = await q.CountAsync(ct);
        var unread = await _db.Notifications.AsNoTracking()
                        .CountAsync(n => n.UserId == userId && !n.IsRead, ct);
        var items  = await q
                        .OrderByDescending(n => n.CreatedAt)
                        .Skip((page - 1) * pageSize).Take(pageSize)
                        .ToListAsync(ct);

        return (items, total, unread);
    }

    public async Task MarkAllAsReadAsync(Guid userId, CancellationToken ct = default)
        => await _db.Notifications
            .Where(n => n.UserId == userId && !n.IsRead)
            .ExecuteUpdateAsync(s => s
                .SetProperty(n => n.IsRead, true)
                .SetProperty(n => n.ReadAt, DateTimeOffset.UtcNow), ct);

    public Task<int> GetUnreadCountAsync(Guid userId, CancellationToken ct = default)
        => _db.Notifications.AsNoTracking()
              .CountAsync(n => n.UserId == userId && !n.IsRead, ct);
}
