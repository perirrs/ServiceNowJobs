using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using SNHub.Auth.Application.Interfaces;
using SNHub.Auth.Domain.Exceptions;

namespace SNHub.Auth.Application.Commands.ForgotPassword;

public sealed record ForgotPasswordCommand(string Email) : IRequest<Unit>;

public sealed class ForgotPasswordCommandValidator : AbstractValidator<ForgotPasswordCommand>
{
    public ForgotPasswordCommandValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
    }
}

public sealed class ForgotPasswordCommandHandler : IRequestHandler<ForgotPasswordCommand, Unit>
{
    private readonly IUserRepository _users;
    private readonly IUnitOfWork _uow;
    private readonly IEmailService _email;
    private readonly ILogger<ForgotPasswordCommandHandler> _logger;

    public ForgotPasswordCommandHandler(
        IUserRepository users, IUnitOfWork uow,
        IEmailService email, ILogger<ForgotPasswordCommandHandler> logger)
    {
        _users = users; _uow = uow;
        _email = email; _logger = logger;
    }

    public async Task<Unit> Handle(ForgotPasswordCommand req, CancellationToken ct)
    {
        var user = await _users.GetByEmailAsync(req.Email, ct);

        // Always return success — never reveal if email exists
        if (user is null || !user.IsActive)
        {
            _logger.LogWarning("Password reset for unknown email: {Email}", req.Email);
            return Unit.Value;
        }

        user.GeneratePasswordResetToken();
        await _uow.SaveChangesAsync(ct);

        _ = _email.SendPasswordResetAsync(
            user.Email, user.FirstName,
            user.PasswordResetToken!,
            CancellationToken.None);

        return Unit.Value;
    }
}
