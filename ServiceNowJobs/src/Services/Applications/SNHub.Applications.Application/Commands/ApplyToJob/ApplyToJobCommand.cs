using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using SNHub.Applications.Application.DTOs;
using SNHub.Applications.Application.Interfaces;
using SNHub.Applications.Domain.Entities;
using SNHub.Applications.Domain.Exceptions;

namespace SNHub.Applications.Application.Commands.ApplyToJob;

public sealed record ApplyToJobCommand(
    Guid JobId, Guid CandidateId,
    string? CoverLetter, string? CvUrl) : IRequest<ApplicationDto>;

public sealed class ApplyToJobCommandValidator : AbstractValidator<ApplyToJobCommand>
{
    public ApplyToJobCommandValidator()
    {
        RuleFor(x => x.JobId).NotEmpty();
        RuleFor(x => x.CandidateId).NotEmpty();
        RuleFor(x => x.CoverLetter).MaximumLength(5000).When(x => x.CoverLetter != null);
    }
}

public sealed class ApplyToJobCommandHandler : IRequestHandler<ApplyToJobCommand, ApplicationDto>
{
    private readonly IApplicationRepository _repo;
    private readonly IUnitOfWork _uow;
    private readonly ILogger<ApplyToJobCommandHandler> _logger;

    public ApplyToJobCommandHandler(IApplicationRepository repo, IUnitOfWork uow,
        ILogger<ApplyToJobCommandHandler> logger) => (_repo, _uow, _logger) = (repo, uow, logger);

    public async Task<ApplicationDto> Handle(ApplyToJobCommand req, CancellationToken ct)
    {
        if (await _repo.ExistsAsync(req.JobId, req.CandidateId, ct))
            throw new DuplicateApplicationException(req.JobId);
        var app = JobApplication.Create(req.JobId, req.CandidateId, req.CoverLetter, req.CvUrl);
        await _repo.AddAsync(app, ct);
        await _uow.SaveChangesAsync(ct);
        _logger.LogInformation("Application {Id} created for job {JobId}", app.Id, app.JobId);
        return ToDto(app);
    }

    public static ApplicationDto ToDto(JobApplication a) => new(
        a.Id, a.JobId, a.CandidateId, a.Status.ToString(),
        a.CoverLetter, a.CvUrl, a.EmployerNotes, a.RejectionReason,
        a.AppliedAt, a.UpdatedAt, a.StatusChangedAt);
}
