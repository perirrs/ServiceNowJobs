using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using SNHub.Auth.Application.Interfaces;
using SNHub.Auth.Domain.Exceptions;

namespace SNHub.Auth.Application.Commands.RevokeToken;

public sealed record RevokeTokenCommand(string RefreshToken) : IRequest<Unit>;

public sealed class RevokeTokenCommandValidator : AbstractValidator<RevokeTokenCommand>
{
    public RevokeTokenCommandValidator()
    {
        RuleFor(x => x.RefreshToken).NotEmpty();
    }
}

public sealed class RevokeTokenCommandHandler : IRequestHandler<RevokeTokenCommand, Unit>
{
    private readonly IUserRepository _users;
    private readonly IUnitOfWork _uow;
    private readonly ICurrentUserService _currentUser;
    private readonly ILogger<RevokeTokenCommandHandler> _logger;

    public RevokeTokenCommandHandler(
        IUserRepository users,
        IUnitOfWork uow,
        ICurrentUserService currentUser,
        ILogger<RevokeTokenCommandHandler> logger)
    {
        _users = users;
        _uow = uow;
        _currentUser = currentUser;
        _logger = logger;
    }

    public async Task<Unit> Handle(RevokeTokenCommand req, CancellationToken ct)
    {
        var user = await _users.GetByRefreshTokenAsync(req.RefreshToken, ct)
            ?? throw new InvalidTokenException("Token not found.");

        user.RevokeRefreshToken(req.RefreshToken, _currentUser.IpAddress ?? "unknown", "Logout");
        await _uow.SaveChangesAsync(ct);

        _logger.LogInformation("Token revoked: {UserId}", user.Id);
        return Unit.Value;
    }
}
