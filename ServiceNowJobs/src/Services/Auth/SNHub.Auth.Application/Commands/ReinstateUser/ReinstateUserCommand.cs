using FluentValidation;
using MediatR;
using SNHub.Auth.Application.Interfaces;
using SNHub.Auth.Domain.Exceptions;

namespace SNHub.Auth.Application.Commands.ReinstateUser;

public sealed record ReinstateUserCommand(Guid TargetUserId) : IRequest<Unit>;

public sealed class ReinstateUserCommandValidator : AbstractValidator<ReinstateUserCommand>
{
    public ReinstateUserCommandValidator()
    {
        RuleFor(x => x.TargetUserId).NotEmpty().WithMessage("User ID is required.");
    }
}

public sealed class ReinstateUserCommandHandler : IRequestHandler<ReinstateUserCommand, Unit>
{
    private readonly IUserRepository _users;
    private readonly IUnitOfWork _uow;
    private readonly ICurrentUserService _currentUser;

    public ReinstateUserCommandHandler(
        IUserRepository users, IUnitOfWork uow, ICurrentUserService currentUser)
    {
        _users = users;
        _uow = uow;
        _currentUser = currentUser;
    }

    public async Task<Unit> Handle(ReinstateUserCommand request, CancellationToken ct)
    {
        var user = await _users.GetByIdAsync(request.TargetUserId, ct)
            ?? throw new UserNotFoundException($"User {request.TargetUserId} not found.");

        if (!user.IsSuspended)
            throw new DomainException("User is not suspended.");

        user.Reinstate(_currentUser.Email ?? "system");
        await _users.UpdateAsync(user, ct);
        await _uow.SaveChangesAsync(ct);
        return Unit.Value;
    }
}
