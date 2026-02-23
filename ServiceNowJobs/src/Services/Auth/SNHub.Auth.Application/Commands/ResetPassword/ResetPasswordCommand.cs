using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using SNHub.Auth.Application.Interfaces;
using SNHub.Auth.Domain.Exceptions;

namespace SNHub.Auth.Application.Commands.ResetPassword;

public sealed record ResetPasswordCommand(
    string Email,
    string Token,
    string NewPassword,
    string ConfirmNewPassword) : IRequest<Unit>;

public sealed class ResetPasswordCommandValidator : AbstractValidator<ResetPasswordCommand>
{
    public ResetPasswordCommandValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Token).NotEmpty();
        RuleFor(x => x.NewPassword)
            .NotEmpty().MinimumLength(8).MaximumLength(128)
            .Matches("[A-Z]").WithMessage("Must contain uppercase.")
            .Matches("[a-z]").WithMessage("Must contain lowercase.")
            .Matches("[0-9]").WithMessage("Must contain a number.")
            .Matches("[^a-zA-Z0-9]").WithMessage("Must contain a special character.");
        RuleFor(x => x.ConfirmNewPassword)
            .Equal(x => x.NewPassword).WithMessage("Passwords do not match.");
    }
}

public sealed class ResetPasswordCommandHandler : IRequestHandler<ResetPasswordCommand, Unit>
{
    private readonly IUserRepository _users;
    private readonly IUnitOfWork _uow;
    private readonly IPasswordHasher _hasher;
    private readonly ILogger<ResetPasswordCommandHandler> _logger;

    public ResetPasswordCommandHandler(
        IUserRepository users, IUnitOfWork uow,
        IPasswordHasher hasher, ILogger<ResetPasswordCommandHandler> logger)
    {
        _users = users; _uow = uow; _hasher = hasher; _logger = logger;
    }

    public async Task<Unit> Handle(ResetPasswordCommand req, CancellationToken ct)
    {
        var user = await _users.GetByEmailAsync(req.Email, ct)
            ?? throw new InvalidTokenException("Invalid or expired token.");

        user.ResetPassword(req.Token, _hasher.HashPassword(req.NewPassword));
        await _uow.SaveChangesAsync(ct);

        _logger.LogInformation("Password reset: {UserId}", user.Id);
        return Unit.Value;
    }
}

// ─── Verify Email ─────────────────────────────────────────────────────────────

namespace SNHub.Auth.Application.Commands.VerifyEmail;

public sealed record VerifyEmailCommand(string Email, string Token) : IRequest<Unit>;

public sealed class VerifyEmailCommandValidator : AbstractValidator<VerifyEmailCommand>
{
    public VerifyEmailCommandValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Token).NotEmpty();
    }
}

public sealed class VerifyEmailCommandHandler : IRequestHandler<VerifyEmailCommand, Unit>
{
    private readonly IUserRepository _users;
    private readonly IUnitOfWork _uow;
    private readonly ILogger<VerifyEmailCommandHandler> _logger;

    public VerifyEmailCommandHandler(
        IUserRepository users, IUnitOfWork uow,
        ILogger<VerifyEmailCommandHandler> logger)
    {
        _users = users; _uow = uow; _logger = logger;
    }

    public async Task<Unit> Handle(VerifyEmailCommand req, CancellationToken ct)
    {
        var user = await _users.GetByEmailAsync(req.Email, ct)
            ?? throw new InvalidTokenException("Invalid verification token.");

        user.VerifyEmail(req.Token);
        await _uow.SaveChangesAsync(ct);

        _logger.LogInformation("Email verified: {UserId}", user.Id);
        return Unit.Value;
    }
}
