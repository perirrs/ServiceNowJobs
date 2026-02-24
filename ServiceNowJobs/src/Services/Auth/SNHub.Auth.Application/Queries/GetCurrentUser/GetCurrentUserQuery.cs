using MediatR;
using SNHub.Auth.Application.DTOs;
using SNHub.Auth.Application.Interfaces;
using SNHub.Auth.Domain.Exceptions;

namespace SNHub.Auth.Application.Queries.GetCurrentUser;

public sealed record GetCurrentUserQuery : IRequest<UserProfileDto>;

public sealed class GetCurrentUserQueryHandler : IRequestHandler<GetCurrentUserQuery, UserProfileDto>
{
    private readonly IUserRepository _users;
    private readonly ICurrentUserService _currentUser;

    public GetCurrentUserQueryHandler(IUserRepository users, ICurrentUserService currentUser)
    {
        _users = users;
        _currentUser = currentUser;
    }

    public async Task<UserProfileDto> Handle(GetCurrentUserQuery request, CancellationToken ct)
    {
        var userId = _currentUser.UserId
            ?? throw new UnauthorizedAccessException("User not authenticated.");

        var user = await _users.GetByIdAsync(userId, ct)
            ?? throw new UserNotFoundException($"User {userId} not found.");

        return new UserProfileDto(
            user.Id,
            user.Email,
            user.FirstName,
            user.LastName,
            user.FullName,
            user.PhoneNumber,
            user.ProfilePictureUrl,
            user.IsEmailVerified,
            user.IsActive,
            user.Roles.Select(r => r.ToString()),
            user.LastLoginAt,
            user.Country,
            user.TimeZone,
            user.CreatedAt);
    }
}
