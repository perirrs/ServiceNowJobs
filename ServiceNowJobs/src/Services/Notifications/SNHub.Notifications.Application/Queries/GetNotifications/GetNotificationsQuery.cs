using MediatR;
using SNHub.Notifications.Application.Commands.CreateNotification;
using SNHub.Notifications.Application.DTOs;
using SNHub.Notifications.Application.Interfaces;
namespace SNHub.Notifications.Application.Queries.GetNotifications;
public sealed record GetNotificationsQuery(Guid UserId, bool? UnreadOnly, int Page, int PageSize) : IRequest<PagedResult<NotificationDto>>;
public sealed class GetNotificationsQueryHandler : IRequestHandler<GetNotificationsQuery, PagedResult<NotificationDto>>
{
    private readonly INotificationRepository _repo;
    public GetNotificationsQueryHandler(INotificationRepository repo) => _repo = repo;
    public async Task<PagedResult<NotificationDto>> Handle(GetNotificationsQuery req, CancellationToken ct)
    {
        var (items, total, unread) = await _repo.GetByUserAsync(req.UserId, req.UnreadOnly, req.Page, req.PageSize, ct);
        return new PagedResult<NotificationDto>(items.Select(CreateNotificationCommandHandler.ToDto), total, req.Page, req.PageSize, unread);
    }
}
