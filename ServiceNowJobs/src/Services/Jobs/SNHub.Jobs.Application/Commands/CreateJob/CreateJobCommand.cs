using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using SNHub.Jobs.Application.DTOs;
using SNHub.Jobs.Application.Interfaces;
using SNHub.Jobs.Domain.Entities;
using SNHub.Jobs.Domain.Enums;
using System.Text.Json;

namespace SNHub.Jobs.Application.Commands.CreateJob;

public sealed record CreateJobCommand(
    Guid EmployerId,
    string Title,
    string Description,
    string? Requirements,
    string? Benefits,
    string? CompanyName,
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
    bool PublishImmediately,
    DateTimeOffset? ExpiresAt) : IRequest<JobDto>;

public sealed class CreateJobCommandValidator : AbstractValidator<CreateJobCommand>
{
    public CreateJobCommandValidator()
    {
        RuleFor(x => x.EmployerId).NotEmpty().WithMessage("EmployerId is required.");
        RuleFor(x => x.Title).NotEmpty().WithMessage("Title is required.").MaximumLength(200);
        RuleFor(x => x.Description).NotEmpty().WithMessage("Description is required.").MaximumLength(10_000);
        RuleFor(x => x.Requirements).MaximumLength(5_000).When(x => x.Requirements is not null);
        RuleFor(x => x.Benefits).MaximumLength(5_000).When(x => x.Benefits is not null);
        RuleFor(x => x.CompanyName).MaximumLength(200).When(x => x.CompanyName is not null);
        RuleFor(x => x.SalaryMin).GreaterThan(0).When(x => x.SalaryMin.HasValue);
        RuleFor(x => x.SalaryMax)
            .GreaterThanOrEqualTo(x => x.SalaryMin!.Value)
            .WithMessage("SalaryMax must be ≥ SalaryMin.")
            .When(x => x.SalaryMin.HasValue && x.SalaryMax.HasValue);
        RuleFor(x => x.SalaryCurrency)
            .Length(3).WithMessage("Currency must be a 3-letter ISO code (e.g. USD).")
            .When(x => x.SalaryCurrency is not null);
        RuleFor(x => x.ExpiresAt)
            .GreaterThan(DateTimeOffset.UtcNow).WithMessage("ExpiresAt must be in the future.")
            .When(x => x.ExpiresAt.HasValue);
    }
}

public sealed class CreateJobCommandHandler : IRequestHandler<CreateJobCommand, JobDto>
{
    private readonly IJobRepository _repo;
    private readonly IUnitOfWork _uow;
    private readonly ILogger<CreateJobCommandHandler> _logger;

    public CreateJobCommandHandler(IJobRepository repo, IUnitOfWork uow, ILogger<CreateJobCommandHandler> logger)
    {
        _repo = repo;
        _uow = uow;
        _logger = logger;
    }

    public async Task<JobDto> Handle(CreateJobCommand req, CancellationToken ct)
    {
        var job = Job.Create(
            req.EmployerId, req.Title, req.Description,
            req.JobType, req.WorkMode, req.ExperienceLevel,
            req.Location, req.Country, req.CompanyName,
            req.SalaryMin, req.SalaryMax, req.SalaryCurrency,
            req.IsSalaryVisible, req.ExpiresAt);

        if (req.SkillsRequired?.Count > 0)
            job.SetSkills(JsonSerializer.Serialize(req.SkillsRequired));

        if (req.CertificationsRequired?.Count > 0)
            job.SetCertifications(JsonSerializer.Serialize(req.CertificationsRequired));

        if (req.ServiceNowVersions?.Count > 0)
            job.SetServiceNowVersions(JsonSerializer.Serialize(req.ServiceNowVersions));

        if (req.PublishImmediately) job.Publish();

        await _repo.AddAsync(job, ct);
        await _uow.SaveChangesAsync(ct);

        _logger.LogInformation("Job {JobId} created by employer {EmployerId}", job.Id, job.EmployerId);
        return JobMapper.ToDto(job);
    }
}
