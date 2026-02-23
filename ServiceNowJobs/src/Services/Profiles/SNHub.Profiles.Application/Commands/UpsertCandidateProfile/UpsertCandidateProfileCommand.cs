using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using SNHub.Profiles.Application.DTOs;
using SNHub.Profiles.Application.Interfaces;
using SNHub.Profiles.Domain.Entities;
using SNHub.Profiles.Domain.Enums;
namespace SNHub.Profiles.Application.Commands.UpsertCandidateProfile;
public sealed record UpsertCandidateProfileCommand(Guid UserId, string? Headline, string? Bio, ExperienceLevel ExperienceLevel, int YearsOfExperience, AvailabilityStatus Availability, string? CurrentRole, string? DesiredRole, string? Location, string? Country, string? TimeZone, string? LinkedInUrl, string? GitHubUrl, string? WebsiteUrl, bool IsPublic, decimal? DesiredSalaryMin, decimal? DesiredSalaryMax, string? SalaryCurrency, bool OpenToRemote, bool OpenToRelocation, string? SkillsJson, string? CertificationsJson) : IRequest<CandidateProfileDto>;
public sealed class UpsertCandidateProfileCommandValidator : AbstractValidator<UpsertCandidateProfileCommand>
{
    public UpsertCandidateProfileCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.Headline).MaximumLength(200).When(x => x.Headline != null);
        RuleFor(x => x.Bio).MaximumLength(3000).When(x => x.Bio != null);
        RuleFor(x => x.YearsOfExperience).InclusiveBetween(0, 50);
    }
}
public sealed class UpsertCandidateProfileCommandHandler : IRequestHandler<UpsertCandidateProfileCommand, CandidateProfileDto>
{
    private readonly ICandidateProfileRepository _repo; private readonly IUnitOfWork _uow; private readonly ILogger<UpsertCandidateProfileCommandHandler> _logger;
    public UpsertCandidateProfileCommandHandler(ICandidateProfileRepository repo, IUnitOfWork uow, ILogger<UpsertCandidateProfileCommandHandler> logger) => (_repo, _uow, _logger) = (repo, uow, logger);
    public async Task<CandidateProfileDto> Handle(UpsertCandidateProfileCommand req, CancellationToken ct)
    {
        var profile = await _repo.GetByUserIdAsync(req.UserId, ct) ?? CandidateProfile.Create(req.UserId);
        var isNew = profile.ProfileCompleteness == 0 && profile.Headline == null;
        profile.Update(req.Headline, req.Bio, req.ExperienceLevel, req.YearsOfExperience, req.Availability, req.CurrentRole, req.DesiredRole, req.Location, req.Country, req.TimeZone, req.LinkedInUrl, req.GitHubUrl, req.WebsiteUrl, req.IsPublic, req.DesiredSalaryMin, req.DesiredSalaryMax, req.SalaryCurrency, req.OpenToRemote, req.OpenToRelocation);
        if (req.SkillsJson != null) profile.SetSkills(req.SkillsJson);
        if (req.CertificationsJson != null) profile.SetCertifications(req.CertificationsJson);
        if (isNew) await _repo.AddAsync(profile, ct);
        await _uow.SaveChangesAsync(ct);
        _logger.LogInformation("Candidate profile upserted for {UserId}", req.UserId);
        return ToDto(profile);
    }
    public static CandidateProfileDto ToDto(CandidateProfile p) => new(p.Id, p.UserId, p.Headline, p.Bio, p.ExperienceLevel.ToString(), p.YearsOfExperience, p.Availability.ToString(), p.CurrentRole, p.DesiredRole, p.Location, p.Country, p.TimeZone, p.ProfilePictureUrl, p.CvUrl, p.LinkedInUrl, p.GitHubUrl, p.WebsiteUrl, p.IsPublic, p.DesiredSalaryMin, p.DesiredSalaryMax, p.SalaryCurrency, p.OpenToRemote, p.OpenToRelocation, p.SkillsJson, p.CertificationsJson, p.ServiceNowVersionsJson, p.ProfileCompleteness, p.CreatedAt, p.UpdatedAt);
}
