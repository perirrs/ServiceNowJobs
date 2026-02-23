using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using SNHub.Jobs.Application.DTOs;
using SNHub.Jobs.Application.Interfaces;
using SNHub.Jobs.Domain.Entities;
using SNHub.Jobs.Domain.Enums;

namespace SNHub.Jobs.Application.Commands.CreateJob;

public sealed record CreateJobCommand(
    Guid EmployerId, string Title, string Description,
    string? Requirements, string? Benefits, string? CompanyName,
    JobType JobType, WorkMode WorkMode, ExperienceLevel ExperienceLevel,
    string? Location, string? Country,
    decimal? SalaryMin, decimal? SalaryMax, string? SalaryCurrency, bool IsSalaryVisible,
    bool PublishImmediately, DateTimeOffset? ExpiresAt) : IRequest<JobDto>;

public sealed class CreateJobCommandValidator : AbstractValidator<CreateJobCommand>
{
    public CreateJobCommandValidator()
    {
        RuleFor(x => x.EmployerId).NotEmpty();
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).NotEmpty().MaximumLength(10000);
        RuleFor(x => x.SalaryMax).GreaterThanOrEqualTo(x => x.SalaryMin)
            .When(x => x.SalaryMin.HasValue && x.SalaryMax.HasValue);
    }
}

public sealed class CreateJobCommandHandler : IRequestHandler<CreateJobCommand, JobDto>
{
    private readonly IJobRepository _repo;
    private readonly IUnitOfWork _uow;
    private readonly ILogger<CreateJobCommandHandler> _logger;

    public CreateJobCommandHandler(IJobRepository repo, IUnitOfWork uow, ILogger<CreateJobCommandHandler> logger)
    { _repo = repo; _uow = uow; _logger = logger; }

    public async Task<JobDto> Handle(CreateJobCommand req, CancellationToken ct)
    {
        var job = Job.Create(req.EmployerId, req.Title, req.Description,
            req.JobType, req.WorkMode, req.ExperienceLevel,
            req.Location, req.Country, req.CompanyName,
            req.SalaryMin, req.SalaryMax, req.SalaryCurrency, req.IsSalaryVisible, req.ExpiresAt);

        if (req.PublishImmediately) job.Publish();
        await _repo.AddAsync(job, ct);
        await _uow.SaveChangesAsync(ct);
        _logger.LogInformation("Job created: {JobId}", job.Id);
        return JobMapper.Map(job);
    }
}
