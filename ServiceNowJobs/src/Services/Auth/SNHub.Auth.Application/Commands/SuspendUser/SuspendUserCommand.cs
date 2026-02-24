using FluentValidation;
using MediatR;
using SNHub.Auth.Application.Interfaces;
using SNHub.Auth.Domain.Exceptions;

namespace SNHub.Auth.Application.Commands.SuspendUser;

public sealed record SuspendUserCommand(Guid TargetUserId, string Reason) : IRequest<Unit>;

public sealed class SuspendUserCommandValidator : AbstractValidator<SuspendUserCommand>
{
    public SuspendUserCommandValidator()
    {
        RuleFor(x => x.TargetUserId).NotEmpty().WithMessage("User ID is required.");
        RuleFor(x => x.Reason)
            .NotEmpty().WithMessage("Suspension reason is required.")
            .MaximumLength(1000);
    }
}

public sealed class SuspendUserCommandHandler : IRequestHandler<SuspendUserCommand, Unit>
{
    private readonly IUserRepository _users;
    private readonly IUnitOfWork _uow;
    private readonly ICurrentUserService _currentUser;

    public SuspendUserCommandHandler(
        IUserRepository users, IUnitOfWork uow, ICurrentUserService currentUser)
    {
        _users = users;
        _uow = uow;
        _currentUser = currentUser;
    }

    public async Task<Unit> Handle(SuspendUserCommand request, CancellationToken ct)
    {
        var adminId = _currentUser.UserId
            ?? throw new UnauthorizedAccessException("Admin not authenticated.");

        if (request.TargetUserId == adminId)
            throw new DomainException("You cannot suspend your own account.");

        // Load with tokens so RevokeAllRefreshTokens works inside Suspend()
        var user = await _users.GetByIdWithTokensAsync(request.TargetUserId, ct)
            ?? throw new UserNotFoundException($"User {request.TargetUserId} not found.");

        user.Suspend(request.Reason, _currentUser.Email ?? adminId.ToString());
        await _uow.SaveChangesAsync(ct);
        return Unit.Value;
    }
}
