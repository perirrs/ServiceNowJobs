using MediatR;
using SNHub.Profiles.Application.Commands.UpsertCandidateProfile;
using SNHub.Profiles.Application.Commands.UpsertEmployerProfile;
using SNHub.Profiles.Application.DTOs;
using SNHub.Profiles.Application.Interfaces;
using SNHub.Profiles.Domain.Exceptions;
namespace SNHub.Profiles.Application.Queries.GetProfile;
public sealed record GetCandidateProfileQuery(Guid UserId) : IRequest<CandidateProfileDto>;
public sealed class GetCandidateProfileQueryHandler : IRequestHandler<GetCandidateProfileQuery, CandidateProfileDto>
{
    private readonly ICandidateProfileRepository _repo;
    public GetCandidateProfileQueryHandler(ICandidateProfileRepository repo) => _repo = repo;
    public async Task<CandidateProfileDto> Handle(GetCandidateProfileQuery req, CancellationToken ct)
    {
        var p = await _repo.GetByUserIdAsync(req.UserId, ct) ?? throw new ProfileNotFoundException(req.UserId);
        return UpsertCandidateProfileCommandHandler.ToDto(p);
    }
}
public sealed record GetEmployerProfileQuery(Guid UserId) : IRequest<EmployerProfileDto>;
public sealed class GetEmployerProfileQueryHandler : IRequestHandler<GetEmployerProfileQuery, EmployerProfileDto>
{
    private readonly IEmployerProfileRepository _repo;
    public GetEmployerProfileQueryHandler(IEmployerProfileRepository repo) => _repo = repo;
    public async Task<EmployerProfileDto> Handle(GetEmployerProfileQuery req, CancellationToken ct)
    {
        var p = await _repo.GetByUserIdAsync(req.UserId, ct) ?? throw new ProfileNotFoundException(req.UserId);
        return UpsertEmployerProfileCommandHandler.ToDto(p);
    }
}
