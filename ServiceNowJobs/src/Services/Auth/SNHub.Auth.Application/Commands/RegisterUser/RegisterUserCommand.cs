using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using SNHub.Auth.Application.DTOs;
using SNHub.Auth.Application.Interfaces;
using SNHub.Auth.Domain.Entities;
using SNHub.Auth.Domain.Enums;
using SNHub.Auth.Domain.Exceptions;

namespace SNHub.Auth.Application.Commands.RegisterUser;

public sealed record RegisterUserCommand(
    string Email,
    string Password,
    string ConfirmPassword,
    string FirstName,
    string LastName,
    UserRole Role,
    string? PhoneNumber = null,
    string? Country = null,
    string? TimeZone = null) : IRequest<AuthResponseDto>;

public sealed class RegisterUserCommandValidator : AbstractValidator<RegisterUserCommand>
{
    public RegisterUserCommandValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required.")
            .EmailAddress().WithMessage("A valid email address is required.")
            .MaximumLength(254).WithMessage("Email must not exceed 254 characters.");

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Password is required.")
            .MinimumLength(8).WithMessage("Password must be at least 8 characters.")
            .MaximumLength(128)
            .Matches("[A-Z]").WithMessage("Must contain at least one uppercase letter.")
            .Matches("[a-z]").WithMessage("Must contain at least one lowercase letter.")
            .Matches("[0-9]").WithMessage("Must contain at least one number.")
            .Matches("[^a-zA-Z0-9]").WithMessage("Must contain at least one special character.");

        RuleFor(x => x.ConfirmPassword)
            .Equal(x => x.Password).WithMessage("Passwords do not match.");

        RuleFor(x => x.FirstName)
            .NotEmpty().MaximumLength(100)
            .Matches("^[a-zA-Z\\s\\-']+$").WithMessage("First name contains invalid characters.");

        RuleFor(x => x.LastName)
            .NotEmpty().MaximumLength(100)
            .Matches("^[a-zA-Z\\s\\-']+$").WithMessage("Last name contains invalid characters.");

        RuleFor(x => x.Role)
            .IsInEnum()
            .Must(r => r != UserRole.SuperAdmin && r != UserRole.Moderator)
            .WithMessage("This role cannot be self-assigned during registration.");

        RuleFor(x => x.PhoneNumber)
            .Matches(@"^\+?[1-9]\d{1,14}$").WithMessage("Invalid phone number format.")
            .When(x => !string.IsNullOrEmpty(x.PhoneNumber));

        RuleFor(x => x.Country)
            .Length(2, 3).WithMessage("Use ISO country code e.g. GB, US, IN.")
            .Matches("^[A-Za-z]+$").WithMessage("Country code must contain only letters.")
            .When(x => !string.IsNullOrEmpty(x.Country));
    }
}

public sealed class RegisterUserCommandHandler : IRequestHandler<RegisterUserCommand, AuthResponseDto>
{
    private readonly IUserRepository _users;
    private readonly IUnitOfWork _uow;
    private readonly IPasswordHasher _hasher;
    private readonly ITokenService _tokens;
    private readonly IEmailService _email;
    private readonly ICurrentUserService _currentUser;
    private readonly ILogger<RegisterUserCommandHandler> _logger;

    public RegisterUserCommandHandler(
        IUserRepository users,
        IUnitOfWork uow,
        IPasswordHasher hasher,
        ITokenService tokens,
        IEmailService email,
        ICurrentUserService currentUser,
        ILogger<RegisterUserCommandHandler> logger)
    {
        _users = users;
        _uow = uow;
        _hasher = hasher;
        _tokens = tokens;
        _email = email;
        _currentUser = currentUser;
        _logger = logger;
    }

    public async Task<AuthResponseDto> Handle(RegisterUserCommand req, CancellationToken ct)
    {
        _logger.LogInformation("Registering user: {Email}", req.Email);

        if (await _users.ExistsByEmailAsync(req.Email, ct))
            throw new UserAlreadyExistsException(req.Email);

        var passwordHash = _hasher.HashPassword(req.Password);

        var user = User.Create(
            req.Email, passwordHash,
            req.FirstName, req.LastName,
            req.Role, req.Country, req.TimeZone);

        // Generate tokens before persisting — User.Id is set in User.Create()
        // and all token claims come from the in-memory entity, not the DB.
        var ip = _currentUser.IpAddress ?? "unknown";
        var ua = _currentUser.UserAgent ?? "unknown";
        var accessToken = _tokens.GenerateAccessToken(user);
        var refreshTokenValue = _tokens.GenerateRefreshToken();
        var accessExpiry = DateTimeOffset.UtcNow.AddMinutes(15);
        var refreshExpiry = DateTimeOffset.UtcNow.AddDays(30);

        // Add user first (without tokens), then explicitly add the refresh token to
        // the DbSet. This is more reliable than relying on EF's graph traversal of
        // the User's private _refreshTokens list during AddAsync.
        await _users.AddAsync(user, ct);
        var newToken = user.AddRefreshToken(refreshTokenValue, ip, ua, refreshExpiry);
        await _users.AddRefreshTokenAsync(newToken, ct);
        await _uow.SaveChangesAsync(ct);   // Single atomic save: INSERT user + INSERT refresh_token

        // Fire-and-forget — email failure must not block registration
        _ = _email.SendEmailVerificationAsync(
            user.Email, user.FirstName,
            user.EmailVerificationToken!,
            CancellationToken.None);

        _logger.LogInformation("User registered: {UserId}", user.Id);

        return new AuthResponseDto(
            accessToken, refreshTokenValue,
            accessExpiry, refreshExpiry,
            ToProfile(user));
    }

    private static UserProfileDto ToProfile(User u) => new(
        u.Id, u.Email, u.FirstName, u.LastName, u.FullName,
        u.PhoneNumber, u.ProfilePictureUrl, u.IsEmailVerified,
        u.IsActive, u.Roles.Select(r => r.ToString()),
        u.LastLoginAt, u.Country, u.TimeZone, u.CreatedAt);
}
