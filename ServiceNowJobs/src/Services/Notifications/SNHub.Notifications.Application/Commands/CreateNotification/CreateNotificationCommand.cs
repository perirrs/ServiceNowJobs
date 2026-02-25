using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using SNHub.Notifications.Application.DTOs;
using SNHub.Notifications.Application.Interfaces;
using SNHub.Notifications.Domain.Entities;
using SNHub.Notifications.Domain.Enums;

namespace SNHub.Notifications.Application.Commands.CreateNotification;

public sealed record CreateNotificationCommand(
    Guid UserId,
    NotificationType Type,
    string Title,
    string Message,
    string? ActionUrl    = null,
    string? MetadataJson = null) : IRequest<NotificationDto>;

public sealed class CreateNotificationCommandValidator : AbstractValidator<CreateNotificationCommand>
{
    public CreateNotificationCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Message).NotEmpty().MaximumLength(1_000);
        RuleFor(x => x.Type).IsInEnum();
        RuleFor(x => x.ActionUrl)
            .MaximumLength(2_048)
            .Must(u => u is null || Uri.TryCreate(u, UriKind.Absolute, out _))
            .WithMessage("ActionUrl must be a valid absolute URL.")
            .When(x => x.ActionUrl is not null);
    }
}

public sealed class CreateNotificationCommandHandler
    : IRequestHandler<CreateNotificationCommand, NotificationDto>
{
    private readonly INotificationRepository _repo;
    private readonly IUnitOfWork _uow;
    private readonly ILogger<CreateNotificationCommandHandler> _logger;

    public CreateNotificationCommandHandler(
        INotificationRepository repo, IUnitOfWork uow,
        ILogger<CreateNotificationCommandHandler> logger)
    { _repo = repo; _uow = uow; _logger = logger; }

    public async Task<NotificationDto> Handle(CreateNotificationCommand req, CancellationToken ct)
    {
        var n = Notification.Create(req.UserId, req.Type, req.Title, req.Message,
                                    req.ActionUrl, req.MetadataJson);
        await _repo.AddAsync(n, ct);
        await _uow.SaveChangesAsync(ct);
        _logger.LogInformation("Notification {Id} ({Type}) created for user {UserId}",
            n.Id, req.Type, req.UserId);
        return NotificationMapper.ToDto(n);
    }
}
