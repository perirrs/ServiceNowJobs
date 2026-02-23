using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using SNHub.Applications.Application.Commands.ApplyToJob;
using SNHub.Applications.Application.DTOs;
using SNHub.Applications.Application.Interfaces;
using SNHub.Applications.Domain.Enums;
using SNHub.Applications.Domain.Exceptions;

namespace SNHub.Applications.Application.Commands.UpdateStatus;

public sealed record UpdateApplicationStatusCommand(
    Guid Id, ApplicationStatus NewStatus,
    string? Notes, string? RejectionReason) : IRequest<ApplicationDto>;

public sealed class UpdateApplicationStatusCommandValidator : AbstractValidator<UpdateApplicationStatusCommand>
{
    public UpdateApplicationStatusCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.NewStatus).IsInEnum();
    }
}

public sealed class UpdateApplicationStatusCommandHandler : IRequestHandler<UpdateApplicationStatusCommand, ApplicationDto>
{
    private readonly IApplicationRepository _repo;
    private readonly IUnitOfWork _uow;
    private readonly ILogger<UpdateApplicationStatusCommandHandler> _logger;

    public UpdateApplicationStatusCommandHandler(IApplicationRepository repo, IUnitOfWork uow,
        ILogger<UpdateApplicationStatusCommandHandler> logger) => (_repo, _uow, _logger) = (repo, uow, logger);

    public async Task<ApplicationDto> Handle(UpdateApplicationStatusCommand req, CancellationToken ct)
    {
        var app = await _repo.GetByIdAsync(req.Id, ct)
            ?? throw new ApplicationNotFoundException(req.Id);
        app.UpdateStatus(req.NewStatus, req.Notes, req.RejectionReason);
        await _uow.SaveChangesAsync(ct);
        _logger.LogInformation("Application {Id} status → {Status}", app.Id, app.Status);
        return ApplyToJobCommandHandler.ToDto(app);
    }
}
