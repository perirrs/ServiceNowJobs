using Asp.Versioning;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SNHub.Applications.Application.Commands.ApplyToJob;
using SNHub.Applications.Application.Commands.UpdateStatus;
using SNHub.Applications.Application.Commands.Withdraw;
using SNHub.Applications.Application.DTOs;
using SNHub.Applications.Application.Queries.GetApplications;
using SNHub.Applications.Domain.Enums;
using System.Security.Claims;

namespace SNHub.Applications.API.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[Authorize]
public sealed class ApplicationsController(IMediator mediator) : ControllerBase
{
    private Guid CurrentUserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    /// <summary>Apply to a job</summary>
    [HttpPost("jobs/{jobId:guid}/apply")]
    [ProducesResponseType(typeof(ApplicationDto), 201)]
    public async Task<IActionResult> Apply(Guid jobId, [FromBody] ApplyRequest req, CancellationToken ct)
    {
        var result = await mediator.Send(new ApplyToJobCommand(jobId, CurrentUserId, req.CoverLetter, req.CvUrl), ct);
        return CreatedAtAction(nameof(GetMine), new { }, result);
    }

    /// <summary>Get my applications (candidate)</summary>
    [HttpGet("mine")]
    [ProducesResponseType(typeof(PagedResult<ApplicationDto>), 200)]
    public async Task<IActionResult> GetMine([FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        var result = await mediator.Send(new GetMyApplicationsQuery(CurrentUserId, page, pageSize), ct);
        return Ok(result);
    }

    /// <summary>Get applications for a job (employer)</summary>
    [HttpGet("jobs/{jobId:guid}")]
    [Authorize(Roles = "Employer,HiringManager,SuperAdmin,Moderator")]
    [ProducesResponseType(typeof(PagedResult<ApplicationDto>), 200)]
    public async Task<IActionResult> GetForJob(Guid jobId, [FromQuery] string? status,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        var result = await mediator.Send(new GetJobApplicationsQuery(jobId, status, page, pageSize), ct);
        return Ok(result);
    }

    /// <summary>Update application status (employer)</summary>
    [HttpPut("{id:guid}/status")]
    [Authorize(Roles = "Employer,HiringManager,SuperAdmin")]
    [ProducesResponseType(typeof(ApplicationDto), 200)]
    public async Task<IActionResult> UpdateStatus(Guid id, [FromBody] UpdateStatusRequest req, CancellationToken ct)
    {
        var result = await mediator.Send(new UpdateApplicationStatusCommand(id, req.Status, req.Notes, req.RejectionReason), ct);
        return Ok(result);
    }

    /// <summary>Withdraw application (candidate)</summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(204)]
    public async Task<IActionResult> Withdraw(Guid id, CancellationToken ct)
    {
        await mediator.Send(new WithdrawApplicationCommand(id, CurrentUserId), ct);
        return NoContent();
    }
}

public sealed record ApplyRequest(string? CoverLetter, string? CvUrl);
public sealed record UpdateStatusRequest(ApplicationStatus Status, string? Notes, string? RejectionReason);
