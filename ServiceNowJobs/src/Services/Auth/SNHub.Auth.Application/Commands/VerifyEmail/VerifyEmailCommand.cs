using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using SNHub.Auth.Application.Interfaces;
using SNHub.Auth.Domain.Exceptions;

namespace SNHub.Auth.Application.Commands.VerifyEmail;

/// <summary>
/// Verifies a user's email address using the token sent after registration.
/// </summary>
public sealed record VerifyEmailCommand(string Email, string Token) : IRequest<Unit>;

public sealed class VerifyEmailCommandValidator : AbstractValidator<VerifyEmailCommand>
{
    public VerifyEmailCommandValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required.")
            .EmailAddress().WithMessage("A valid email address is required.")
            .MaximumLength(256);

        RuleFor(x => x.Token)
            .NotEmpty().WithMessage("Verification token is required.");
    }
}

public sealed class VerifyEmailCommandHandler : IRequestHandler<VerifyEmailCommand, Unit>
{
    private readonly IUserRepository _users;
    private readonly IUnitOfWork _uow;
    private readonly ILogger<VerifyEmailCommandHandler> _logger;

    public VerifyEmailCommandHandler(
        IUserRepository users,
        IUnitOfWork uow,
        ILogger<VerifyEmailCommandHandler> logger)
    {
        _users = users;
        _uow = uow;
        _logger = logger;
    }

    public async Task<Unit> Handle(VerifyEmailCommand req, CancellationToken ct)
    {
        // InvalidTokenException is intentionally the same error for both
        // "user not found" and "wrong token" — prevents email enumeration.
        var user = await _users.GetByEmailAsync(req.Email, ct)
            ?? throw new InvalidTokenException("The verification link is invalid or has expired.");

        // Domain method throws InvalidTokenException on bad/expired token
        // and DomainException if already verified.
        user.VerifyEmail(req.Token);

        await _uow.SaveChangesAsync(ct);

        _logger.LogInformation("Email verified for user {UserId}", user.Id);

        return Unit.Value;
    }
}
