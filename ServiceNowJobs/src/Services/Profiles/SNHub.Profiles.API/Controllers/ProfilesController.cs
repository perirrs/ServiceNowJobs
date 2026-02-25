using Asp.Versioning;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SNHub.Profiles.Application.Commands.UploadFile;
using SNHub.Profiles.Application.Commands.UpsertCandidateProfile;
using SNHub.Profiles.Application.Commands.UpsertEmployerProfile;
using SNHub.Profiles.Application.DTOs;
using SNHub.Profiles.Application.Interfaces;
using SNHub.Profiles.Application.Queries.GetProfile;
using SNHub.Profiles.Domain.Enums;

namespace SNHub.Profiles.API.Controllers;

/// <summary>Candidate and employer profiles — the professional identity layer of SNHub.</summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/profiles")]
[Authorize]
[Produces("application/json")]
public sealed class ProfilesController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ICurrentUserService _currentUser;

    public ProfilesController(IMediator mediator, ICurrentUserService currentUser)
    { _mediator = mediator; _currentUser = currentUser; }

    // ── Candidate — my profile ────────────────────────────────────────────────

    /// <summary>Get my own candidate profile.</summary>
    [HttpGet("candidate/me")]
    [ProducesResponseType(typeof(CandidateProfileDto), 200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> GetMyCandidateProfile(CancellationToken ct)
    {
        var result = await _mediator.Send(new GetCandidateProfileQuery(_currentUser.UserId!.Value), ct);
        return Ok(result);
    }

    /// <summary>Create or update my candidate profile.</summary>
    [HttpPut("candidate/me")]
    [ProducesResponseType(typeof(CandidateProfileDto), 200)]
    [ProducesResponseType(400)]
    public async Task<IActionResult> UpsertCandidateProfile(
        [FromBody] UpsertCandidateProfileRequest req, CancellationToken ct)
    {
        var result = await _mediator.Send(new UpsertCandidateProfileCommand(
            _currentUser.UserId!.Value,
            req.Headline, req.Bio, req.ExperienceLevel, req.YearsOfExperience,
            req.Availability, req.CurrentRole, req.DesiredRole,
            req.Location, req.Country, req.TimeZone,
            req.LinkedInUrl, req.GitHubUrl, req.WebsiteUrl,
            req.IsPublic, req.DesiredSalaryMin, req.DesiredSalaryMax, req.SalaryCurrency,
            req.OpenToRemote, req.OpenToRelocation,
            req.Skills, req.CertificationsJson, req.ServiceNowVersions), ct);
        return Ok(result);
    }

    /// <summary>Upload my profile picture (JPEG/PNG/WebP, max 5MB).</summary>
    [HttpPost("candidate/me/picture")]
    [ProducesResponseType(typeof(UploadedFileResponse), 200)]
    [ProducesResponseType(413)]
    [ProducesResponseType(415)]
    public async Task<IActionResult> UploadProfilePicture(IFormFile file, CancellationToken ct)
    {
        var url = await _mediator.Send(new UploadProfilePictureCommand(
            _currentUser.UserId!.Value,
            file.OpenReadStream(), file.FileName, file.ContentType, file.Length), ct);
        return Ok(new UploadedFileResponse(url));
    }

    /// <summary>Upload my CV (PDF only, max 10MB).</summary>
    [HttpPost("candidate/me/cv")]
    [ProducesResponseType(typeof(UploadedFileResponse), 200)]
    [ProducesResponseType(413)]
    [ProducesResponseType(415)]
    public async Task<IActionResult> UploadCv(IFormFile file, CancellationToken ct)
    {
        var url = await _mediator.Send(new UploadCvCommand(
            _currentUser.UserId!.Value,
            file.OpenReadStream(), file.FileName, file.ContentType, file.Length), ct);
        return Ok(new UploadedFileResponse(url));
    }

    // ── Candidate — public directory ──────────────────────────────────────────

    /// <summary>Get any candidate's public profile.</summary>
    [HttpGet("candidate/{userId:guid}")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(CandidateProfileDto), 200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> GetCandidateProfile(Guid userId, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetCandidateProfileQuery(userId), ct);
        return Ok(result);
    }

    /// <summary>Search the public candidate directory.</summary>
    [HttpGet("candidates/search")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(PagedResult<CandidateProfileDto>), 200)]
    public async Task<IActionResult> SearchCandidates(
        [FromQuery] string? keyword,
        [FromQuery] string? country,
        [FromQuery] ExperienceLevel? level,
        [FromQuery] int? minYears,
        [FromQuery] bool? openToRemote,
        [FromQuery] AvailabilityStatus? availability,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(
            new SearchCandidatesQuery(keyword, country, level, minYears, openToRemote, availability, page, pageSize), ct);
        return Ok(result);
    }

    // ── Employer profile ──────────────────────────────────────────────────────

    /// <summary>Get my employer profile.</summary>
    [HttpGet("employer/me")]
    [Authorize(Roles = "Employer,HiringManager,SuperAdmin")]
    [ProducesResponseType(typeof(EmployerProfileDto), 200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> GetMyEmployerProfile(CancellationToken ct)
    {
        var result = await _mediator.Send(new GetEmployerProfileQuery(_currentUser.UserId!.Value), ct);
        return Ok(result);
    }

    /// <summary>Get any employer's public profile.</summary>
    [HttpGet("employer/{userId:guid}")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(EmployerProfileDto), 200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> GetEmployerProfile(Guid userId, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetEmployerProfileQuery(userId), ct);
        return Ok(result);
    }

    /// <summary>Create or update my employer profile.</summary>
    [HttpPut("employer/me")]
    [Authorize(Roles = "Employer,HiringManager,SuperAdmin")]
    [ProducesResponseType(typeof(EmployerProfileDto), 200)]
    [ProducesResponseType(400)]
    public async Task<IActionResult> UpsertEmployerProfile(
        [FromBody] UpsertEmployerProfileRequest req, CancellationToken ct)
    {
        var result = await _mediator.Send(new UpsertEmployerProfileCommand(
            _currentUser.UserId!.Value,
            req.CompanyName, req.CompanyDescription, req.Industry,
            req.CompanySize, req.City, req.Country, req.WebsiteUrl, req.LinkedInUrl), ct);
        return Ok(result);
    }
}

// ── Request / response records ────────────────────────────────────────────────

public sealed record UpsertCandidateProfileRequest(
    string? Headline,
    string? Bio,
    ExperienceLevel ExperienceLevel,
    int YearsOfExperience,
    AvailabilityStatus Availability,
    string? CurrentRole,
    string? DesiredRole,
    string? Location,
    string? Country,
    string? TimeZone,
    string? LinkedInUrl,
    string? GitHubUrl,
    string? WebsiteUrl,
    bool IsPublic = true,
    decimal? DesiredSalaryMin = null,
    decimal? DesiredSalaryMax = null,
    string? SalaryCurrency = "USD",
    bool OpenToRemote = false,
    bool OpenToRelocation = false,
    IReadOnlyList<string>? Skills = null,
    string? CertificationsJson = null,
    IReadOnlyList<string>? ServiceNowVersions = null);

public sealed record UpsertEmployerProfileRequest(
    string? CompanyName,
    string? CompanyDescription,
    string? Industry,
    string? CompanySize,
    string? City,
    string? Country,
    string? WebsiteUrl,
    string? LinkedInUrl);

public sealed record UploadedFileResponse(string Url);
