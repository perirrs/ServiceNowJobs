using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using SNHub.Auth.Application.DTOs;
using SNHub.Auth.Application.Interfaces;
using SNHub.Auth.Domain.Exceptions;

namespace SNHub.Auth.Application.Commands.LoginUser;

public sealed record LoginUserCommand(
    string Email,
    string Password,
    bool RememberMe = false) : IRequest<AuthResponseDto>;

public sealed class LoginUserCommandValidator : AbstractValidator<LoginUserCommand>
{
    public LoginUserCommandValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().EmailAddress();

        RuleFor(x => x.Password)
            .NotEmpty().MaximumLength(128);
    }
}

public sealed class LoginUserCommandHandler : IRequestHandler<LoginUserCommand, AuthResponseDto>
{
    private readonly IUserRepository _users;
    private readonly IUnitOfWork _uow;
    private readonly IPasswordHasher _hasher;
    private readonly ITokenService _tokens;
    private readonly ICurrentUserService _currentUser;
    private readonly ILogger<LoginUserCommandHandler> _logger;

    public LoginUserCommandHandler(
        IUserRepository users,
        IUnitOfWork uow,
        IPasswordHasher hasher,
        ITokenService tokens,
        ICurrentUserService currentUser,
        ILogger<LoginUserCommandHandler> logger)
    {
        _users = users;
        _uow = uow;
        _hasher = hasher;
        _tokens = tokens;
        _currentUser = currentUser;
        _logger = logger;
    }

    public async Task<AuthResponseDto> Handle(LoginUserCommand req, CancellationToken ct)
    {
        var user = await _users.GetByEmailWithTokensAsync(req.Email, ct);

        // Always same error — never reveal whether email exists
        if (user is null || !user.IsActive)
            throw new InvalidCredentialsException();

        if (user.IsSuspended)
            throw new AccountSuspendedException(user.SuspensionReason);

        if (user.IsLockedOut)
            throw new AccountLockedException(user.LockedOutUntil);

        if (!_hasher.VerifyPassword(req.Password, user.PasswordHash))
        {
            user.RecordFailedLogin();
            await _uow.SaveChangesAsync(ct);
            throw new InvalidCredentialsException();
        }

        var ip = _currentUser.IpAddress ?? "unknown";
        var ua = _currentUser.UserAgent ?? "unknown";
        var refreshDays = req.RememberMe ? 90 : 30;

        var accessToken = _tokens.GenerateAccessToken(user);
        var refreshValue = _tokens.GenerateRefreshToken();
        var accessExpiry = DateTimeOffset.UtcNow.AddMinutes(15);
        var refreshExpiry = DateTimeOffset.UtcNow.AddDays(refreshDays);

        user.RecordSuccessfulLogin(ip);
        var newToken = user.AddRefreshToken(refreshValue, ip, ua, refreshExpiry);
        await _users.AddRefreshTokenAsync(newToken, ct);  // explicit DbSet.Add — EF can't detect List<T> mutations

        await _uow.SaveChangesAsync(ct);

        _logger.LogInformation("Login successful: {UserId}", user.Id);

        return new AuthResponseDto(
            accessToken, refreshValue,
            accessExpiry, refreshExpiry,
            new UserProfileDto(
                user.Id, user.Email, user.FirstName, user.LastName, user.FullName,
                user.PhoneNumber, user.ProfilePictureUrl, user.IsEmailVerified,
                user.IsActive, user.Roles.Select(r => r.ToString()),
                user.LastLoginAt, user.Country, user.TimeZone, user.CreatedAt));
    }
}
