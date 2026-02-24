using Asp.Versioning;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SNHub.Auth.Application.Commands.ReinstateUser;
using SNHub.Auth.Application.Commands.SuspendUser;
using SNHub.Auth.Application.Commands.UpdateUserRoles;
using SNHub.Auth.Application.Queries.GetUserById;
using SNHub.Auth.Application.Queries.GetUsers;
using SNHub.Auth.Domain.Enums;
using SNHub.Shared.Models;

namespace SNHub.Auth.API.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/admin/users")]
public sealed class AdminUsersController : ControllerBase
{
    private readonly IMediator _mediator;

    public AdminUsersController(IMediator mediator) => _mediator = mediator;

    // GET /api/v1/admin/users?page=1&pageSize=20&search=john&isActive=true&role=5
    [HttpGet]
    [Authorize(Policy = "ModeratorOrAdmin")]
    [ProducesResponseType(typeof(ApiResponse<object>), 200)]
    [ProducesResponseType(401), ProducesResponseType(403)]
    public async Task<IActionResult> GetUsers(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null,
        [FromQuery] bool? isActive = null,
        [FromQuery] UserRole? role = null,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(
            new GetUsersQuery(page, pageSize, search, isActive, role), ct);

        return Ok(ApiResponse<object>.Ok(result, $"{result.TotalCount} users found."));
    }

    // GET /api/v1/admin/users/{id}
    [HttpGet("{id:guid}")]
    [Authorize(Policy = "ModeratorOrAdmin")]
    [ProducesResponseType(typeof(ApiResponse<object>), 200)]
    [ProducesResponseType(401), ProducesResponseType(403), ProducesResponseType(404)]
    public async Task<IActionResult> GetUser(Guid id, CancellationToken ct)
    {
        var user = await _mediator.Send(new GetUserByIdQuery(id), ct);
        return Ok(ApiResponse<object>.Ok(user));
    }

    // PUT /api/v1/admin/users/{id}/suspend
    [HttpPut("{id:guid}/suspend")]
    [Authorize(Policy = "AdminOnly")]
    [ProducesResponseType(204)]
    [ProducesResponseType(400), ProducesResponseType(401), ProducesResponseType(403), ProducesResponseType(404)]
    public async Task<IActionResult> SuspendUser(
        Guid id,
        [FromBody] SuspendUserRequest request,
        CancellationToken ct)
    {
        await _mediator.Send(new SuspendUserCommand(id, request.Reason), ct);
        return NoContent();
    }

    // PUT /api/v1/admin/users/{id}/reinstate
    [HttpPut("{id:guid}/reinstate")]
    [Authorize(Policy = "AdminOnly")]
    [ProducesResponseType(204)]
    [ProducesResponseType(400), ProducesResponseType(401), ProducesResponseType(403), ProducesResponseType(404)]
    public async Task<IActionResult> ReinstateUser(Guid id, CancellationToken ct)
    {
        await _mediator.Send(new ReinstateUserCommand(id), ct);
        return NoContent();
    }

    // PUT /api/v1/admin/users/{id}/roles
    [HttpPut("{id:guid}/roles")]
    [Authorize(Policy = "AdminOnly")]
    [ProducesResponseType(204)]
    [ProducesResponseType(400), ProducesResponseType(401), ProducesResponseType(403), ProducesResponseType(404)]
    public async Task<IActionResult> UpdateUserRoles(
        Guid id,
        [FromBody] UpdateUserRolesRequest request,
        CancellationToken ct)
    {
        await _mediator.Send(new UpdateUserRolesCommand(id, request.Roles), ct);
        return NoContent();
    }
}

public sealed record SuspendUserRequest(string Reason);

public sealed record UpdateUserRolesRequest(IReadOnlyList<UserRole> Roles);
