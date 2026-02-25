using MediatR;
using SNHub.Profiles.Application.DTOs;
using SNHub.Profiles.Application.Interfaces;
using SNHub.Profiles.Domain.Enums;
using SNHub.Profiles.Domain.Exceptions;

namespace SNHub.Profiles.Application.Queries.GetProfile;

// ── Get candidate profile ─────────────────────────────────────────────────────

public sealed record GetCandidateProfileQuery(Guid UserId) : IRequest<CandidateProfileDto>;

public sealed class GetCandidateProfileQueryHandler
    : IRequestHandler<GetCandidateProfileQuery, CandidateProfileDto>
{
    private readonly ICandidateProfileRepository _repo;
    public GetCandidateProfileQueryHandler(ICandidateProfileRepository repo) => _repo = repo;

    public async Task<CandidateProfileDto> Handle(GetCandidateProfileQuery req, CancellationToken ct)
    {
        var p = await _repo.GetByUserIdAsync(req.UserId, ct)
            ?? throw new ProfileNotFoundException(req.UserId);
        return ProfileMapper.ToDto(p);
    }
}

// ── Get employer profile ──────────────────────────────────────────────────────

public sealed record GetEmployerProfileQuery(Guid UserId) : IRequest<EmployerProfileDto>;

public sealed class GetEmployerProfileQueryHandler
    : IRequestHandler<GetEmployerProfileQuery, EmployerProfileDto>
{
    private readonly IEmployerProfileRepository _repo;
    public GetEmployerProfileQueryHandler(IEmployerProfileRepository repo) => _repo = repo;

    public async Task<EmployerProfileDto> Handle(GetEmployerProfileQuery req, CancellationToken ct)
    {
        var p = await _repo.GetByUserIdAsync(req.UserId, ct)
            ?? throw new ProfileNotFoundException(req.UserId);
        return ProfileMapper.ToDto(p);
    }
}

// ── Search candidates (public directory) ─────────────────────────────────────

public sealed record SearchCandidatesQuery(
    string? Keyword,
    string? Country,
    ExperienceLevel? Level,
    int? MinYears,
    bool? OpenToRemote,
    AvailabilityStatus? Availability,
    int Page = 1,
    int PageSize = 20) : IRequest<PagedResult<CandidateProfileDto>>;

public sealed class SearchCandidatesQueryHandler
    : IRequestHandler<SearchCandidatesQuery, PagedResult<CandidateProfileDto>>
{
    private readonly ICandidateProfileRepository _repo;
    public SearchCandidatesQueryHandler(ICandidateProfileRepository repo) => _repo = repo;

    public async Task<PagedResult<CandidateProfileDto>> Handle(SearchCandidatesQuery req, CancellationToken ct)
    {
        var (items, total) = await _repo.SearchAsync(
            req.Keyword, req.Country, req.Level, req.MinYears,
            req.OpenToRemote, req.Availability, req.Page, req.PageSize, ct);

        return PagedResult<CandidateProfileDto>.Create(
            items.Select(ProfileMapper.ToDto), total, req.Page, req.PageSize);
    }
}
