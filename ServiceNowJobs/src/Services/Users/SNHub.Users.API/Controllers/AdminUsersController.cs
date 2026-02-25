using Asp.Versioning;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SNHub.Users.Application.Commands.ReinstateUser;
using SNHub.Users.Application.Commands.SoftDeleteUser;
using SNHub.Users.Application.DTOs;
using SNHub.Users.Application.Interfaces;
using SNHub.Users.Application.Queries.GetAdminUsers;

namespace SNHub.Users.API.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/admin/users")]
[Authorize(Roles = "SuperAdmin,Admin")]
[Produces("application/json")]
public sealed class AdminUsersController : ControllerBase
{
    private readonly IMediator           _mediator;
    private readonly ICurrentUserService _currentUser;

    public AdminUsersController(IMediator mediator, ICurrentUserService currentUser)
    { _mediator = mediator; _currentUser = currentUser; }

    /// <summary>Paged list of all users. Filter by name/email or deletion status.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<AdminUserDto>), 200)]
    public async Task<IActionResult> GetUsers(
        [FromQuery] string? search,
        [FromQuery] bool?   isDeleted,
        [FromQuery] int     page     = 1,
        [FromQuery] int     pageSize = 20,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(
            new GetAdminUsersQuery(search, isDeleted, page, pageSize), ct);
        return Ok(result);
    }

    /// <summary>Get a single user by their userId (not profile id).</summary>
    [HttpGet("{userId:guid}")]
    [ProducesResponseType(typeof(AdminUserDto), 200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> GetUser(Guid userId, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetAdminUserByIdQuery(userId), ct);
        return result is null ? NotFound() : Ok(result);
    }

    /// <summary>Soft-delete a user.</summary>
    [HttpDelete("{userId:guid}")]
    [ProducesResponseType(204)]
    [ProducesResponseType(404)]
    [ProducesResponseType(409)]
    public async Task<IActionResult> DeleteUser(Guid userId, CancellationToken ct)
    {
        await _mediator.Send(
            new SoftDeleteUserCommand(userId, _currentUser.UserId!.Value), ct);
        return NoContent();
    }

    /// <summary>Reinstate a previously soft-deleted user.</summary>
    [HttpPost("{userId:guid}/reinstate")]
    [ProducesResponseType(204)]
    [ProducesResponseType(404)]
    [ProducesResponseType(409)]
    public async Task<IActionResult> ReinstateUser(Guid userId, CancellationToken ct)
    {
        await _mediator.Send(
            new ReinstateUserCommand(userId, _currentUser.UserId!.Value), ct);
        return NoContent();
    }
}
