using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using SNHub.Applications.Application.Interfaces;
using SNHub.Applications.Domain.Exceptions;

namespace SNHub.Applications.Application.Commands.Withdraw;

public sealed record WithdrawApplicationCommand(Guid ApplicationId, Guid CandidateId) : IRequest<Unit>;

public sealed class WithdrawApplicationCommandValidator : AbstractValidator<WithdrawApplicationCommand>
{
    public WithdrawApplicationCommandValidator()
    {
        RuleFor(x => x.ApplicationId).NotEmpty();
        RuleFor(x => x.CandidateId).NotEmpty();
    }
}

public sealed class WithdrawApplicationCommandHandler : IRequestHandler<WithdrawApplicationCommand, Unit>
{
    private readonly IApplicationRepository _repo;
    private readonly IUnitOfWork _uow;
    private readonly ILogger<WithdrawApplicationCommandHandler> _logger;

    public WithdrawApplicationCommandHandler(
        IApplicationRepository repo, IUnitOfWork uow,
        ILogger<WithdrawApplicationCommandHandler> logger)
    { _repo = repo; _uow = uow; _logger = logger; }

    public async Task<Unit> Handle(WithdrawApplicationCommand req, CancellationToken ct)
    {
        var app = await _repo.GetByIdAsync(req.ApplicationId, ct)
            ?? throw new ApplicationNotFoundException(req.ApplicationId);

        if (app.CandidateId != req.CandidateId)
            throw new ApplicationAccessDeniedException();

        app.Withdraw();
        await _uow.SaveChangesAsync(ct);

        _logger.LogInformation("Application {Id} withdrawn by {CandidateId}", app.Id, req.CandidateId);
        return Unit.Value;
    }
}
