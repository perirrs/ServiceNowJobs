using SNHub.Auth.Domain.Enums;

namespace SNHub.Auth.Application.DTOs;

public sealed record AuthResponseDto(
    string AccessToken,
    string RefreshToken,
    DateTimeOffset AccessTokenExpiry,
    DateTimeOffset RefreshTokenExpiry,
    UserProfileDto User);

public sealed record TokenDto(
    string AccessToken,
    string RefreshToken,
    DateTimeOffset AccessTokenExpiry,
    DateTimeOffset RefreshTokenExpiry);

public sealed record UserProfileDto(
    Guid Id,
    string Email,
    string FirstName,
    string LastName,
    string FullName,
    string? PhoneNumber,
    string? ProfilePictureUrl,
    bool IsEmailVerified,
    bool IsActive,
    IEnumerable<string> Roles,
    DateTimeOffset? LastLoginAt,
    string? Country,
    string? TimeZone,
    DateTimeOffset CreatedAt);

public sealed record PagedResultDto<T>(
    IEnumerable<T> Items,
    int TotalCount,
    int Page,
    int PageSize)
{
    public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);
    public bool HasNextPage => Page < TotalPages;
    public bool HasPreviousPage => Page > 1;
}

public sealed record UserSummaryDto(
    Guid Id,
    string Email,
    string FirstName,
    string LastName,
    string FullName,
    bool IsActive,
    bool IsEmailVerified,
    bool IsSuspended,
    IEnumerable<string> Roles,
    DateTimeOffset CreatedAt);

public sealed record UserAdminDto(
    Guid Id,
    string Email,
    string FirstName,
    string LastName,
    string FullName,
    string? PhoneNumber,
    string? ProfilePictureUrl,
    bool IsEmailVerified,
    bool IsActive,
    bool IsSuspended,
    string? SuspensionReason,
    DateTimeOffset? SuspendedAt,
    IEnumerable<string> Roles,
    DateTimeOffset? LastLoginAt,
    string? LastLoginIp,
    int FailedLoginAttempts,
    DateTimeOffset? LockedOutUntil,
    string? Country,
    string? TimeZone,
    DateTimeOffset CreatedAt);

public sealed record UpdateProfileResponse(
    string FirstName,
    string LastName,
    string? PhoneNumber,
    string? Country,
    string? TimeZone);
