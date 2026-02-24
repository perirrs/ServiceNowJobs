using FluentValidation;
using MediatR;
using SNHub.Auth.Application.Interfaces;
using SNHub.Auth.Domain.Enums;
using SNHub.Auth.Domain.Exceptions;

namespace SNHub.Auth.Application.Commands.UpdateUserRoles;

public sealed record UpdateUserRolesCommand(
    Guid TargetUserId,
    IReadOnlyList<UserRole> Roles) : IRequest<Unit>;

public sealed class UpdateUserRolesCommandValidator : AbstractValidator<UpdateUserRolesCommand>
{
    public UpdateUserRolesCommandValidator()
    {
        RuleFor(x => x.TargetUserId).NotEmpty().WithMessage("User ID is required.");

        RuleFor(x => x.Roles)
            .NotEmpty().WithMessage("At least one role is required.")
            .Must(r => !r.Contains(UserRole.SuperAdmin))
            .WithMessage("SuperAdmin role cannot be assigned via this endpoint.");
    }
}

public sealed class UpdateUserRolesCommandHandler : IRequestHandler<UpdateUserRolesCommand, Unit>
{
    private readonly IUserRepository _users;
    private readonly IUnitOfWork _uow;
    private readonly ICurrentUserService _currentUser;

    public UpdateUserRolesCommandHandler(
        IUserRepository users, IUnitOfWork uow, ICurrentUserService currentUser)
    {
        _users = users;
        _uow = uow;
        _currentUser = currentUser;
    }

    public async Task<Unit> Handle(UpdateUserRolesCommand request, CancellationToken ct)
    {
        var adminId = _currentUser.UserId
            ?? throw new UnauthorizedAccessException("Admin not authenticated.");

        if (request.TargetUserId == adminId)
            throw new DomainException("You cannot update your own roles.");

        var user = await _users.GetByIdAsync(request.TargetUserId, ct)
            ?? throw new UserNotFoundException($"User {request.TargetUserId} not found.");

        if (user.Roles.Contains(UserRole.SuperAdmin))
            throw new DomainException("Cannot update roles of a SuperAdmin account.");

        // Replace roles: add new ones, remove ones not in the new set
        var toAdd    = request.Roles.Except(user.Roles).ToList();
        var toRemove = user.Roles.Except(request.Roles).ToList();

        foreach (var role in toAdd)    user.AddRole(role);
        foreach (var role in toRemove) user.RemoveRole(role);

        await _users.UpdateAsync(user, ct);
        await _uow.SaveChangesAsync(ct);
        return Unit.Value;
    }
}
