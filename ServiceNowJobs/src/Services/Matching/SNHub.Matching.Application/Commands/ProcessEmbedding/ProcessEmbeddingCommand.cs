using System.Text;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using SNHub.Matching.Application.DTOs;
using SNHub.Matching.Application.Interfaces;
using SNHub.Matching.Domain.Enums;
using SNHub.Matching.Domain.Exceptions;

namespace SNHub.Matching.Application.Commands.ProcessEmbedding;

/// <summary>
/// Called by the background worker for each pending EmbeddingRecord.
/// Fetches the document data, generates an embedding via Azure OpenAI,
/// and upserts it into Azure AI Search.
/// </summary>
public sealed record ProcessEmbeddingCommand(
    Guid         DocumentId,
    DocumentType DocumentType) : IRequest<bool>;

public sealed class ProcessEmbeddingCommandValidator
    : AbstractValidator<ProcessEmbeddingCommand>
{
    public ProcessEmbeddingCommandValidator()
    {
        RuleFor(x => x.DocumentId).NotEmpty();
        RuleFor(x => x.DocumentType).IsInEnum();
    }
}

public sealed class ProcessEmbeddingCommandHandler
    : IRequestHandler<ProcessEmbeddingCommand, bool>
{
    private readonly IEmbeddingRecordRepository _repo;
    private readonly IUnitOfWork                _uow;
    private readonly IEmbeddingService          _embeddings;
    private readonly IVectorSearchService       _search;
    private readonly IJobsServiceClient         _jobs;
    private readonly IProfilesServiceClient     _profiles;
    private readonly ILogger<ProcessEmbeddingCommandHandler> _logger;

    public ProcessEmbeddingCommandHandler(
        IEmbeddingRecordRepository repo, IUnitOfWork uow,
        IEmbeddingService embeddings, IVectorSearchService search,
        IJobsServiceClient jobs, IProfilesServiceClient profiles,
        ILogger<ProcessEmbeddingCommandHandler> logger)
    {
        _repo = repo; _uow = uow; _embeddings = embeddings;
        _search = search; _jobs = jobs; _profiles = profiles; _logger = logger;
    }

    public async Task<bool> Handle(ProcessEmbeddingCommand req, CancellationToken ct)
    {
        var record = await _repo.GetByDocumentIdAsync(req.DocumentId, req.DocumentType, ct);
        if (record is null)
        {
            _logger.LogWarning("EmbeddingRecord not found for {Type} {Id}",
                req.DocumentType, req.DocumentId);
            return false;
        }

        record.SetProcessing();
        await _uow.SaveChangesAsync(ct);

        try
        {
            if (req.DocumentType == DocumentType.Job)
                await ProcessJobAsync(req.DocumentId, ct);
            else
                await ProcessCandidateAsync(req.DocumentId, ct);

            record.SetIndexed();
            _logger.LogInformation("Indexed {Type} {Id}", req.DocumentType, req.DocumentId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to index {Type} {Id}", req.DocumentType, req.DocumentId);
            record.SetFailed(ex.Message);
        }

        await _uow.SaveChangesAsync(ct);
        return record.Status == EmbeddingStatus.Indexed;
    }

    // ── Job processing ────────────────────────────────────────────────────────

    private async Task ProcessJobAsync(Guid jobId, CancellationToken ct)
    {
        var job = await _jobs.GetJobAsync(jobId, ct)
            ?? throw new DocumentNotFoundException(jobId, "Job");

        if (!job.IsActive)
        {
            await _search.DeleteAsync(jobId.ToString(), DocumentType.Job, ct);
            return;
        }

        var text      = BuildJobText(job);
        var embedding = await _embeddings.GenerateAsync(text, ct);

        await _search.UpsertJobAsync(new JobSearchDocument
        {
            Id              = job.Id.ToString(),
            Title           = job.Title,
            Description     = job.Description,
            Requirements    = job.Requirements,
            CompanyName     = job.CompanyName,
            Location        = job.Location,
            Country         = job.Country,
            WorkMode        = job.WorkMode,
            ExperienceLevel = job.ExperienceLevel,
            JobType         = job.JobType,
            SalaryMin       = job.SalaryMin,
            SalaryMax       = job.SalaryMax,
            SalaryCurrency  = job.SalaryCurrency,
            IsSalaryVisible = job.IsSalaryVisible,
            Skills          = job.Skills,
            ServiceNowVersions = job.ServiceNowVersions,
            Embedding       = embedding,
            CreatedAt       = job.CreatedAt
        }, ct);
    }

    // ── Candidate processing ──────────────────────────────────────────────────

    private async Task ProcessCandidateAsync(Guid userId, CancellationToken ct)
    {
        var candidate = await _profiles.GetCandidateAsync(userId, ct)
            ?? throw new DocumentNotFoundException(userId, "CandidateProfile");

        var text      = BuildCandidateText(candidate);
        var embedding = await _embeddings.GenerateAsync(text, ct);

        await _search.UpsertCandidateAsync(new CandidateSearchDocument
        {
            Id               = candidate.UserId.ToString(),
            FullName         = $"{candidate.FirstName} {candidate.LastName}".Trim(),
            Headline         = candidate.Headline,
            Summary          = candidate.Bio,
            CurrentRole      = candidate.CurrentRole,
            Location         = candidate.Location,
            Country          = candidate.Country,
            YearsOfExperience= candidate.YearsOfExperience,
            ExperienceLevel  = candidate.ExperienceLevel,
            Availability     = candidate.Availability,
            OpenToRemote     = candidate.OpenToRemote,
            Skills           = candidate.Skills,
            Certifications   = candidate.Certifications,
            ServiceNowVersions = candidate.ServiceNowVersions,
            Embedding        = embedding,
            UpdatedAt        = candidate.UpdatedAt
        }, ct);
    }

    // ── Text builders ─────────────────────────────────────────────────────────

    private static string BuildJobText(JobData job)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Job Title: {job.Title}");
        if (!string.IsNullOrWhiteSpace(job.CompanyName))
            sb.AppendLine($"Company: {job.CompanyName}");
        sb.AppendLine($"Type: {job.JobType}, Mode: {job.WorkMode}, Level: {job.ExperienceLevel}");
        if (!string.IsNullOrWhiteSpace(job.Location))
            sb.AppendLine($"Location: {job.Location}, {job.Country}");
        sb.AppendLine($"Description: {job.Description}");
        if (!string.IsNullOrWhiteSpace(job.Requirements))
            sb.AppendLine($"Requirements: {job.Requirements}");
        if (job.Skills.Length > 0)
            sb.AppendLine($"Required Skills: {string.Join(", ", job.Skills)}");
        if (job.ServiceNowVersions.Length > 0)
            sb.AppendLine($"ServiceNow Versions: {string.Join(", ", job.ServiceNowVersions)}");
        return sb.ToString();
    }

    private static string BuildCandidateText(CandidateData c)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(c.Headline))
            sb.AppendLine($"Headline: {c.Headline}");
        if (!string.IsNullOrWhiteSpace(c.CurrentRole))
            sb.AppendLine($"Current Role: {c.CurrentRole}");
        sb.AppendLine($"Experience: {c.YearsOfExperience} years, Level: {c.ExperienceLevel}");
        sb.AppendLine($"Availability: {c.Availability}");
        if (!string.IsNullOrWhiteSpace(c.Location))
            sb.AppendLine($"Location: {c.Location}, {c.Country}");
        if (!string.IsNullOrWhiteSpace(c.Bio))
            sb.AppendLine($"Summary: {c.Bio}");
        if (c.Skills.Length > 0)
            sb.AppendLine($"Skills: {string.Join(", ", c.Skills)}");
        if (c.Certifications.Length > 0)
            sb.AppendLine($"Certifications: {string.Join(", ", c.Certifications)}");
        if (c.ServiceNowVersions.Length > 0)
            sb.AppendLine($"ServiceNow Versions: {string.Join(", ", c.ServiceNowVersions)}");
        return sb.ToString();
    }
}
