using Asp.Versioning;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SNHub.Jobs.Application.Commands.CloseJob;
using SNHub.Jobs.Application.Commands.CreateJob;
using SNHub.Jobs.Application.Commands.UpdateJob;
using SNHub.Jobs.Application.Queries.GetJob;
using SNHub.Jobs.Application.Queries.SearchJobs;
using SNHub.Jobs.Domain.Enums;
using System.Security.Claims;

namespace SNHub.Jobs.API.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/jobs")]
public sealed class JobsController : ControllerBase
{
    private readonly IMediator _mediator;
    public JobsController(IMediator mediator) { _mediator = mediator; }

    private Guid CurrentUserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub") ?? Guid.Empty.ToString());

    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> Search(
        [FromQuery] string? keyword, [FromQuery] string? country,
        [FromQuery] JobType? jobType, [FromQuery] WorkMode? workMode, [FromQuery] ExperienceLevel? level,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new SearchJobsQuery(keyword, country, jobType, workMode, level, null, page, pageSize), ct);
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    [AllowAnonymous]
    public async Task<IActionResult> GetJob(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetJobQuery(id), ct);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpPost]
    [Authorize(Roles = "Employer,HiringManager,SuperAdmin")]
    public async Task<IActionResult> CreateJob([FromBody] CreateJobCommand command, CancellationToken ct)
    {
        var cmd = command with { EmployerId = CurrentUserId };
        var result = await _mediator.Send(cmd, ct);
        return CreatedAtAction(nameof(GetJob), new { id = result.Id }, result);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = "Employer,HiringManager,SuperAdmin")]
    public async Task<IActionResult> UpdateJob(Guid id, [FromBody] UpdateJobCommand command, CancellationToken ct)
    {
        var cmd = command with { JobId = id, RequesterId = CurrentUserId };
        var result = await _mediator.Send(cmd, ct);
        return Ok(result);
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "Employer,HiringManager,SuperAdmin")]
    public async Task<IActionResult> CloseJob(Guid id, CancellationToken ct)
    {
        await _mediator.Send(new CloseJobCommand(id, CurrentUserId), ct);
        return NoContent();
    }
}
