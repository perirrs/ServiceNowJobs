using FluentValidation;
using MediatR;
using SNHub.CvParser.Application.DTOs;
using SNHub.CvParser.Application.Interfaces;
using SNHub.CvParser.Application.Mappers;
using SNHub.CvParser.Domain.Exceptions;

namespace SNHub.CvParser.Application.Queries.GetParseResult;

public sealed record GetParseResultQuery(Guid ParseResultId, Guid RequesterId) : IRequest<CvParseResultDto>;

public sealed class GetParseResultQueryValidator : AbstractValidator<GetParseResultQuery>
{
    public GetParseResultQueryValidator()
    {
        RuleFor(x => x.ParseResultId).NotEmpty();
        RuleFor(x => x.RequesterId).NotEmpty();
    }
}

public sealed class GetParseResultQueryHandler : IRequestHandler<GetParseResultQuery, CvParseResultDto>
{
    private readonly ICvParseResultRepository _repo;
    public GetParseResultQueryHandler(ICvParseResultRepository repo) => _repo = repo;

    public async Task<CvParseResultDto> Handle(GetParseResultQuery req, CancellationToken ct)
    {
        var result = await _repo.GetByIdAsync(req.ParseResultId, ct)
            ?? throw new ParseResultNotFoundException(req.ParseResultId);

        if (result.UserId != req.RequesterId)
            throw new ParseResultAccessDeniedException();

        return CvParseResultMapper.ToDto(result);
    }
}

// ── List all parse results for current user ───────────────────────────────────

public sealed record GetMyParseResultsQuery(Guid UserId) : IRequest<IEnumerable<CvParseResultDto>>;

public sealed class GetMyParseResultsQueryHandler
    : IRequestHandler<GetMyParseResultsQuery, IEnumerable<CvParseResultDto>>
{
    private readonly ICvParseResultRepository _repo;
    public GetMyParseResultsQueryHandler(ICvParseResultRepository repo) => _repo = repo;

    public async Task<IEnumerable<CvParseResultDto>> Handle(
        GetMyParseResultsQuery req, CancellationToken ct)
    {
        var results = await _repo.GetByUserIdAsync(req.UserId, ct);
        return results.Select(CvParseResultMapper.ToDto);
    }
}
