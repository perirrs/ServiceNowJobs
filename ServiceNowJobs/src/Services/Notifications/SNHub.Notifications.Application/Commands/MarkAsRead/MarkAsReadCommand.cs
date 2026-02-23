using MediatR;
using SNHub.Notifications.Application.Interfaces;
namespace SNHub.Notifications.Application.Commands.MarkAsRead;
public sealed record MarkNotificationReadCommand(Guid Id) : IRequest<Unit>;
public sealed record MarkAllReadCommand(Guid UserId) : IRequest<Unit>;
public sealed class MarkNotificationReadCommandHandler : IRequestHandler<MarkNotificationReadCommand, Unit>
{
    private readonly INotificationRepository _repo; private readonly IUnitOfWork _uow;
    public MarkNotificationReadCommandHandler(INotificationRepository repo, IUnitOfWork uow) => (_repo, _uow) = (repo, uow);
    public async Task<Unit> Handle(MarkNotificationReadCommand req, CancellationToken ct)
    { var n = await _repo.GetByIdAsync(req.Id, ct); if (n != null) { n.MarkAsRead(); await _uow.SaveChangesAsync(ct); } return Unit.Value; }
}
public sealed class MarkAllReadCommandHandler : IRequestHandler<MarkAllReadCommand, Unit>
{
    private readonly INotificationRepository _repo; private readonly IUnitOfWork _uow;
    public MarkAllReadCommandHandler(INotificationRepository repo, IUnitOfWork uow) => (_repo, _uow) = (repo, uow);
    public async Task<Unit> Handle(MarkAllReadCommand req, CancellationToken ct)
    { await _repo.MarkAllAsReadAsync(req.UserId, ct); await _uow.SaveChangesAsync(ct); return Unit.Value; }
}
