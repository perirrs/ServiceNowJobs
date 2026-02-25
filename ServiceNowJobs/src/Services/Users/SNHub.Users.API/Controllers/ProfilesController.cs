using Asp.Versioning;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SNHub.Users.Application.Commands.UpdateProfile;
using SNHub.Users.Application.Commands.UploadProfilePicture;
using SNHub.Users.Application.DTOs;
using SNHub.Users.Application.Interfaces;
using SNHub.Users.Application.Queries.GetProfile;

namespace SNHub.Users.API.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/users")]
[Authorize]
[Produces("application/json")]
public sealed class ProfilesController : ControllerBase
{
    private readonly IMediator           _mediator;
    private readonly ICurrentUserService _currentUser;

    public ProfilesController(IMediator mediator, ICurrentUserService currentUser)
    { _mediator = mediator; _currentUser = currentUser; }

    /// <summary>Get my own profile.</summary>
    [HttpGet("me")]
    [ProducesResponseType(typeof(UserProfileDto), 200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> GetMyProfile(CancellationToken ct)
    {
        var result = await _mediator.Send(new GetProfileQuery(_currentUser.UserId!.Value), ct);
        return result is null ? NotFound(new { message = "Profile not found." }) : Ok(result);
    }

    /// <summary>Update my own profile.</summary>
    [HttpPut("me")]
    [ProducesResponseType(typeof(UserProfileDto), 200)]
    [ProducesResponseType(400)]
    public async Task<IActionResult> UpdateMyProfile([FromBody] UpdateProfileRequest req, CancellationToken ct)
    {
        var cmd    = req.ToCommand(_currentUser.UserId!.Value);
        var result = await _mediator.Send(cmd, ct);
        return Ok(result);
    }

    /// <summary>Upload my profile picture (JPEG/PNG/WebP, max 5 MB).</summary>
    [HttpPost("me/picture")]
    [RequestSizeLimit(5 * 1024 * 1024)]
    [ProducesResponseType(typeof(UploadedFileResponse), 200)]
    [ProducesResponseType(400)]
    public async Task<IActionResult> UploadProfilePicture(IFormFile file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { message = "No file provided." });

        await using var stream = file.OpenReadStream();
        var url = await _mediator.Send(
            new UploadProfilePictureCommand(
                _currentUser.UserId!.Value, stream, file.FileName,
                file.ContentType, file.Length), ct);

        return Ok(new UploadedFileResponse(url));
    }

    /// <summary>Get a public profile by user ID.</summary>
    [HttpGet("{userId:guid}")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(UserProfileDto), 200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> GetProfile(Guid userId, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetProfileQuery(userId), ct);
        if (result is null) return NotFound();

        // Private profiles visible only to the owner
        var callerId = _currentUser.UserId;
        if (!result.IsPublic && callerId != userId)
            return NotFound();

        return Ok(result);
    }
}

// ── Request body record (keeps controller clean) ──────────────────────────────
public sealed record UpdateProfileRequest(
    string? FirstName,
    string? LastName,
    string? PhoneNumber,
    string? Headline,
    string? Bio,
    string? Location,
    string? LinkedInUrl,
    string? GitHubUrl,
    string? WebsiteUrl,
    int     YearsOfExperience,
    bool    IsPublic,
    string? Country,
    string? TimeZone)
{
    public UpdateProfileCommand ToCommand(Guid userId) =>
        new(userId, FirstName, LastName, PhoneNumber, Headline, Bio, Location,
            LinkedInUrl, GitHubUrl, WebsiteUrl, YearsOfExperience, IsPublic, Country, TimeZone);
}
