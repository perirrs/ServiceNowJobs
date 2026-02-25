using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using SNHub.Users.Application.Interfaces;
using SNHub.Users.Domain.Exceptions;

namespace SNHub.Users.Application.Commands.SoftDeleteUser;

public sealed record SoftDeleteUserCommand(Guid TargetUserId, Guid DeletedBy) : IRequest<Unit>;

public sealed class SoftDeleteUserCommandValidator : AbstractValidator<SoftDeleteUserCommand>
{
    public SoftDeleteUserCommandValidator()
    {
        RuleFor(x => x.TargetUserId).NotEmpty();
        RuleFor(x => x.DeletedBy).NotEmpty();
    }
}

public sealed class SoftDeleteUserCommandHandler : IRequestHandler<SoftDeleteUserCommand, Unit>
{
    private readonly IUserProfileRepository _repo;
    private readonly IUnitOfWork _uow;
    private readonly ILogger<SoftDeleteUserCommandHandler> _logger;

    public SoftDeleteUserCommandHandler(
        IUserProfileRepository repo, IUnitOfWork uow,
        ILogger<SoftDeleteUserCommandHandler> logger)
    { _repo = repo; _uow = uow; _logger = logger; }

    public async Task<Unit> Handle(SoftDeleteUserCommand req, CancellationToken ct)
    {
        var profile = await _repo.GetByUserIdAsync(req.TargetUserId, ct)
            ?? throw new UserProfileNotFoundException(req.TargetUserId);

        if (profile.IsDeleted)
            throw new UserAlreadyDeletedException(req.TargetUserId);

        profile.SoftDelete(req.DeletedBy);
        await _uow.SaveChangesAsync(ct);

        _logger.LogWarning("User {TargetUserId} soft-deleted by {DeletedBy}",
            req.TargetUserId, req.DeletedBy);
        return Unit.Value;
    }
}
