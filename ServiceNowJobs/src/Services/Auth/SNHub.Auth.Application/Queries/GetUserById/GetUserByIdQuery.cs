using MediatR;
using SNHub.Auth.Application.DTOs;
using SNHub.Auth.Application.Interfaces;
using SNHub.Auth.Domain.Exceptions;

namespace SNHub.Auth.Application.Queries.GetUserById;

public sealed record GetUserByIdQuery(Guid UserId) : IRequest<UserAdminDto>;

public sealed class GetUserByIdQueryHandler : IRequestHandler<GetUserByIdQuery, UserAdminDto>
{
    private readonly IUserRepository _users;

    public GetUserByIdQueryHandler(IUserRepository users) => _users = users;

    public async Task<UserAdminDto> Handle(GetUserByIdQuery request, CancellationToken ct)
    {
        var user = await _users.GetByIdAsync(request.UserId, ct)
            ?? throw new UserNotFoundException($"User {request.UserId} not found.");

        return new UserAdminDto(
            user.Id,
            user.Email,
            user.FirstName,
            user.LastName,
            user.FullName,
            user.PhoneNumber,
            user.ProfilePictureUrl,
            user.IsEmailVerified,
            user.IsActive,
            user.IsSuspended,
            user.SuspensionReason,
            user.SuspendedAt,
            user.Roles.Select(r => r.ToString()),
            user.LastLoginAt,
            user.LastLoginIp,
            user.FailedLoginAttempts,
            user.LockedOutUntil,
            user.Country,
            user.TimeZone,
            user.CreatedAt);
    }
}
