using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using SNHub.Applications.Application.DTOs;
using SNHub.Applications.Application.Interfaces;
using SNHub.Applications.Domain.Entities;
using SNHub.Applications.Domain.Enums;
using SNHub.Applications.Domain.Exceptions;

namespace SNHub.Applications.Application.Commands.ApplyToJob;

public sealed record ApplyToJobCommand(
    Guid JobId,
    Guid CandidateId,
    string? CoverLetter,
    string? CvUrl) : IRequest<ApplicationDto>;

public sealed class ApplyToJobCommandValidator : AbstractValidator<ApplyToJobCommand>
{
    public ApplyToJobCommandValidator()
    {
        RuleFor(x => x.JobId).NotEmpty().WithMessage("JobId is required.");
        RuleFor(x => x.CandidateId).NotEmpty().WithMessage("CandidateId is required.");
        RuleFor(x => x.CoverLetter)
            .MaximumLength(5_000).WithMessage("Cover letter must not exceed 5,000 characters.")
            .When(x => x.CoverLetter is not null);
        RuleFor(x => x.CvUrl)
            .MaximumLength(2_048).WithMessage("CV URL must not exceed 2,048 characters.")
            .Must(url => Uri.TryCreate(url, UriKind.Absolute, out _))
            .WithMessage("CV URL must be a valid absolute URL.")
            .When(x => x.CvUrl is not null);
    }
}

public sealed class ApplyToJobCommandHandler : IRequestHandler<ApplyToJobCommand, ApplicationDto>
{
    private readonly IApplicationRepository _repo;
    private readonly ISubscriptionService   _subscriptions;
    private readonly IUnitOfWork            _uow;
    private readonly ILogger<ApplyToJobCommandHandler> _logger;

    public ApplyToJobCommandHandler(
        IApplicationRepository repo,
        ISubscriptionService subscriptions,
        IUnitOfWork uow,
        ILogger<ApplyToJobCommandHandler> logger)
    {
        _repo          = repo;
        _subscriptions = subscriptions;
        _uow           = uow;
        _logger        = logger;
    }

    public async Task<ApplicationDto> Handle(ApplyToJobCommand req, CancellationToken ct)
    {
        // ── Guard: no duplicate applications ──────────────────────────────────
        if (await _repo.ExistsAsync(req.JobId, req.CandidateId, ct))
            throw new DuplicateApplicationException(req.JobId);

        // ── Guard: subscription plan application limit ────────────────────────
        var plan  = await _subscriptions.GetCandidatePlanAsync(req.CandidateId, ct);
        var limit = plan.MonthlyApplicationLimit();

        if (!plan.IsUnlimited())
        {
            var countThisMonth = await _repo.GetCountThisMonthAsync(req.CandidateId, ct);
            if (countThisMonth >= limit)
                throw new SubscriptionLimitExceededException(plan.ToString(), limit);
        }

        // ── Create and persist ────────────────────────────────────────────────
        var app = JobApplication.Create(req.JobId, req.CandidateId, req.CoverLetter, req.CvUrl);
        await _repo.AddAsync(app, ct);
        await _uow.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Application {Id} created: candidate {CandidateId} → job {JobId} (plan: {Plan})",
            app.Id, app.CandidateId, app.JobId, plan);

        return ApplicationMapper.ToDto(app);
    }
}
