using FluentValidation;
using MediatR;
using SNHub.JobEnhancer.Application.DTOs;
using SNHub.JobEnhancer.Application.Interfaces;
using SNHub.JobEnhancer.Application.Mappers;
using SNHub.JobEnhancer.Domain.Exceptions;

namespace SNHub.JobEnhancer.Application.Queries.GetEnhancement;

public sealed record GetEnhancementQuery(
    Guid EnhancementId, Guid RequesterId) : IRequest<EnhancementResultDto>;

public sealed class GetEnhancementQueryValidator : AbstractValidator<GetEnhancementQuery>
{
    public GetEnhancementQueryValidator()
    {
        RuleFor(x => x.EnhancementId).NotEmpty();
        RuleFor(x => x.RequesterId).NotEmpty();
    }
}

public sealed class GetEnhancementQueryHandler
    : IRequestHandler<GetEnhancementQuery, EnhancementResultDto>
{
    private readonly IEnhancementResultRepository _repo;
    public GetEnhancementQueryHandler(IEnhancementResultRepository repo) => _repo = repo;

    public async Task<EnhancementResultDto> Handle(
        GetEnhancementQuery req, CancellationToken ct)
    {
        var record = await _repo.GetByIdAsync(req.EnhancementId, ct)
            ?? throw new EnhancementNotFoundException(req.EnhancementId);

        if (record.RequestedBy != req.RequesterId)
            throw new EnhancementAccessDeniedException();

        return EnhancementResultMapper.ToDto(record);
    }
}

// ── List by job ───────────────────────────────────────────────────────────────

public sealed record GetJobEnhancementsQuery(
    Guid JobId, Guid RequesterId) : IRequest<IEnumerable<EnhancementResultDto>>;

public sealed class GetJobEnhancementsQueryHandler
    : IRequestHandler<GetJobEnhancementsQuery, IEnumerable<EnhancementResultDto>>
{
    private readonly IEnhancementResultRepository _repo;
    public GetJobEnhancementsQueryHandler(IEnhancementResultRepository repo) => _repo = repo;

    public async Task<IEnumerable<EnhancementResultDto>> Handle(
        GetJobEnhancementsQuery req, CancellationToken ct)
    {
        var results = await _repo.GetByJobIdAsync(req.JobId, ct);
        return results
            .Where(r => r.RequestedBy == req.RequesterId)
            .OrderByDescending(r => r.CreatedAt)
            .Select(EnhancementResultMapper.ToDto);
    }
}

// ── List all by requester ─────────────────────────────────────────────────────

public sealed record GetMyEnhancementsQuery(Guid UserId)
    : IRequest<IEnumerable<EnhancementResultDto>>;

public sealed class GetMyEnhancementsQueryHandler
    : IRequestHandler<GetMyEnhancementsQuery, IEnumerable<EnhancementResultDto>>
{
    private readonly IEnhancementResultRepository _repo;
    public GetMyEnhancementsQueryHandler(IEnhancementResultRepository repo) => _repo = repo;

    public async Task<IEnumerable<EnhancementResultDto>> Handle(
        GetMyEnhancementsQuery req, CancellationToken ct)
    {
        var results = await _repo.GetByRequesterAsync(req.UserId, ct);
        return results.OrderByDescending(r => r.CreatedAt)
                      .Select(EnhancementResultMapper.ToDto);
    }
}
