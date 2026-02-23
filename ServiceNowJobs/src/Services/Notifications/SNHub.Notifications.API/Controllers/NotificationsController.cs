using Asp.Versioning;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SNHub.Notifications.Application.Commands.CreateNotification;
using SNHub.Notifications.Application.Commands.MarkAsRead;
using SNHub.Notifications.Application.DTOs;
using SNHub.Notifications.Application.Queries.GetNotifications;
using SNHub.Notifications.Domain.Enums;
using System.Security.Claims;

namespace SNHub.Notifications.API.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[Authorize]
public sealed class NotificationsController(IMediator mediator) : ControllerBase
{
    private Guid CurrentUserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    /// <summary>Get my notifications</summary>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<NotificationDto>), 200)]
    public async Task<IActionResult> GetMine([FromQuery] bool? unreadOnly, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        var result = await mediator.Send(new GetNotificationsQuery(CurrentUserId, unreadOnly, page, pageSize), ct);
        return Ok(result);
    }

    /// <summary>Mark notification as read</summary>
    [HttpPut("{id:guid}/read")]
    [ProducesResponseType(204)]
    public async Task<IActionResult> MarkRead(Guid id, CancellationToken ct)
    {
        await mediator.Send(new MarkNotificationReadCommand(id), ct);
        return NoContent();
    }

    /// <summary>Mark all as read</summary>
    [HttpPut("read-all")]
    [ProducesResponseType(204)]
    public async Task<IActionResult> MarkAllRead(CancellationToken ct)
    {
        await mediator.Send(new MarkAllReadCommand(CurrentUserId), ct);
        return NoContent();
    }

    /// <summary>Create notification (internal service-to-service)</summary>
    [HttpPost("internal")]
    [Authorize(Roles = "SuperAdmin")]
    [ProducesResponseType(typeof(NotificationDto), 201)]
    public async Task<IActionResult> Create([FromBody] CreateNotificationRequest req, CancellationToken ct)
    {
        var result = await mediator.Send(new CreateNotificationCommand(req.UserId, req.Type, req.Title, req.Message, req.ActionUrl, req.MetadataJson), ct);
        return CreatedAtAction(nameof(GetMine), result);
    }
}

public sealed record CreateNotificationRequest(Guid UserId, NotificationType Type, string Title, string Message, string? ActionUrl, string? MetadataJson);
