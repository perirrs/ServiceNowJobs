using Xunit;
using System.Text.Json.Serialization;

namespace SNHub.Auth.IntegrationTests.Models;

// ── API envelope models ───────────────────────────────────────────────────────

public sealed record ApiResponse<T>(
    bool Success,
    T? Data,
    string? Message,
    string? TraceId = null);

public sealed record ApiErrorResponse(
    string TraceId,
    int StatusCode,
    string ErrorCode,
    string Message,
    IEnumerable<string>? Errors = null,
    string? Detail = null);

// ── Auth-specific response models ─────────────────────────────────────────────

public sealed record AuthResponse(
    string AccessToken,
    string RefreshToken,
    DateTimeOffset AccessTokenExpiry,
    DateTimeOffset RefreshTokenExpiry,
    UserProfile User);

public sealed record TokenResponse(
    string AccessToken,
    string RefreshToken,
    DateTimeOffset AccessTokenExpiry,
    DateTimeOffset RefreshTokenExpiry);

public sealed record UserProfile(
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

// ── Request models (what we POST to the API) ──────────────────────────────────

public sealed record RegisterRequest(
    string Email,
    string Password,
    string ConfirmPassword,
    string FirstName,
    string LastName,
    int Role,               // UserRole enum value
    string? PhoneNumber = null,
    string? Country = null,
    string? TimeZone = null);

public sealed record LoginRequest(
    string Email,
    string Password,
    bool RememberMe = false);

public sealed record RefreshRequest(
    string AccessToken,
    string RefreshToken);

public sealed record RevokeRequest(
    string RefreshToken);

public sealed record ForgotPasswordRequest(
    string Email);

public sealed record ResetPasswordRequest(
    string Email,
    string Token,
    string NewPassword,
    string ConfirmNewPassword);

public sealed record VerifyEmailRequest(
    string Email,
    string Token);

// ── Step 4: User profile & admin models ──────────────────────────────────────

public sealed record UpdateProfileRequest(
    string FirstName,
    string LastName,
    string? PhoneNumber = null,
    string? Country = null,
    string? TimeZone = null);

public sealed record UserSummary(
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

public sealed record UserAdmin(
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

public sealed record PagedResult<T>(
    IEnumerable<T> Items,
    int TotalCount,
    int Page,
    int PageSize,
    int TotalPages,
    bool HasNextPage,
    bool HasPreviousPage);

public sealed record SuspendUserRequest(string Reason);

public sealed record UpdateUserRolesRequest(IReadOnlyList<int> Roles);

public sealed record ResendVerificationRequest(string Email);
