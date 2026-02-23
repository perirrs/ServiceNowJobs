using MediatR;
using Microsoft.Extensions.Logging;
using SNHub.Applications.Application.Interfaces;
using SNHub.Applications.Domain.Exceptions;

namespace SNHub.Applications.Application.Commands.Withdraw;

public sealed record WithdrawApplicationCommand(Guid Id, Guid CandidateId) : IRequest<Unit>;

public sealed class WithdrawApplicationCommandHandler : IRequestHandler<WithdrawApplicationCommand, Unit>
{
    private readonly IApplicationRepository _repo;
    private readonly IUnitOfWork _uow;
    private readonly ILogger<WithdrawApplicationCommandHandler> _logger;

    public WithdrawApplicationCommandHandler(IApplicationRepository repo, IUnitOfWork uow,
        ILogger<WithdrawApplicationCommandHandler> logger) => (_repo, _uow, _logger) = (repo, uow, logger);

    public async Task<Unit> Handle(WithdrawApplicationCommand req, CancellationToken ct)
    {
        var app = await _repo.GetByIdAsync(req.Id, ct)
            ?? throw new ApplicationNotFoundException(req.Id);
        if (app.CandidateId != req.CandidateId)
            throw new DomainException("You can only withdraw your own applications.");
        app.Withdraw();
        await _uow.SaveChangesAsync(ct);
        return Unit.Value;
    }
}
