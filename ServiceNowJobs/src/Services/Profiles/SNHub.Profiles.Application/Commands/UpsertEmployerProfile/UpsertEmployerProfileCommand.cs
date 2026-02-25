using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using SNHub.Profiles.Application.DTOs;
using SNHub.Profiles.Application.Interfaces;
using SNHub.Profiles.Domain.Entities;

namespace SNHub.Profiles.Application.Commands.UpsertEmployerProfile;

public sealed record UpsertEmployerProfileCommand(
    Guid UserId,
    string? CompanyName,
    string? CompanyDescription,
    string? Industry,
    string? CompanySize,
    string? City,
    string? Country,
    string? WebsiteUrl,
    string? LinkedInUrl) : IRequest<EmployerProfileDto>;

public sealed class UpsertEmployerProfileCommandValidator : AbstractValidator<UpsertEmployerProfileCommand>
{
    private static readonly HashSet<string> _validSizes = ["1-10", "11-50", "51-200", "201-500", "500+"];

    public UpsertEmployerProfileCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.CompanyName).MaximumLength(200).When(x => x.CompanyName is not null);
        RuleFor(x => x.CompanyDescription).MaximumLength(5_000).When(x => x.CompanyDescription is not null);
        RuleFor(x => x.Industry).MaximumLength(100).When(x => x.Industry is not null);
        RuleFor(x => x.CompanySize)
            .Must(s => s is null || _validSizes.Contains(s))
            .WithMessage($"CompanySize must be one of: {string.Join(", ", _validSizes)}");
        RuleFor(x => x.WebsiteUrl)
            .Must(u => u is null || Uri.TryCreate(u, UriKind.Absolute, out _))
            .WithMessage("WebsiteUrl must be a valid URL.").When(x => x.WebsiteUrl is not null);
        RuleFor(x => x.LinkedInUrl)
            .MaximumLength(500)
            .Must(u => u is null || Uri.TryCreate(u, UriKind.Absolute, out _))
            .WithMessage("LinkedInUrl must be a valid URL.").When(x => x.LinkedInUrl is not null);
    }
}

public sealed class UpsertEmployerProfileCommandHandler
    : IRequestHandler<UpsertEmployerProfileCommand, EmployerProfileDto>
{
    private readonly IEmployerProfileRepository _repo;
    private readonly IUnitOfWork _uow;
    private readonly ILogger<UpsertEmployerProfileCommandHandler> _logger;

    public UpsertEmployerProfileCommandHandler(
        IEmployerProfileRepository repo, IUnitOfWork uow,
        ILogger<UpsertEmployerProfileCommandHandler> logger)
    { _repo = repo; _uow = uow; _logger = logger; }

    public async Task<EmployerProfileDto> Handle(UpsertEmployerProfileCommand req, CancellationToken ct)
    {
        var profile = await _repo.GetByUserIdAsync(req.UserId, ct);
        var isNew   = profile is null;
        profile   ??= EmployerProfile.Create(req.UserId);

        profile.Update(req.CompanyName, req.CompanyDescription, req.Industry,
            req.CompanySize, req.City, req.Country, req.WebsiteUrl, req.LinkedInUrl);

        if (isNew) await _repo.AddAsync(profile, ct);
        await _uow.SaveChangesAsync(ct);

        _logger.LogInformation("{Action} employer profile for {UserId}", isNew ? "Created" : "Updated", req.UserId);
        return ProfileMapper.ToDto(profile);
    }
}
