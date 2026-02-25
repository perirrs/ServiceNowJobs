using Asp.Versioning;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SNHub.CvParser.Application.Commands.ApplyParsedCv;
using SNHub.CvParser.Application.Commands.ParseCv;
using SNHub.CvParser.Application.DTOs;
using SNHub.CvParser.Application.Interfaces;
using SNHub.CvParser.Application.Queries.GetParseResult;

namespace SNHub.CvParser.API.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/cv")]
[Authorize]
[Produces("application/json")]
public sealed class CvParserController : ControllerBase
{
    private readonly IMediator           _mediator;
    private readonly ICurrentUserService _currentUser;

    public CvParserController(IMediator mediator, ICurrentUserService currentUser)
    { _mediator = mediator; _currentUser = currentUser; }

    /// <summary>
    /// Upload and parse a CV. Accepts PDF and DOCX (max 10 MB).
    /// Returns extracted fields with confidence scores.
    /// AI parsing happens synchronously — typical response time 5-15 seconds.
    /// </summary>
    [HttpPost("parse")]
    [RequestSizeLimit(10 * 1024 * 1024)]
    [ProducesResponseType(typeof(CvParseResultDto), 200)]
    [ProducesResponseType(400)]
    public async Task<IActionResult> ParseCv(IFormFile file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { message = "No file provided." });

        await using var stream = file.OpenReadStream();
        var result = await _mediator.Send(new ParseCvCommand(
            _currentUser.UserId!.Value,
            stream,
            file.FileName,
            file.ContentType,
            file.Length), ct);

        return Ok(result);
    }

    /// <summary>
    /// Apply the extracted CV data to your candidate profile.
    /// Only fields that meet the confidence threshold (default 60%) are applied.
    /// Can only be applied once per parse result.
    /// </summary>
    [HttpPost("{parseResultId:guid}/apply")]
    [ProducesResponseType(typeof(ApplyParsedCvResponse), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(404)]
    [ProducesResponseType(409)]
    public async Task<IActionResult> ApplyParsedCv(
        Guid parseResultId,
        [FromQuery] int confidenceThreshold = 60,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(new ApplyParsedCvCommand(
            parseResultId, _currentUser.UserId!.Value, confidenceThreshold), ct);
        return Ok(result);
    }

    /// <summary>Get a specific parse result by ID.</summary>
    [HttpGet("{parseResultId:guid}")]
    [ProducesResponseType(typeof(CvParseResultDto), 200)]
    [ProducesResponseType(403)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> GetParseResult(Guid parseResultId, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetParseResultQuery(
            parseResultId, _currentUser.UserId!.Value), ct);
        return Ok(result);
    }

    /// <summary>List all parse results for the current user, newest first.</summary>
    [HttpGet("my-results")]
    [ProducesResponseType(typeof(IEnumerable<CvParseResultDto>), 200)]
    public async Task<IActionResult> GetMyParseResults(CancellationToken ct)
    {
        var results = await _mediator.Send(
            new GetMyParseResultsQuery(_currentUser.UserId!.Value), ct);
        return Ok(results);
    }
}
