using MediatR;
using SNHub.Users.Application.DTOs;
using SNHub.Users.Application.Interfaces;

namespace SNHub.Users.Application.Queries.GetProfile;

public sealed record GetProfileQuery(Guid UserId) : IRequest<UserProfileDto?>;

public sealed class GetProfileQueryHandler : IRequestHandler<GetProfileQuery, UserProfileDto?>
{
    private readonly IUserProfileRepository _repo;
    public GetProfileQueryHandler(IUserProfileRepository repo) { _repo = repo; }

    public async Task<UserProfileDto?> Handle(GetProfileQuery req, CancellationToken ct)
    {
        var p = await _repo.GetByUserIdAsync(req.UserId, ct);
        if (p is null) return null;
        return new UserProfileDto(p.Id, p.UserId, p.Headline, p.Bio, p.Location,
            p.ProfilePictureUrl, p.CvUrl, p.LinkedInUrl, p.GitHubUrl, p.WebsiteUrl,
            p.IsPublic, p.YearsOfExperience, p.Country, p.TimeZone, p.CreatedAt, p.UpdatedAt);
    }
}
