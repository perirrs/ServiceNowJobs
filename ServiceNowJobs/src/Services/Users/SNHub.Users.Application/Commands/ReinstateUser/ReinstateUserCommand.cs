using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using SNHub.Users.Application.Interfaces;
using SNHub.Users.Domain.Exceptions;

namespace SNHub.Users.Application.Commands.ReinstateUser;

public sealed record ReinstateUserCommand(Guid TargetUserId, Guid ReinstatedBy) : IRequest<Unit>;

public sealed class ReinstateUserCommandValidator : AbstractValidator<ReinstateUserCommand>
{
    public ReinstateUserCommandValidator()
    {
        RuleFor(x => x.TargetUserId).NotEmpty();
        RuleFor(x => x.ReinstatedBy).NotEmpty();
    }
}

public sealed class ReinstateUserCommandHandler : IRequestHandler<ReinstateUserCommand, Unit>
{
    private readonly IUserProfileRepository _repo;
    private readonly IUnitOfWork _uow;
    private readonly ILogger<ReinstateUserCommandHandler> _logger;

    public ReinstateUserCommandHandler(
        IUserProfileRepository repo, IUnitOfWork uow,
        ILogger<ReinstateUserCommandHandler> logger)
    { _repo = repo; _uow = uow; _logger = logger; }

    public async Task<Unit> Handle(ReinstateUserCommand req, CancellationToken ct)
    {
        var profile = await _repo.GetByUserIdAsync(req.TargetUserId, ct)
            ?? throw new UserProfileNotFoundException(req.TargetUserId);

        if (!profile.IsDeleted)
            throw new InvalidOperationException($"User {req.TargetUserId} is not deleted.");

        profile.Reinstate();
        await _uow.SaveChangesAsync(ct);

        _logger.LogInformation("User {TargetUserId} reinstated by {ReinstatedBy}",
            req.TargetUserId, req.ReinstatedBy);
        return Unit.Value;
    }
}
