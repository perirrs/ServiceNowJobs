using Asp.Versioning;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SNHub.Jobs.Application.Commands.CloseJob;
using SNHub.Jobs.Application.Commands.CreateJob;
using SNHub.Jobs.Application.Commands.PauseJob;
using SNHub.Jobs.Application.Commands.PublishJob;
using SNHub.Jobs.Application.Commands.UpdateJob;
using SNHub.Jobs.Application.DTOs;
using SNHub.Jobs.Application.Interfaces;
using SNHub.Jobs.Application.Queries.GetJob;
using SNHub.Jobs.Application.Queries.GetMyJobs;
using SNHub.Jobs.Application.Queries.SearchJobs;
using SNHub.Jobs.Domain.Enums;

namespace SNHub.Jobs.API.Controllers;

/// <summary>Job postings — create, browse, search, manage.</summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/jobs")]
[Produces("application/json")]
public sealed class JobsController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ICurrentUserService _currentUser;

    public JobsController(IMediator mediator, ICurrentUserService currentUser)
    {
        _mediator = mediator;
        _currentUser = currentUser;
    }

    // ── Public search ─────────────────────────────────────────────────────────

    /// <summary>Search active job listings. All parameters are optional filters.</summary>
    [HttpGet]
    [AllowAnonymous]
    [ProducesResponseType(typeof(JobSearchResultDto), 200)]
    public async Task<IActionResult> Search(
        [FromQuery] string? keyword,
        [FromQuery] string? country,
        [FromQuery] string? location,
        [FromQuery] JobType? jobType,
        [FromQuery] WorkMode? workMode,
        [FromQuery] ExperienceLevel? level,
        [FromQuery] decimal? salaryMin,
        [FromQuery] decimal? salaryMax,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(
            new SearchJobsQuery(keyword, country, location, jobType, workMode, level,
                                salaryMin, salaryMax, EmployerId: null, page, pageSize), ct);
        return Ok(result);
    }

    /// <summary>Get a specific job posting by ID. Increments view count.</summary>
    [HttpGet("{id:guid}")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(JobDto), 200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> GetJob(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetJobQuery(id), ct);
        return Ok(result);
    }

    // ── Employer — my jobs ────────────────────────────────────────────────────

    /// <summary>Get the current employer's own job listings (all statuses).</summary>
    [HttpGet("mine")]
    [Authorize(Roles = "Employer,HiringManager,SuperAdmin")]
    [ProducesResponseType(typeof(JobSearchResultDto), 200)]
    public async Task<IActionResult> GetMyJobs(
        [FromQuery] JobStatus? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(
            new GetMyJobsQuery(_currentUser.UserId!.Value, status, page, pageSize), ct);
        return Ok(result);
    }

    // ── Employer — CRUD ───────────────────────────────────────────────────────

    /// <summary>Post a new job (saved as Draft by default; set PublishImmediately=true to go live).</summary>
    [HttpPost]
    [Authorize(Roles = "Employer,HiringManager,SuperAdmin")]
    [ProducesResponseType(typeof(JobDto), 201)]
    [ProducesResponseType(400)]
    public async Task<IActionResult> CreateJob([FromBody] CreateJobCommand command, CancellationToken ct)
    {
        var cmd = command with { EmployerId = _currentUser.UserId!.Value };
        var result = await _mediator.Send(cmd, ct);
        return CreatedAtAction(nameof(GetJob), new { id = result.Id, version = "1" }, result);
    }

    /// <summary>Update an existing job posting.</summary>
    [HttpPut("{id:guid}")]
    [Authorize(Roles = "Employer,HiringManager,SuperAdmin")]
    [ProducesResponseType(typeof(JobDto), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(403)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> UpdateJob(Guid id, [FromBody] UpdateJobCommand command, CancellationToken ct)
    {
        var cmd = command with { JobId = id, RequesterId = _currentUser.UserId!.Value };
        var result = await _mediator.Send(cmd, ct);
        return Ok(result);
    }

    /// <summary>Publish a draft or paused job — makes it visible to candidates.</summary>
    [HttpPost("{id:guid}/publish")]
    [Authorize(Roles = "Employer,HiringManager,SuperAdmin")]
    [ProducesResponseType(typeof(JobDto), 200)]
    [ProducesResponseType(403)]
    [ProducesResponseType(404)]
    [ProducesResponseType(409)]
    public async Task<IActionResult> PublishJob(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new PublishJobCommand(id, _currentUser.UserId!.Value), ct);
        return Ok(result);
    }

    /// <summary>Pause an active job — hides it from search without closing it permanently.</summary>
    [HttpPost("{id:guid}/pause")]
    [Authorize(Roles = "Employer,HiringManager,SuperAdmin")]
    [ProducesResponseType(typeof(JobDto), 200)]
    [ProducesResponseType(403)]
    [ProducesResponseType(404)]
    [ProducesResponseType(409)]
    public async Task<IActionResult> PauseJob(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new PauseJobCommand(id, _currentUser.UserId!.Value), ct);
        return Ok(result);
    }

    /// <summary>Close a job permanently — removes it from search and prevents further applications.</summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "Employer,HiringManager,SuperAdmin")]
    [ProducesResponseType(204)]
    [ProducesResponseType(403)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> CloseJob(Guid id, CancellationToken ct)
    {
        await _mediator.Send(new CloseJobCommand(id, _currentUser.UserId!.Value), ct);
        return NoContent();
    }
}
