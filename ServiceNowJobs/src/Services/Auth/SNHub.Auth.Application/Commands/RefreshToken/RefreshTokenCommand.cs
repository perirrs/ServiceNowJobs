using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using SNHub.Auth.Application.DTOs;
using SNHub.Auth.Application.Interfaces;
using SNHub.Auth.Domain.Exceptions;

namespace SNHub.Auth.Application.Commands.RefreshToken;

public sealed record RefreshTokenCommand(
    string AccessToken,
    string RefreshToken) : IRequest<TokenDto>;

public sealed class RefreshTokenCommandValidator : AbstractValidator<RefreshTokenCommand>
{
    public RefreshTokenCommandValidator()
    {
        RuleFor(x => x.AccessToken).NotEmpty();
        RuleFor(x => x.RefreshToken).NotEmpty();
    }
}

public sealed class RefreshTokenCommandHandler : IRequestHandler<RefreshTokenCommand, TokenDto>
{
    private readonly IUserRepository _users;
    private readonly IUnitOfWork _uow;
    private readonly ITokenService _tokens;
    private readonly ICurrentUserService _currentUser;
    private readonly ILogger<RefreshTokenCommandHandler> _logger;

    public RefreshTokenCommandHandler(
        IUserRepository users,
        IUnitOfWork uow,
        ITokenService tokens,
        ICurrentUserService currentUser,
        ILogger<RefreshTokenCommandHandler> logger)
    {
        _users = users;
        _uow = uow;
        _tokens = tokens;
        _currentUser = currentUser;
        _logger = logger;
    }

    public async Task<TokenDto> Handle(RefreshTokenCommand req, CancellationToken ct)
    {
        var user = await _users.GetByRefreshTokenAsync(req.RefreshToken, ct)
            ?? throw new InvalidTokenException("Invalid refresh token.");

        if (user.IsSuspended)
            throw new AccountSuspendedException(user.SuspensionReason);

        var ip = _currentUser.IpAddress ?? "unknown";
        var ua = _currentUser.UserAgent ?? "unknown";

        user.RevokeRefreshToken(req.RefreshToken, ip, "Token refresh");

        var newAccess = _tokens.GenerateAccessToken(user);
        var newRefresh = _tokens.GenerateRefreshToken();
        var accessExpiry = DateTimeOffset.UtcNow.AddMinutes(15);
        var refreshExpiry = DateTimeOffset.UtcNow.AddDays(30);

        user.AddRefreshToken(newRefresh, ip, ua, refreshExpiry);
        await _uow.SaveChangesAsync(ct);

        _logger.LogInformation("Token refreshed: {UserId}", user.Id);

        return new TokenDto(newAccess, newRefresh, accessExpiry, refreshExpiry);
    }
}
