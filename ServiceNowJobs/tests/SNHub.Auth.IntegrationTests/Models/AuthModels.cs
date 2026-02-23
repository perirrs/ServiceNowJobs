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
