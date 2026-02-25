using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using SNHub.Jobs.Application.DTOs;
using SNHub.Jobs.Application.Interfaces;
using SNHub.Jobs.Domain.Enums;
using SNHub.Jobs.Domain.Exceptions;
using System.Text.Json;

namespace SNHub.Jobs.Application.Commands.UpdateJob;

public sealed record UpdateJobCommand(
    Guid JobId,
    Guid RequesterId,
    string Title,
    string Description,
    string? Requirements,
    string? Benefits,
    JobType JobType,
    WorkMode WorkMode,
    ExperienceLevel ExperienceLevel,
    string? Location,
    string? Country,
    decimal? SalaryMin,
    decimal? SalaryMax,
    string? SalaryCurrency,
    bool IsSalaryVisible,
    IReadOnlyList<string>? SkillsRequired,
    IReadOnlyList<string>? CertificationsRequired,
    IReadOnlyList<string>? ServiceNowVersions,
    DateTimeOffset? ExpiresAt) : IRequest<JobDto>;

public sealed class UpdateJobCommandValidator : AbstractValidator<UpdateJobCommand>
{
    public UpdateJobCommandValidator()
    {
        RuleFor(x => x.JobId).NotEmpty();
        RuleFor(x => x.RequesterId).NotEmpty();
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).NotEmpty().MaximumLength(10_000);
        RuleFor(x => x.Requirements).MaximumLength(5_000).When(x => x.Requirements is not null);
        RuleFor(x => x.Benefits).MaximumLength(5_000).When(x => x.Benefits is not null);
        RuleFor(x => x.SalaryMin).GreaterThan(0).When(x => x.SalaryMin.HasValue);
        RuleFor(x => x.SalaryMax)
            .GreaterThanOrEqualTo(x => x.SalaryMin!.Value)
            .WithMessage("SalaryMax must be ≥ SalaryMin.")
            .When(x => x.SalaryMin.HasValue && x.SalaryMax.HasValue);
        RuleFor(x => x.ExpiresAt)
            .GreaterThan(DateTimeOffset.UtcNow).WithMessage("ExpiresAt must be in the future.")
            .When(x => x.ExpiresAt.HasValue);
    }
}

public sealed class UpdateJobCommandHandler : IRequestHandler<UpdateJobCommand, JobDto>
{
    private readonly IJobRepository _repo;
    private readonly IUnitOfWork _uow;
    private readonly ILogger<UpdateJobCommandHandler> _logger;

    public UpdateJobCommandHandler(IJobRepository repo, IUnitOfWork uow, ILogger<UpdateJobCommandHandler> logger)
    {
        _repo = repo;
        _uow = uow;
        _logger = logger;
    }

    public async Task<JobDto> Handle(UpdateJobCommand req, CancellationToken ct)
    {
        var job = await _repo.GetByIdAsync(req.JobId, ct)
            ?? throw new JobNotFoundException(req.JobId);

        if (job.EmployerId != req.RequesterId)
            throw new JobAccessDeniedException();

        if (job.Status == Domain.Enums.JobStatus.Closed)
            throw new JobNotActiveException("Cannot update a closed job.");

        job.Update(req.Title, req.Description, req.Requirements, req.Benefits,
            req.JobType, req.WorkMode, req.ExperienceLevel, req.Location, req.Country,
            req.SalaryMin, req.SalaryMax, req.SalaryCurrency, req.IsSalaryVisible, req.ExpiresAt);

        if (req.SkillsRequired is not null)
            job.SetSkills(JsonSerializer.Serialize(req.SkillsRequired));

        if (req.CertificationsRequired is not null)
            job.SetCertifications(JsonSerializer.Serialize(req.CertificationsRequired));

        if (req.ServiceNowVersions is not null)
            job.SetServiceNowVersions(JsonSerializer.Serialize(req.ServiceNowVersions));

        await _repo.UpdateAsync(job, ct);
        await _uow.SaveChangesAsync(ct);

        _logger.LogInformation("Job {JobId} updated by {RequesterId}", job.Id, req.RequesterId);
        return JobMapper.ToDto(job);
    }
}
