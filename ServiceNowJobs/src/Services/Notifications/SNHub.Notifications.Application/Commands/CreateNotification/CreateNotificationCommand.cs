using MediatR;
using Microsoft.Extensions.Logging;
using SNHub.Notifications.Application.DTOs;
using SNHub.Notifications.Application.Interfaces;
using SNHub.Notifications.Domain.Entities;
using SNHub.Notifications.Domain.Enums;
namespace SNHub.Notifications.Application.Commands.CreateNotification;
public sealed record CreateNotificationCommand(Guid UserId, NotificationType Type, string Title, string Message, string? ActionUrl = null, string? MetadataJson = null) : IRequest<NotificationDto>;
public sealed class CreateNotificationCommandHandler : IRequestHandler<CreateNotificationCommand, NotificationDto>
{
    private readonly INotificationRepository _repo; private readonly IUnitOfWork _uow; private readonly ILogger<CreateNotificationCommandHandler> _logger;
    public CreateNotificationCommandHandler(INotificationRepository repo, IUnitOfWork uow, ILogger<CreateNotificationCommandHandler> logger) => (_repo, _uow, _logger) = (repo, uow, logger);
    public async Task<NotificationDto> Handle(CreateNotificationCommand req, CancellationToken ct)
    {
        var n = Notification.Create(req.UserId, req.Type, req.Title, req.Message, req.ActionUrl, req.MetadataJson);
        await _repo.AddAsync(n, ct);
        await _uow.SaveChangesAsync(ct);
        _logger.LogInformation("Notification {Id} for user {UserId}", n.Id, n.UserId);
        return ToDto(n);
    }
    public static NotificationDto ToDto(Notification n) => new(n.Id, n.UserId, n.Type.ToString(), n.Title, n.Message, n.ActionUrl, n.IsRead, n.CreatedAt, n.ReadAt);
}
