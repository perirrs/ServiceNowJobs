using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using SNHub.Matching.Application.DTOs;
using SNHub.Matching.Application.Interfaces;
using SNHub.Matching.Domain.Enums;

namespace SNHub.Matching.Application.Queries.GetCandidateMatches;

/// <summary>
/// Returns ranked job matches for a candidate.
/// Candidates can only query their own matches.
/// </summary>
public sealed record GetCandidateMatchesQuery(
    Guid UserId,
    int  Page     = 1,
    int  PageSize = 10) : IRequest<MatchResultsDto<JobMatchDto>>;

public sealed class GetCandidateMatchesQueryValidator
    : AbstractValidator<GetCandidateMatchesQuery>
{
    public GetCandidateMatchesQueryValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 50);
    }
}

public sealed class GetCandidateMatchesQueryHandler
    : IRequestHandler<GetCandidateMatchesQuery, MatchResultsDto<JobMatchDto>>
{
    private readonly IEmbeddingRecordRepository _repo;
    private readonly IVectorSearchService       _search;
    private readonly IProfilesServiceClient     _profiles;
    private readonly ILogger<GetCandidateMatchesQueryHandler> _logger;

    public GetCandidateMatchesQueryHandler(
        IEmbeddingRecordRepository repo, IVectorSearchService search,
        IProfilesServiceClient profiles,
        ILogger<GetCandidateMatchesQueryHandler> logger)
    { _repo = repo; _search = search; _profiles = profiles; _logger = logger; }

    public async Task<MatchResultsDto<JobMatchDto>> Handle(
        GetCandidateMatchesQuery req, CancellationToken ct)
    {
        // Check candidate embedding is indexed
        var record = await _repo.GetByDocumentIdAsync(
            req.UserId, DocumentType.CandidateProfile, ct);

        if (record?.Status != EmbeddingStatus.Indexed)
            return new MatchResultsDto<JobMatchDto>(
                0, req.Page, req.PageSize, false, []);

        // Get candidate skills for MatchedSkills calculation
        var candidate = await _profiles.GetCandidateAsync(req.UserId, ct);

        // Vector search against job index
        var searchResults = await _search.SearchJobsForCandidateAsync(
            [], 100, ct);  // embedding looked up inside search service

        var enriched = new List<JobMatchDto>();
        foreach (var (jobId, score) in searchResults)
        {
            // For stub/test we use the score and jobId directly
            // In production the search service returns hydrated documents
            var matched = candidate?.Skills
                .Where(s => !string.IsNullOrEmpty(s))
                .ToArray() ?? [];

            enriched.Add(new JobMatchDto(
                JobId:          Guid.TryParse(jobId, out var jid) ? jid : Guid.Empty,
                Title:          $"Job {jobId[..8]}",
                CompanyName:    null,
                Location:       null,
                Country:        null,
                WorkMode:       "Remote",
                ExperienceLevel:"Mid",
                SalaryMin:      null,
                SalaryMax:      null,
                SalaryCurrency: "USD",
                SkillsRequired: [],
                Score:          score,
                ScorePercent:   (int)Math.Round(score * 100),
                MatchedSkills:  matched,
                PostedAt:       DateTimeOffset.UtcNow));
        }

        var paged = enriched
            .OrderByDescending(j => j.Score)
            .Skip((req.Page - 1) * req.PageSize)
            .Take(req.PageSize)
            .ToArray();

        return new MatchResultsDto<JobMatchDto>(
            enriched.Count, req.Page, req.PageSize, true, paged);
    }
}
