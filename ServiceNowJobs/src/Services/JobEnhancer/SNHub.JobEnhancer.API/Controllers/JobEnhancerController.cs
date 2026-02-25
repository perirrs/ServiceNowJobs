using Asp.Versioning;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SNHub.JobEnhancer.Application.Commands.AcceptEnhancement;
using SNHub.JobEnhancer.Application.Commands.EnhanceDescription;
using SNHub.JobEnhancer.Application.DTOs;
using SNHub.JobEnhancer.Application.Interfaces;
using SNHub.JobEnhancer.Application.Queries.GetEnhancement;

namespace SNHub.JobEnhancer.API.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/enhance")]
[Authorize]
[Produces("application/json")]
public sealed class JobEnhancerController : ControllerBase
{
    private readonly IMediator           _mediator;
    private readonly ICurrentUserService _currentUser;

    public JobEnhancerController(IMediator mediator, ICurrentUserService currentUser)
    { _mediator = mediator; _currentUser = currentUser; }

    /// <summary>
    /// Submits a job description to GPT-4o for AI enhancement.
    /// Returns enhanced content, bias analysis, quality scores, and improvement suggestions.
    /// Response time: 10-25 seconds (synchronous GPT-4o call).
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(EnhancementResultDto), 200)]
    [ProducesResponseType(400)]
    public async Task<IActionResult> Enhance(
        [FromBody] EnhanceRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(new EnhanceDescriptionCommand(
            request.JobId,
            _currentUser.UserId!.Value,
            request.Title,
            request.Description,
            request.Requirements), ct);
        return Ok(result);
    }

    /// <summary>
    /// Accepts an enhancement — applies the AI-improved content to the job posting.
    /// Notifies the Jobs service via HTTP PATCH.
    /// </summary>
    [HttpPost("{id:guid}/accept")]
    [ProducesResponseType(typeof(AcceptEnhancementResponse), 200)]
    [ProducesResponseType(403)]
    [ProducesResponseType(404)]
    [ProducesResponseType(409)]
    public async Task<IActionResult> Accept(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new AcceptEnhancementCommand(
            id, _currentUser.UserId!.Value), ct);
        return Ok(result);
    }

    /// <summary>
    /// Gets a single enhancement result by ID.
    /// Only the requester can access their own results.
    /// </summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(EnhancementResultDto), 200)]
    [ProducesResponseType(403)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetEnhancementQuery(
            id, _currentUser.UserId!.Value), ct);
        return Ok(result);
    }

    /// <summary>
    /// Lists all enhancement attempts for a specific job.
    /// Only returns results the caller requested.
    /// </summary>
    [HttpGet("jobs/{jobId:guid}")]
    [ProducesResponseType(typeof(IEnumerable<EnhancementResultDto>), 200)]
    public async Task<IActionResult> GetByJob(Guid jobId, CancellationToken ct)
    {
        var results = await _mediator.Send(new GetJobEnhancementsQuery(
            jobId, _currentUser.UserId!.Value), ct);
        return Ok(results);
    }

    /// <summary>
    /// Lists all enhancements requested by the current user, newest first.
    /// </summary>
    [HttpGet("my-enhancements")]
    [ProducesResponseType(typeof(IEnumerable<EnhancementResultDto>), 200)]
    public async Task<IActionResult> GetMyEnhancements(CancellationToken ct)
    {
        var results = await _mediator.Send(new GetMyEnhancementsQuery(
            _currentUser.UserId!.Value), ct);
        return Ok(results);
    }
}

public sealed record EnhanceRequest(
    Guid    JobId,
    string  Title,
    string  Description,
    string? Requirements);
