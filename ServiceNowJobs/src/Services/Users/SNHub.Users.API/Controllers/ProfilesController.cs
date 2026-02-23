using Asp.Versioning;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SNHub.Users.Application.Commands.UpdateProfile;
using SNHub.Users.Application.Commands.UploadProfilePicture;
using SNHub.Users.Application.Queries.GetProfile;
using System.Security.Claims;

namespace SNHub.Users.API.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/profiles")]
[Authorize]
public sealed class ProfilesController : ControllerBase
{
    private readonly IMediator _mediator;
    public ProfilesController(IMediator mediator) { _mediator = mediator; }

    private Guid CurrentUserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? User.FindFirstValue("sub")
        ?? throw new UnauthorizedAccessException());

    [HttpGet("me")]
    public async Task<IActionResult> GetMyProfile(CancellationToken ct)
    {
        var result = await _mediator.Send(new GetProfileQuery(CurrentUserId), ct);
        return result is null ? NotFound(new { message = "Profile not found." }) : Ok(result);
    }

    [HttpGet("{userId:guid}")]
    [AllowAnonymous]
    public async Task<IActionResult> GetProfile(Guid userId, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetProfileQuery(userId), ct);
        if (result is null) return NotFound();
        if (!result.IsPublic && CurrentUserId != userId) return NotFound();
        return Ok(result);
    }

    [HttpPut("me")]
    public async Task<IActionResult> UpdateMyProfile([FromBody] UpdateProfileCommand cmd, CancellationToken ct)
    {
        var command = cmd with { UserId = CurrentUserId };
        var result = await _mediator.Send(command, ct);
        return Ok(result);
    }

    [HttpPost("me/picture")]
    [RequestSizeLimit(5 * 1024 * 1024)]
    public async Task<IActionResult> UploadProfilePicture(IFormFile file, CancellationToken ct)
    {
        if (file is null || file.Length == 0) return BadRequest(new { message = "No file provided." });
        var allowed = new[] { "image/jpeg", "image/png", "image/webp" };
        if (!allowed.Contains(file.ContentType)) return BadRequest(new { message = "Only JPEG, PNG, and WebP images are allowed." });

        await using var stream = file.OpenReadStream();
        var url = await _mediator.Send(new UploadProfilePictureCommand(CurrentUserId, stream, file.FileName, file.ContentType), ct);
        return Ok(new { url });
    }
}
