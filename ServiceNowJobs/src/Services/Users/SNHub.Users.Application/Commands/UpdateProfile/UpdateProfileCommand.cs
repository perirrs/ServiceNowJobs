using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using SNHub.Users.Application.DTOs;
using SNHub.Users.Application.Interfaces;
using SNHub.Users.Application.Queries.GetProfile;
using SNHub.Users.Domain.Entities;

namespace SNHub.Users.Application.Commands.UpdateProfile;

public sealed record UpdateProfileCommand(
    Guid    UserId,
    string? FirstName,
    string? LastName,
    string? PhoneNumber,
    string? Headline,
    string? Bio,
    string? Location,
    string? LinkedInUrl,
    string? GitHubUrl,
    string? WebsiteUrl,
    int     YearsOfExperience,
    bool    IsPublic,
    string? Country,
    string? TimeZone) : IRequest<UserProfileDto>;

public sealed class UpdateProfileCommandValidator : AbstractValidator<UpdateProfileCommand>
{
    public UpdateProfileCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.FirstName).MaximumLength(100).When(x => x.FirstName != null);
        RuleFor(x => x.LastName).MaximumLength(100).When(x => x.LastName != null);
        RuleFor(x => x.PhoneNumber).MaximumLength(30).When(x => x.PhoneNumber != null);
        RuleFor(x => x.Headline).MaximumLength(200).When(x => x.Headline != null);
        RuleFor(x => x.Bio).MaximumLength(2000).When(x => x.Bio != null);
        RuleFor(x => x.Location).MaximumLength(200).When(x => x.Location != null);
        RuleFor(x => x.YearsOfExperience).InclusiveBetween(0, 50);
        RuleFor(x => x.LinkedInUrl)
            .Must(u => Uri.TryCreate(u, UriKind.Absolute, out _))
            .When(x => !string.IsNullOrEmpty(x.LinkedInUrl))
            .WithMessage("Invalid LinkedIn URL.");
        RuleFor(x => x.GitHubUrl)
            .Must(u => Uri.TryCreate(u, UriKind.Absolute, out _))
            .When(x => !string.IsNullOrEmpty(x.GitHubUrl))
            .WithMessage("Invalid GitHub URL.");
        RuleFor(x => x.WebsiteUrl)
            .Must(u => Uri.TryCreate(u, UriKind.Absolute, out _))
            .When(x => !string.IsNullOrEmpty(x.WebsiteUrl))
            .WithMessage("Invalid website URL.");
    }
}

public sealed class UpdateProfileCommandHandler : IRequestHandler<UpdateProfileCommand, UserProfileDto>
{
    private readonly IUserProfileRepository _repo;
    private readonly IUnitOfWork            _uow;
    private readonly ILogger<UpdateProfileCommandHandler> _logger;

    public UpdateProfileCommandHandler(
        IUserProfileRepository repo, IUnitOfWork uow,
        ILogger<UpdateProfileCommandHandler> logger)
    { _repo = repo; _uow = uow; _logger = logger; }

    public async Task<UserProfileDto> Handle(UpdateProfileCommand req, CancellationToken ct)
    {
        var profile = await _repo.GetByUserIdAsync(req.UserId, ct);

        if (profile is null)
        {
            profile = UserProfile.Create(req.UserId);
            profile.Update(req.FirstName, req.LastName, null, req.PhoneNumber,
                req.Headline, req.Bio, req.Location,
                req.LinkedInUrl, req.GitHubUrl, req.WebsiteUrl,
                req.YearsOfExperience, req.IsPublic, req.Country, req.TimeZone);
            await _repo.AddAsync(profile, ct);
        }
        else
        {
            profile.Update(req.FirstName, req.LastName, profile.Email, req.PhoneNumber,
                req.Headline, req.Bio, req.Location,
                req.LinkedInUrl, req.GitHubUrl, req.WebsiteUrl,
                req.YearsOfExperience, req.IsPublic, req.Country, req.TimeZone);
        }

        await _uow.SaveChangesAsync(ct);
        _logger.LogInformation("Profile updated for user {UserId}", req.UserId);
        return UserProfileMapper.ToDto(profile);
    }
}
