using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using SNHub.Jobs.Application.DTOs;
using SNHub.Jobs.Application.Interfaces;
using SNHub.Jobs.Domain.Enums;
using SNHub.Jobs.Domain.Exceptions;

namespace SNHub.Jobs.Application.Commands.PublishJob;

public sealed record PublishJobCommand(Guid JobId, Guid RequesterId) : IRequest<JobDto>;

public sealed class PublishJobCommandValidator : AbstractValidator<PublishJobCommand>
{
    public PublishJobCommandValidator()
    {
        RuleFor(x => x.JobId).NotEmpty();
        RuleFor(x => x.RequesterId).NotEmpty();
    }
}

public sealed class PublishJobCommandHandler : IRequestHandler<PublishJobCommand, JobDto>
{
    private readonly IJobRepository _repo;
    private readonly IUnitOfWork _uow;
    private readonly ILogger<PublishJobCommandHandler> _logger;

    public PublishJobCommandHandler(IJobRepository repo, IUnitOfWork uow, ILogger<PublishJobCommandHandler> logger)
    {
        _repo = repo;
        _uow = uow;
        _logger = logger;
    }

    public async Task<JobDto> Handle(PublishJobCommand req, CancellationToken ct)
    {
        var job = await _repo.GetByIdAsync(req.JobId, ct)
            ?? throw new JobNotFoundException(req.JobId);

        if (job.EmployerId != req.RequesterId)
            throw new JobAccessDeniedException();

        if (job.Status == JobStatus.Closed)
            throw new JobNotActiveException("Cannot publish a closed job.");

        if (job.Status == JobStatus.Active)
            throw new DomainException("Job is already published.");

        job.Publish();
        await _repo.UpdateAsync(job, ct);
        await _uow.SaveChangesAsync(ct);

        _logger.LogInformation("Job {JobId} published by {RequesterId}", req.JobId, req.RequesterId);
        return JobMapper.ToDto(job);
    }
}
