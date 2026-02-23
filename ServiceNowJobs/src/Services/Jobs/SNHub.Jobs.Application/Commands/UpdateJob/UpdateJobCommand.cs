using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using SNHub.Jobs.Application.DTOs;
using SNHub.Jobs.Application.Interfaces;
using SNHub.Jobs.Domain.Enums;

namespace SNHub.Jobs.Application.Commands.UpdateJob;

public sealed record UpdateJobCommand(
    Guid JobId, Guid RequesterId,
    string Title, string Description, string? Requirements, string? Benefits,
    JobType JobType, WorkMode WorkMode, ExperienceLevel ExperienceLevel,
    string? Location, string? Country,
    decimal? SalaryMin, decimal? SalaryMax, string? SalaryCurrency, bool IsSalaryVisible,
    DateTimeOffset? ExpiresAt) : IRequest<JobDto>;

public sealed class UpdateJobCommandValidator : AbstractValidator<UpdateJobCommand>
{
    public UpdateJobCommandValidator()
    {
        RuleFor(x => x.JobId).NotEmpty();
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).NotEmpty().MaximumLength(10000);
    }
}

public sealed class UpdateJobCommandHandler : IRequestHandler<UpdateJobCommand, JobDto>
{
    private readonly IJobRepository _repo;
    private readonly IUnitOfWork _uow;
    private readonly ILogger<UpdateJobCommandHandler> _logger;

    public UpdateJobCommandHandler(IJobRepository repo, IUnitOfWork uow, ILogger<UpdateJobCommandHandler> logger)
    { _repo = repo; _uow = uow; _logger = logger; }

    public async Task<JobDto> Handle(UpdateJobCommand req, CancellationToken ct)
    {
        var job = await _repo.GetByIdAsync(req.JobId, ct)
            ?? throw new KeyNotFoundException($"Job {req.JobId} not found.");
        if (job.EmployerId != req.RequesterId) throw new UnauthorizedAccessException();

        job.Update(req.Title, req.Description, req.Requirements, req.Benefits,
            req.JobType, req.WorkMode, req.ExperienceLevel, req.Location, req.Country,
            req.SalaryMin, req.SalaryMax, req.SalaryCurrency, req.IsSalaryVisible, req.ExpiresAt);

        await _repo.UpdateAsync(job, ct);
        await _uow.SaveChangesAsync(ct);
        return JobMapper.Map(job);
    }
}
