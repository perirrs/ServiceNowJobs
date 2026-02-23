using Asp.Versioning;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SNHub.Profiles.Application.Commands.UpsertCandidateProfile;
using SNHub.Profiles.Application.Commands.UpsertEmployerProfile;
using SNHub.Profiles.Application.DTOs;
using SNHub.Profiles.Application.Queries.GetProfile;
using SNHub.Profiles.Domain.Enums;
using System.Security.Claims;

namespace SNHub.Profiles.API.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[Authorize]
public sealed class ProfilesController(IMediator mediator) : ControllerBase
{
    private Guid CurrentUserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    /// <summary>Get my candidate profile</summary>
    [HttpGet("candidate/me")]
    [ProducesResponseType(typeof(CandidateProfileDto), 200)]
    public async Task<IActionResult> GetMyCandidateProfile(CancellationToken ct)
    {
        var result = await mediator.Send(new GetCandidateProfileQuery(CurrentUserId), ct);
        return Ok(result);
    }

    /// <summary>Get candidate profile by user ID (public)</summary>
    [HttpGet("candidate/{userId:guid}")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(CandidateProfileDto), 200)]
    public async Task<IActionResult> GetCandidateProfile(Guid userId, CancellationToken ct)
    {
        var result = await mediator.Send(new GetCandidateProfileQuery(userId), ct);
        return Ok(result);
    }

    /// <summary>Upsert my candidate profile</summary>
    [HttpPut("candidate/me")]
    [ProducesResponseType(typeof(CandidateProfileDto), 200)]
    public async Task<IActionResult> UpsertCandidateProfile([FromBody] UpsertCandidateProfileRequest req, CancellationToken ct)
    {
        var result = await mediator.Send(new UpsertCandidateProfileCommand(
            CurrentUserId, req.Headline, req.Bio, req.ExperienceLevel, req.YearsOfExperience,
            req.Availability, req.CurrentRole, req.DesiredRole,
            req.Location, req.Country, req.TimeZone,
            req.LinkedInUrl, req.GitHubUrl, req.WebsiteUrl,
            req.IsPublic, req.DesiredSalaryMin, req.DesiredSalaryMax, req.SalaryCurrency,
            req.OpenToRemote, req.OpenToRelocation, req.SkillsJson, req.CertificationsJson), ct);
        return Ok(result);
    }

    /// <summary>Get my employer profile</summary>
    [HttpGet("employer/me")]
    [ProducesResponseType(typeof(EmployerProfileDto), 200)]
    public async Task<IActionResult> GetMyEmployerProfile(CancellationToken ct)
    {
        var result = await mediator.Send(new GetEmployerProfileQuery(CurrentUserId), ct);
        return Ok(result);
    }

    /// <summary>Upsert my employer profile</summary>
    [HttpPut("employer/me")]
    [Authorize(Roles = "Employer,HiringManager,SuperAdmin")]
    [ProducesResponseType(typeof(EmployerProfileDto), 200)]
    public async Task<IActionResult> UpsertEmployerProfile([FromBody] UpsertEmployerProfileRequest req, CancellationToken ct)
    {
        var result = await mediator.Send(new UpsertEmployerProfileCommand(
            CurrentUserId, req.CompanyName, req.CompanyDescription,
            req.Industry, req.CompanySize, req.City, req.Country,
            req.WebsiteUrl, req.LinkedInUrl), ct);
        return Ok(result);
    }
}

public sealed record UpsertCandidateProfileRequest(
    string? Headline, string? Bio,
    ExperienceLevel ExperienceLevel, int YearsOfExperience,
    AvailabilityStatus Availability, string? CurrentRole, string? DesiredRole,
    string? Location, string? Country, string? TimeZone,
    string? LinkedInUrl, string? GitHubUrl, string? WebsiteUrl,
    bool IsPublic = true, decimal? DesiredSalaryMin = null, decimal? DesiredSalaryMax = null,
    string? SalaryCurrency = "USD", bool OpenToRemote = false, bool OpenToRelocation = false,
    string? SkillsJson = null, string? CertificationsJson = null);

public sealed record UpsertEmployerProfileRequest(
    string? CompanyName, string? CompanyDescription, string? Industry,
    string? CompanySize, string? City, string? Country,
    string? WebsiteUrl, string? LinkedInUrl);
