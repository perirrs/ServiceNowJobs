using Asp.Versioning;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SNHub.Matching.Application.Commands.RequestEmbedding;
using SNHub.Matching.Application.DTOs;
using SNHub.Matching.Application.Interfaces;
using SNHub.Matching.Application.Queries.GetCandidateMatches;
using SNHub.Matching.Application.Queries.GetJobMatches;
using SNHub.Matching.Domain.Enums;

namespace SNHub.Matching.API.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/matching")]
[Authorize]
[Produces("application/json")]
public sealed class MatchingController : ControllerBase
{
    private readonly IMediator           _mediator;
    private readonly ICurrentUserService _currentUser;

    public MatchingController(IMediator mediator, ICurrentUserService currentUser)
    { _mediator = mediator; _currentUser = currentUser; }

    // ── Candidate: get my job matches ─────────────────────────────────────────

    /// <summary>
    /// Returns ranked job matches for the current candidate.
    /// Returns embeddingReady=false if the profile hasn't been indexed yet.
    /// </summary>
    [HttpGet("my-job-matches")]
    [ProducesResponseType(typeof(MatchResultsDto<JobMatchDto>), 200)]
    public async Task<IActionResult> GetMyJobMatches(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 10,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetCandidateMatchesQuery(
            _currentUser.UserId!.Value, page, pageSize), ct);
        return Ok(result);
    }

    // ── Employer: get candidates for a job ────────────────────────────────────

    /// <summary>
    /// Returns ranked candidate matches for a specific job posting.
    /// Only the job's employer or an admin may call this endpoint.
    /// </summary>
    [HttpGet("jobs/{jobId:guid}/candidates")]
    [ProducesResponseType(typeof(MatchResultsDto<CandidateMatchDto>), 200)]
    [ProducesResponseType(403)]
    public async Task<IActionResult> GetCandidatesForJob(
        Guid jobId,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 10,
        CancellationToken ct = default)
    {
        var isAdmin = _currentUser.IsInRole("SuperAdmin") || _currentUser.IsInRole("Admin");
        var result  = await _mediator.Send(new GetJobMatchesQuery(
            jobId, _currentUser.UserId!.Value, isAdmin, page, pageSize), ct);
        return Ok(result);
    }

    // ── Request (re)indexing ──────────────────────────────────────────────────

    /// <summary>
    /// Requests (re)indexing of the current user's candidate profile.
    /// The background worker will process it within ~15 seconds.
    /// Call this after updating your profile.
    /// </summary>
    [HttpPost("index/my-profile")]
    [ProducesResponseType(typeof(EmbeddingStatusDto), 202)]
    public async Task<IActionResult> IndexMyProfile(CancellationToken ct)
    {
        var result = await _mediator.Send(new RequestEmbeddingCommand(
            _currentUser.UserId!.Value, DocumentType.CandidateProfile), ct);
        return Accepted(result);
    }

    /// <summary>
    /// Requests (re)indexing of a job posting.
    /// Called internally when a job is published or updated.
    /// Employer must own the job, or caller must be an admin.
    /// </summary>
    [HttpPost("index/jobs/{jobId:guid}")]
    [ProducesResponseType(typeof(EmbeddingStatusDto), 202)]
    public async Task<IActionResult> IndexJob(Guid jobId, CancellationToken ct)
    {
        var result = await _mediator.Send(new RequestEmbeddingCommand(
            jobId, DocumentType.Job), ct);
        return Accepted(result);
    }
}
