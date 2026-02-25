using Asp.Versioning;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SNHub.Applications.Application.Commands.ApplyToJob;
using SNHub.Applications.Application.Commands.UpdateStatus;
using SNHub.Applications.Application.Commands.Withdraw;
using SNHub.Applications.Application.DTOs;
using SNHub.Applications.Application.Interfaces;
using SNHub.Applications.Application.Queries.GetApplications;
using SNHub.Applications.Domain.Enums;

namespace SNHub.Applications.API.Controllers;

/// <summary>Job applications — submit, track, manage.</summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/applications")]
[Authorize]
[Produces("application/json")]
public sealed class ApplicationsController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ICurrentUserService _currentUser;

    public ApplicationsController(IMediator mediator, ICurrentUserService currentUser)
    { _mediator = mediator; _currentUser = currentUser; }

    // ── Candidate — submit ───────────────────────────────────────────────────

    /// <summary>Apply to a job. Free plan: 5/month. Lite: 20/month. Pro/Enterprise: unlimited.</summary>
    [HttpPost("jobs/{jobId:guid}")]
    [Authorize(Roles = "Candidate,JobSeeker,SuperAdmin")]
    [ProducesResponseType(typeof(ApplicationDto), 201)]
    [ProducesResponseType(400)]
    [ProducesResponseType(402)] // Payment Required — subscription limit
    [ProducesResponseType(409)] // Conflict — duplicate application
    public async Task<IActionResult> Apply(Guid jobId, [FromBody] ApplyRequest req, CancellationToken ct)
    {
        var result = await _mediator.Send(
            new ApplyToJobCommand(jobId, _currentUser.UserId!.Value, req.CoverLetter, req.CvUrl), ct);
        return CreatedAtAction(nameof(GetById), new { id = result.Id, version = "1" }, result);
    }

    // ── Candidate — view own ─────────────────────────────────────────────────

    /// <summary>Get a specific application (candidate sees own, employers see any).</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ApplicationDto), 200)]
    [ProducesResponseType(403)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var isEmployer = _currentUser.IsInRole("Employer") || _currentUser.IsInRole("HiringManager")
                      || _currentUser.IsInRole("SuperAdmin");
        var result = await _mediator.Send(
            new GetApplicationByIdQuery(id, _currentUser.UserId!.Value, isEmployer), ct);
        return Ok(result);
    }

    /// <summary>Get the current candidate's own applications.</summary>
    [HttpGet("mine")]
    [Authorize(Roles = "Candidate,JobSeeker,SuperAdmin")]
    [ProducesResponseType(typeof(PagedResult<ApplicationDto>), 200)]
    public async Task<IActionResult> GetMine(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        var result = await _mediator.Send(
            new GetMyApplicationsQuery(_currentUser.UserId!.Value, page, pageSize), ct);
        return Ok(result);
    }

    // ── Employer — review pipeline ───────────────────────────────────────────

    /// <summary>Get all applications for a job, optionally filtered by status.</summary>
    [HttpGet("jobs/{jobId:guid}")]
    [Authorize(Roles = "Employer,HiringManager,SuperAdmin,Moderator")]
    [ProducesResponseType(typeof(PagedResult<ApplicationDto>), 200)]
    public async Task<IActionResult> GetForJob(
        Guid jobId,
        [FromQuery] ApplicationStatus? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetJobApplicationsQuery(jobId, status, page, pageSize), ct);
        return Ok(result);
    }

    /// <summary>Move an application through the hiring pipeline (employer action).</summary>
    [HttpPut("{id:guid}/status")]
    [Authorize(Roles = "Employer,HiringManager,SuperAdmin")]
    [ProducesResponseType(typeof(ApplicationDto), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> UpdateStatus(Guid id, [FromBody] UpdateStatusRequest req, CancellationToken ct)
    {
        var result = await _mediator.Send(
            new UpdateApplicationStatusCommand(id, _currentUser.UserId!.Value, req.Status, req.Notes, req.RejectionReason), ct);
        return Ok(result);
    }

    // ── Candidate — withdraw ─────────────────────────────────────────────────

    /// <summary>Withdraw an application (candidate action, irreversible).</summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "Candidate,JobSeeker,SuperAdmin")]
    [ProducesResponseType(204)]
    [ProducesResponseType(403)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> Withdraw(Guid id, CancellationToken ct)
    {
        await _mediator.Send(new WithdrawApplicationCommand(id, _currentUser.UserId!.Value), ct);
        return NoContent();
    }
}

public sealed record ApplyRequest(string? CoverLetter, string? CvUrl);
public sealed record UpdateStatusRequest(ApplicationStatus Status, string? Notes, string? RejectionReason);
