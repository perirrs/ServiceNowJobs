using Asp.Versioning;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SNHub.Auth.Application.Commands.UpdateProfile;
using SNHub.Auth.Application.Commands.UploadProfilePicture;
using SNHub.Auth.Application.Queries.GetCurrentUser;
using SNHub.Shared.Models;

namespace SNHub.Auth.API.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/users")]
[Authorize]
public sealed class UsersController : ControllerBase
{
    private readonly IMediator _mediator;

    public UsersController(IMediator mediator) => _mediator = mediator;

    // GET /api/v1/users/me
    [HttpGet("me")]
    [ProducesResponseType(typeof(ApiResponse<object>), 200)]
    [ProducesResponseType(401)]
    public async Task<IActionResult> GetMe(CancellationToken ct)
    {
        var profile = await _mediator.Send(new GetCurrentUserQuery(), ct);
        return Ok(ApiResponse<object>.Ok(profile, "Profile retrieved."));
    }

    // PUT /api/v1/users/me
    [HttpPut("me")]
    [ProducesResponseType(typeof(ApiResponse<object>), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    public async Task<IActionResult> UpdateMe(
        [FromBody] UpdateProfileRequest request,
        CancellationToken ct)
    {
        var updated = await _mediator.Send(
            new UpdateProfileCommand(
                request.FirstName, request.LastName,
                request.PhoneNumber, request.Country, request.TimeZone),
            ct);

        return Ok(ApiResponse<object>.Ok(updated, "Profile updated."));
    }

    // POST /api/v1/users/me/profile-picture
    [HttpPost("me/profile-picture")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(ApiResponse<object>), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    public async Task<IActionResult> UploadProfilePicture(
        IFormFile file,
        CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { message = "No file provided." });

        using var ms = new MemoryStream();
        await file.CopyToAsync(ms, ct);

        var url = await _mediator.Send(
            new UploadProfilePictureCommand(
                ms.ToArray(), file.FileName, file.ContentType),
            ct);

        return Ok(ApiResponse<object>.Ok(new { profilePictureUrl = url }, "Profile picture updated."));
    }
}

public sealed record UpdateProfileRequest(
    string FirstName,
    string LastName,
    string? PhoneNumber = null,
    string? Country = null,
    string? TimeZone = null);
