using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using SNHub.Applications.Application.DTOs;
using SNHub.Applications.Application.Interfaces;
using SNHub.Applications.Domain.Enums;
using SNHub.Applications.Domain.Exceptions;

namespace SNHub.Applications.Application.Commands.UpdateStatus;

public sealed record UpdateApplicationStatusCommand(
    Guid ApplicationId,
    Guid RequesterId,         // employer/hiring manager user ID
    ApplicationStatus NewStatus,
    string? Notes,
    string? RejectionReason) : IRequest<ApplicationDto>;

public sealed class UpdateApplicationStatusCommandValidator
    : AbstractValidator<UpdateApplicationStatusCommand>
{
    public UpdateApplicationStatusCommandValidator()
    {
        RuleFor(x => x.ApplicationId).NotEmpty();
        RuleFor(x => x.RequesterId).NotEmpty();
        RuleFor(x => x.NewStatus).IsInEnum().WithMessage("Invalid application status.");
        RuleFor(x => x.Notes)
            .MaximumLength(2_000).When(x => x.Notes is not null);
        RuleFor(x => x.RejectionReason)
            .NotEmpty().WithMessage("Rejection reason is required when rejecting.")
            .MaximumLength(1_000)
            .When(x => x.NewStatus == ApplicationStatus.Rejected);
    }
}

public sealed class UpdateApplicationStatusCommandHandler
    : IRequestHandler<UpdateApplicationStatusCommand, ApplicationDto>
{
    private readonly IApplicationRepository _repo;
    private readonly IUnitOfWork _uow;
    private readonly ILogger<UpdateApplicationStatusCommandHandler> _logger;

    public UpdateApplicationStatusCommandHandler(
        IApplicationRepository repo, IUnitOfWork uow,
        ILogger<UpdateApplicationStatusCommandHandler> logger)
    { _repo = repo; _uow = uow; _logger = logger; }

    public async Task<ApplicationDto> Handle(UpdateApplicationStatusCommand req, CancellationToken ct)
    {
        var app = await _repo.GetByIdAsync(req.ApplicationId, ct)
            ?? throw new ApplicationNotFoundException(req.ApplicationId);

        app.UpdateStatus(req.NewStatus, req.Notes, req.RejectionReason);
        await _uow.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Application {Id} → {Status} by {RequesterId}",
            app.Id, app.Status, req.RequesterId);

        return ApplicationMapper.ToDto(app);
    }
}
