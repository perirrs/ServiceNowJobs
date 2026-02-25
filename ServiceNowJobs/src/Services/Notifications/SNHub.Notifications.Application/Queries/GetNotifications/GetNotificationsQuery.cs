using FluentValidation;
using MediatR;
using SNHub.Notifications.Application.Commands.CreateNotification;
using SNHub.Notifications.Application.DTOs;
using SNHub.Notifications.Application.Interfaces;

namespace SNHub.Notifications.Application.Queries.GetNotifications;

public sealed record GetNotificationsQuery(
    Guid UserId,
    bool? UnreadOnly,
    int Page     = 1,
    int PageSize = 20) : IRequest<PagedResult<NotificationDto>>;

public sealed class GetNotificationsQueryValidator : AbstractValidator<GetNotificationsQuery>
{
    public GetNotificationsQueryValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
    }
}

public sealed class GetNotificationsQueryHandler
    : IRequestHandler<GetNotificationsQuery, PagedResult<NotificationDto>>
{
    private readonly INotificationRepository _repo;
    public GetNotificationsQueryHandler(INotificationRepository repo) => _repo = repo;

    public async Task<PagedResult<NotificationDto>> Handle(GetNotificationsQuery req, CancellationToken ct)
    {
        var (items, total, unread) = await _repo.GetByUserAsync(
            req.UserId, req.UnreadOnly, req.Page, req.PageSize, ct);

        return PagedResult<NotificationDto>.Create(
            items.Select(NotificationMapper.ToDto), total, req.Page, req.PageSize, unread);
    }
}

// ── Get unread count (lightweight badge endpoint) ─────────────────────────────

public sealed record GetUnreadCountQuery(Guid UserId) : IRequest<int>;

public sealed class GetUnreadCountQueryHandler : IRequestHandler<GetUnreadCountQuery, int>
{
    private readonly INotificationRepository _repo;
    public GetUnreadCountQueryHandler(INotificationRepository repo) => _repo = repo;

    public async Task<int> Handle(GetUnreadCountQuery req, CancellationToken ct)
        => await _repo.GetUnreadCountAsync(req.UserId, ct);
}
