using FluentValidation;
using MediatR;
using SNHub.Auth.Application.DTOs;
using SNHub.Auth.Application.Interfaces;
using SNHub.Auth.Domain.Exceptions;

namespace SNHub.Auth.Application.Commands.UpdateProfile;

public sealed record UpdateProfileCommand(
    string FirstName,
    string LastName,
    string? PhoneNumber = null,
    string? Country = null,
    string? TimeZone = null) : IRequest<UserProfileDto>;

public sealed class UpdateProfileCommandValidator : AbstractValidator<UpdateProfileCommand>
{
    private static readonly System.Text.RegularExpressions.Regex NameRegex =
        new(@"^[\p{L}\s\-']+$", System.Text.RegularExpressions.RegexOptions.Compiled);

    private static readonly System.Text.RegularExpressions.Regex PhoneRegex =
        new(@"^\+[1-9]\d{6,14}$", System.Text.RegularExpressions.RegexOptions.Compiled);

    public UpdateProfileCommandValidator()
    {
        RuleFor(x => x.FirstName)
            .NotEmpty().WithMessage("First name is required.")
            .MaximumLength(100)
            .Matches(NameRegex).WithMessage("First name can only contain letters, spaces, hyphens, and apostrophes.");

        RuleFor(x => x.LastName)
            .NotEmpty().WithMessage("Last name is required.")
            .MaximumLength(100)
            .Matches(NameRegex).WithMessage("Last name can only contain letters, spaces, hyphens, and apostrophes.");

        When(x => x.PhoneNumber is not null, () =>
            RuleFor(x => x.PhoneNumber!)
                .Matches(PhoneRegex).WithMessage("Phone must be in E.164 format (e.g. +447911123456).")
                .MaximumLength(20));

        When(x => x.Country is not null, () =>
            RuleFor(x => x.Country!)
                .Length(2, 3).WithMessage("Country must be an ISO 3166-1 alpha-2 or alpha-3 code.")
                .Matches(@"^[a-zA-Z]+$").WithMessage("Country must contain only letters."));

        When(x => x.TimeZone is not null, () =>
            RuleFor(x => x.TimeZone!).MaximumLength(100));
    }
}

public sealed class UpdateProfileCommandHandler : IRequestHandler<UpdateProfileCommand, UserProfileDto>
{
    private readonly IUserRepository _users;
    private readonly IUnitOfWork _uow;
    private readonly ICurrentUserService _currentUser;

    public UpdateProfileCommandHandler(
        IUserRepository users, IUnitOfWork uow, ICurrentUserService currentUser)
    {
        _users = users;
        _uow = uow;
        _currentUser = currentUser;
    }

    public async Task<UserProfileDto> Handle(UpdateProfileCommand request, CancellationToken ct)
    {
        // Use email from JWT claim — GetByEmailAsync returns a tracked entity
        // (no AsNoTracking) so EF Core can persist the UpdateProfile mutations.
        var email = _currentUser.Email
            ?? throw new UnauthorizedAccessException("User not authenticated.");

        var user = await _users.GetByEmailAsync(email, ct)
            ?? throw new UserNotFoundException($"User with email {email} not found.");

        user.UpdateProfile(
            request.FirstName, request.LastName,
            request.PhoneNumber, request.Country,
            request.TimeZone, email);

        await _uow.SaveChangesAsync(ct);

        return new UserProfileDto(
            user.Id, user.Email, user.FirstName, user.LastName, user.FullName,
            user.PhoneNumber, user.ProfilePictureUrl, user.IsEmailVerified,
            user.IsActive, user.Roles.Select(r => r.ToString()),
            user.LastLoginAt, user.Country, user.TimeZone, user.CreatedAt);
    }
}
