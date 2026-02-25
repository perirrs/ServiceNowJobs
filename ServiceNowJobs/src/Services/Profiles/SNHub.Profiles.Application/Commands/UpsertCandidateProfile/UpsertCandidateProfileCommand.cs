using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using SNHub.Profiles.Application.DTOs;
using SNHub.Profiles.Application.Interfaces;
using SNHub.Profiles.Domain.Entities;
using SNHub.Profiles.Domain.Enums;
using System.Text.Json;

namespace SNHub.Profiles.Application.Commands.UpsertCandidateProfile;

public sealed record UpsertCandidateProfileCommand(
    Guid UserId,
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
    bool IsPublic,
    decimal? DesiredSalaryMin,
    decimal? DesiredSalaryMax,
    string? SalaryCurrency,
    bool OpenToRemote,
    bool OpenToRelocation,
    IReadOnlyList<string>? Skills,
    string? CertificationsJson,
    IReadOnlyList<string>? ServiceNowVersions) : IRequest<CandidateProfileDto>;

public sealed class UpsertCandidateProfileCommandValidator : AbstractValidator<UpsertCandidateProfileCommand>
{
    private static readonly HashSet<string> _validCurrencies = ["USD", "GBP", "EUR", "AUD", "CAD", "INR", "SGD", "AED"];

    public UpsertCandidateProfileCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.Headline).MaximumLength(200).When(x => x.Headline is not null);
        RuleFor(x => x.Bio).MaximumLength(3_000).When(x => x.Bio is not null);
        RuleFor(x => x.YearsOfExperience).InclusiveBetween(0, 50);
        RuleFor(x => x.ExperienceLevel).IsInEnum();
        RuleFor(x => x.Availability).IsInEnum();
        RuleFor(x => x.SalaryCurrency)
            .Must(c => c is null || _validCurrencies.Contains(c))
            .WithMessage("SalaryCurrency must be a valid 3-letter currency code.");
        RuleFor(x => x.DesiredSalaryMax)
            .GreaterThanOrEqualTo(x => x.DesiredSalaryMin)
            .When(x => x.DesiredSalaryMin.HasValue && x.DesiredSalaryMax.HasValue)
            .WithMessage("DesiredSalaryMax must be ≥ DesiredSalaryMin.");
        RuleFor(x => x.LinkedInUrl)
            .MaximumLength(500).Must(u => u is null || Uri.TryCreate(u, UriKind.Absolute, out _))
            .WithMessage("LinkedInUrl must be a valid URL.").When(x => x.LinkedInUrl is not null);
        RuleFor(x => x.Skills).Must(s => s is null || s.Count <= 30)
            .WithMessage("Maximum 30 skills allowed.");
        RuleFor(x => x.ServiceNowVersions).Must(v => v is null || v.Count <= 20)
            .WithMessage("Maximum 20 ServiceNow versions.");
    }
}

public sealed class UpsertCandidateProfileCommandHandler
    : IRequestHandler<UpsertCandidateProfileCommand, CandidateProfileDto>
{
    private readonly ICandidateProfileRepository _repo;
    private readonly IUnitOfWork _uow;
    private readonly ILogger<UpsertCandidateProfileCommandHandler> _logger;

    public UpsertCandidateProfileCommandHandler(
        ICandidateProfileRepository repo, IUnitOfWork uow,
        ILogger<UpsertCandidateProfileCommandHandler> logger)
    { _repo = repo; _uow = uow; _logger = logger; }

    public async Task<CandidateProfileDto> Handle(UpsertCandidateProfileCommand req, CancellationToken ct)
    {
        var profile = await _repo.GetByUserIdAsync(req.UserId, ct);
        var isNew   = profile is null;
        profile   ??= CandidateProfile.Create(req.UserId);

        profile.Update(
            req.Headline, req.Bio, req.ExperienceLevel, req.YearsOfExperience,
            req.Availability, req.CurrentRole, req.DesiredRole,
            req.Location, req.Country, req.TimeZone,
            req.LinkedInUrl, req.GitHubUrl, req.WebsiteUrl,
            req.IsPublic, req.DesiredSalaryMin, req.DesiredSalaryMax, req.SalaryCurrency,
            req.OpenToRemote, req.OpenToRelocation);

        if (req.Skills is not null)
            profile.SetSkills(JsonSerializer.Serialize(req.Skills));
        if (req.CertificationsJson is not null)
            profile.SetCertifications(req.CertificationsJson);
        if (req.ServiceNowVersions is not null)
            profile.SetServiceNowVersions(JsonSerializer.Serialize(req.ServiceNowVersions));

        if (isNew) await _repo.AddAsync(profile, ct);
        await _uow.SaveChangesAsync(ct);

        _logger.LogInformation("{Action} candidate profile for {UserId}", isNew ? "Created" : "Updated", req.UserId);
        return ProfileMapper.ToDto(profile);
    }
}
