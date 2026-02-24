using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using SNHub.Auth.Application.Interfaces;

namespace SNHub.Auth.Application.Commands.ResendVerification;

/// <summary>
/// Resends the email verification link to a registered but unverified user.
///
/// Security notes:
///  • Always returns success — never reveals whether the email exists or is verified.
///  • Only sends if the account is active and email is not yet verified.
///  • Regenerates the token (invalidates any old links still in transit).
///  • Token expiry resets to 24 hours from now.
/// </summary>
public sealed record ResendVerificationCommand(string Email) : IRequest<Unit>;

public sealed class ResendVerificationCommandValidator : AbstractValidator<ResendVerificationCommand>
{
    public ResendVerificationCommandValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required.")
            .EmailAddress().WithMessage("A valid email address is required.")
            .MaximumLength(256).WithMessage("Email must not exceed 256 characters.");
    }
}

public sealed class ResendVerificationCommandHandler : IRequestHandler<ResendVerificationCommand, Unit>
{
    private readonly IUserRepository _users;
    private readonly IUnitOfWork _uow;
    private readonly IEmailService _email;
    private readonly ILogger<ResendVerificationCommandHandler> _logger;

    public ResendVerificationCommandHandler(
        IUserRepository users,
        IUnitOfWork uow,
        IEmailService email,
        ILogger<ResendVerificationCommandHandler> logger)
    {
        _users = users;
        _uow = uow;
        _email = email;
        _logger = logger;
    }

    public async Task<Unit> Handle(ResendVerificationCommand req, CancellationToken ct)
    {
        var user = await _users.GetByEmailAsync(req.Email, ct);

        // Silent no-op for unknown, inactive, or already-verified accounts.
        // This prevents email enumeration: the caller never learns whether
        // an account exists or what its verification state is.
        if (user is null || !user.IsActive || user.IsEmailVerified)
        {
            _logger.LogInformation(
                "ResendVerification skipped for {Email} (notFound={NotFound}, inactive={Inactive}, alreadyVerified={Verified})",
                req.Email,
                user is null,
                user is not null && !user.IsActive,
                user is not null && user.IsEmailVerified);

            return Unit.Value;
        }

        // Regenerate token so any previously sent link is immediately invalidated.
        user.GenerateEmailVerificationToken();
        await _uow.SaveChangesAsync(ct);

        // Fire-and-forget — email failure must never block the response.
        _ = _email.SendEmailVerificationAsync(
            user.Email,
            user.FirstName,
            user.EmailVerificationToken!,
            CancellationToken.None);

        _logger.LogInformation("Verification email resent for user {UserId}", user.Id);
        return Unit.Value;
    }
}
