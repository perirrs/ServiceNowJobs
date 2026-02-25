using MediatR;
using SNHub.Users.Application.DTOs;
using SNHub.Users.Application.Interfaces;

namespace SNHub.Users.Application.Queries.GetProfile;

public sealed record GetProfileQuery(Guid UserId) : IRequest<UserProfileDto?>;

public sealed class GetProfileQueryHandler : IRequestHandler<GetProfileQuery, UserProfileDto?>
{
    private readonly IUserProfileRepository _repo;
    public GetProfileQueryHandler(IUserProfileRepository repo) => _repo = repo;

    public async Task<UserProfileDto?> Handle(GetProfileQuery req, CancellationToken ct)
    {
        var p = await _repo.GetByUserIdAsync(req.UserId, ct);
        return p is null ? null : UserProfileMapper.ToDto(p);
    }
}
