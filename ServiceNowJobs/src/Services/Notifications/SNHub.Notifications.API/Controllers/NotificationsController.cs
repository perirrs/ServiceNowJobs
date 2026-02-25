using Asp.Versioning;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SNHub.Notifications.Application.Commands.CreateNotification;
using SNHub.Notifications.Application.Commands.MarkAsRead;
using SNHub.Notifications.Application.DTOs;
using SNHub.Notifications.Application.Interfaces;
using SNHub.Notifications.Application.Queries.GetNotifications;
using SNHub.Notifications.Domain.Enums;

namespace SNHub.Notifications.API.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/notifications")]
[Authorize]
[Produces("application/json")]
public sealed class NotificationsController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ICurrentUserService _currentUser;

    public NotificationsController(IMediator mediator, ICurrentUserService currentUser)
    { _mediator = mediator; _currentUser = currentUser; }

    /// <summary>Get my notifications, newest first. Filter by unread only.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<NotificationDto>), 200)]
    public async Task<IActionResult> GetMine(
        [FromQuery] bool? unreadOnly,
        [FromQuery] int page     = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(
            new GetNotificationsQuery(_currentUser.UserId!.Value, unreadOnly, page, pageSize), ct);
        return Ok(result);
    }

    /// <summary>Get unread notification count (for badge display).</summary>
    [HttpGet("unread-count")]
    [ProducesResponseType(typeof(UnreadCountResponse), 200)]
    public async Task<IActionResult> GetUnreadCount(CancellationToken ct)
    {
        var count = await _mediator.Send(new GetUnreadCountQuery(_currentUser.UserId!.Value), ct);
        return Ok(new UnreadCountResponse(count));
    }

    /// <summary>Mark a specific notification as read.</summary>
    [HttpPut("{id:guid}/read")]
    [ProducesResponseType(204)]
    [ProducesResponseType(403)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> MarkRead(Guid id, CancellationToken ct)
    {
        await _mediator.Send(new MarkNotificationReadCommand(id, _currentUser.UserId!.Value), ct);
        return NoContent();
    }

    /// <summary>Mark all my notifications as read.</summary>
    [HttpPut("read-all")]
    [ProducesResponseType(204)]
    public async Task<IActionResult> MarkAllRead(CancellationToken ct)
    {
        await _mediator.Send(new MarkAllReadCommand(_currentUser.UserId!.Value), ct);
        return NoContent();
    }

    /// <summary>Create a notification. Internal service-to-service endpoint.</summary>
    [HttpPost("internal")]
    [Authorize(Roles = "SuperAdmin,ServiceAccount")]
    [ProducesResponseType(typeof(NotificationDto), 201)]
    [ProducesResponseType(400)]
    public async Task<IActionResult> Create([FromBody] CreateNotificationRequest req, CancellationToken ct)
    {
        var result = await _mediator.Send(
            new CreateNotificationCommand(req.UserId, req.Type, req.Title, req.Message,
                req.ActionUrl, req.MetadataJson), ct);
        return CreatedAtAction(nameof(GetMine), result);
    }
}

public sealed record CreateNotificationRequest(
    Guid UserId,
    NotificationType Type,
    string Title,
    string Message,
    string? ActionUrl    = null,
    string? MetadataJson = null);

public sealed record UnreadCountResponse(int Count);
