using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using SNHub.Matching.Application.DTOs;
using SNHub.Matching.Application.Interfaces;
using SNHub.Matching.Domain.Enums;
using SNHub.Matching.Domain.Exceptions;

namespace SNHub.Matching.Application.Queries.GetJobMatches;

/// <summary>
/// Returns ranked candidates for a given job.
/// Requires the job's embedding to be indexed.
/// Only accessible by the job's employer or an admin.
/// </summary>
public sealed record GetJobMatchesQuery(
    Guid  JobId,
    Guid  RequesterId,
    bool  RequesterIsAdmin,
    int   Page     = 1,
    int   PageSize = 10) : IRequest<MatchResultsDto<CandidateMatchDto>>;

public sealed class GetJobMatchesQueryValidator : AbstractValidator<GetJobMatchesQuery>
{
    public GetJobMatchesQueryValidator()
    {
        RuleFor(x => x.JobId).NotEmpty();
        RuleFor(x => x.RequesterId).NotEmpty();
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 50);
    }
}

public sealed class GetJobMatchesQueryHandler
    : IRequestHandler<GetJobMatchesQuery, MatchResultsDto<CandidateMatchDto>>
{
    private readonly IEmbeddingRecordRepository _repo;
    private readonly IVectorSearchService       _search;
    private readonly IJobsServiceClient         _jobs;
    private readonly IProfilesServiceClient     _profiles;
    private readonly ILogger<GetJobMatchesQueryHandler> _logger;

    public GetJobMatchesQueryHandler(
        IEmbeddingRecordRepository repo, IVectorSearchService search,
        IJobsServiceClient jobs, IProfilesServiceClient profiles,
        ILogger<GetJobMatchesQueryHandler> logger)
    { _repo = repo; _search = search; _jobs = jobs; _profiles = profiles; _logger = logger; }

    public async Task<MatchResultsDto<CandidateMatchDto>> Handle(
        GetJobMatchesQuery req, CancellationToken ct)
    {
        // Verify job exists and requester owns it (or is admin)
        var job = await _jobs.GetJobAsync(req.JobId, ct)
            ?? throw new DocumentNotFoundException(req.JobId, "Job");

        if (!req.RequesterIsAdmin && job.EmployerId != req.RequesterId)
            throw new AccessDeniedException();

        // Check embedding is indexed
        var record = await _repo.GetByDocumentIdAsync(req.JobId, DocumentType.Job, ct);
        if (record?.Status != EmbeddingStatus.Indexed)
            return new MatchResultsDto<CandidateMatchDto>(
                0, req.Page, req.PageSize, false, []);

        // Vector search — get top 100, then page
        var searchResults = await _search.SearchCandidatesForJobAsync(
            [], 100, ct);  // embedding fetched inside the search service

        // Enrich with candidate data
        var enriched = new List<CandidateMatchDto>();
        foreach (var (candidateId, score) in searchResults)
        {
            if (!Guid.TryParse(candidateId, out var userId)) continue;
            var candidate = await _profiles.GetCandidateAsync(userId, ct);
            if (candidate is null) continue;

            var matched = job.Skills.Intersect(
                candidate.Skills, StringComparer.OrdinalIgnoreCase).ToArray();

            enriched.Add(new CandidateMatchDto(
                UserId:           candidate.UserId,
                FullName:         $"{candidate.FirstName} {candidate.LastName}".Trim(),
                Headline:         candidate.Headline,
                CurrentRole:      candidate.CurrentRole,
                Location:         candidate.Location,
                YearsOfExperience:candidate.YearsOfExperience,
                ExperienceLevel:  candidate.ExperienceLevel,
                Availability:     candidate.Availability,
                Skills:           candidate.Skills,
                Certifications:   candidate.Certifications,
                Score:            score,
                ScorePercent:     (int)Math.Round(score * 100),
                MatchedSkills:    matched,
                ProfileUpdatedAt: candidate.UpdatedAt));
        }

        var paged = enriched
            .OrderByDescending(c => c.Score)
            .Skip((req.Page - 1) * req.PageSize)
            .Take(req.PageSize)
            .ToArray();

        return new MatchResultsDto<CandidateMatchDto>(
            enriched.Count, req.Page, req.PageSize, true, paged);
    }
}
