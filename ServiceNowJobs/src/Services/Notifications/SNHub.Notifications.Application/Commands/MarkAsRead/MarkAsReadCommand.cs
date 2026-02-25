using FluentValidation;
using MediatR;
using SNHub.Notifications.Application.Interfaces;
using SNHub.Notifications.Domain.Exceptions;

namespace SNHub.Notifications.Application.Commands.MarkAsRead;

// ── Mark single notification read ─────────────────────────────────────────────

public sealed record MarkNotificationReadCommand(Guid Id, Guid RequesterId) : IRequest<Unit>;

public sealed class MarkNotificationReadCommandValidator
    : AbstractValidator<MarkNotificationReadCommand>
{
    public MarkNotificationReadCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.RequesterId).NotEmpty();
    }
}

public sealed class MarkNotificationReadCommandHandler
    : IRequestHandler<MarkNotificationReadCommand, Unit>
{
    private readonly INotificationRepository _repo;
    private readonly IUnitOfWork _uow;

    public MarkNotificationReadCommandHandler(INotificationRepository repo, IUnitOfWork uow)
    { _repo = repo; _uow = uow; }

    public async Task<Unit> Handle(MarkNotificationReadCommand req, CancellationToken ct)
    {
        var n = await _repo.GetByIdAsync(req.Id, ct)
            ?? throw new NotificationNotFoundException(req.Id);

        if (n.UserId != req.RequesterId)
            throw new NotificationAccessDeniedException();

        if (!n.IsRead)
        {
            n.MarkAsRead();
            await _uow.SaveChangesAsync(ct);
        }

        return Unit.Value;
    }
}

// ── Mark all notifications read ───────────────────────────────────────────────

public sealed record MarkAllReadCommand(Guid UserId) : IRequest<Unit>;

public sealed class MarkAllReadCommandValidator : AbstractValidator<MarkAllReadCommand>
{
    public MarkAllReadCommandValidator() => RuleFor(x => x.UserId).NotEmpty();
}

public sealed class MarkAllReadCommandHandler : IRequestHandler<MarkAllReadCommand, Unit>
{
    private readonly INotificationRepository _repo;
    private readonly IUnitOfWork _uow;

    public MarkAllReadCommandHandler(INotificationRepository repo, IUnitOfWork uow)
    { _repo = repo; _uow = uow; }

    public async Task<Unit> Handle(MarkAllReadCommand req, CancellationToken ct)
    {
        await _repo.MarkAllAsReadAsync(req.UserId, ct);
        await _uow.SaveChangesAsync(ct);
        return Unit.Value;
    }
}
