using SNHub.Auth.Domain.Enums;
using SNHub.Auth.Domain.Events;
using SNHub.Auth.Domain.Exceptions;
using System.Text.Json;

namespace SNHub.Auth.Domain.Entities;

/// <summary>
/// User aggregate root. State changes go through methods only.
/// RolesJson is the EF-mapped column (jsonb). The computed Roles property is
/// the clean domain API — not mapped by EF (Ignored in EntityConfigurations).
/// </summary>
public sealed class User
{
    private List<RefreshToken> _refreshTokens = [];
    private readonly List<DomainEvent> _domainEvents = [];

    private User() { }

    // ── Persisted properties ──────────────────────────────────────────────────
    public Guid Id { get; private set; }
    public string Email { get; private set; } = string.Empty;
    public string NormalizedEmail { get; private set; } = string.Empty;
    public string PasswordHash { get; private set; } = string.Empty;
    public string FirstName { get; private set; } = string.Empty;
    public string LastName { get; private set; } = string.Empty;
    public string? PhoneNumber { get; private set; }
    public string? ProfilePictureUrl { get; private set; }
    public bool IsEmailVerified { get; private set; }
    public bool IsActive { get; private set; }
    public bool IsSuspended { get; private set; }
    public string? SuspensionReason { get; private set; }
    public DateTimeOffset? SuspendedAt { get; private set; }
    public DateTimeOffset? EmailVerifiedAt { get; private set; }
    public DateTimeOffset? LastLoginAt { get; private set; }
    public string? LastLoginIp { get; private set; }
    public int FailedLoginAttempts { get; private set; }
    public DateTimeOffset? LockedOutUntil { get; private set; }
    public string? EmailVerificationToken { get; private set; }
    public DateTimeOffset? EmailVerificationTokenExpiry { get; private set; }
    public string? PasswordResetToken { get; private set; }
    public DateTimeOffset? PasswordResetTokenExpiry { get; private set; }
    public string? LinkedInId { get; private set; }
    public string? GoogleId { get; private set; }
    public string? AzureAdObjectId { get; private set; }
    public string? TimeZone { get; private set; }
    public string? Country { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public string CreatedBy { get; private set; } = "system";
    public string UpdatedBy { get; private set; } = "system";

    // EF-mapped column — public readable so EntityConfigurations can reference it
    // via expression; only settable through domain methods.
    public string RolesJson { get; private set; } = "[]";

    // ── Computed / navigation (not mapped by EF) ──────────────────────────────
    public IReadOnlyCollection<RefreshToken> RefreshTokens => _refreshTokens.AsReadOnly();
    public IReadOnlyCollection<DomainEvent> DomainEvents => _domainEvents.AsReadOnly();
    public string FullName => $"{FirstName} {LastName}".Trim();
    public bool IsLockedOut => LockedOutUntil.HasValue && LockedOutUntil > DateTimeOffset.UtcNow;

    public IReadOnlyCollection<UserRole> Roles =>
        JsonSerializer.Deserialize<List<int>>(RolesJson)!
            .Select(i => (UserRole)i).ToList().AsReadOnly();

    // ── Private helpers ───────────────────────────────────────────────────────
    private List<UserRole> GetRolesList() =>
        JsonSerializer.Deserialize<List<int>>(RolesJson)!.Select(i => (UserRole)i).ToList();

    private void SetRolesList(IEnumerable<UserRole> roles) =>
        RolesJson = JsonSerializer.Serialize(roles.Select(r => (int)r).ToList());

    // ── Factory Methods ───────────────────────────────────────────────────────
    public static User Create(
        string email, string passwordHash,
        string firstName, string lastName,
        UserRole primaryRole, string? country = null, string? timeZone = null)
    {
        if (string.IsNullOrWhiteSpace(email)) throw new DomainException("Email is required.");
        if (string.IsNullOrWhiteSpace(passwordHash)) throw new DomainException("Password hash is required.");
        if (string.IsNullOrWhiteSpace(firstName) || string.IsNullOrWhiteSpace(lastName))
            throw new DomainException("First and last name are required.");

        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = email.ToLowerInvariant().Trim(),
            NormalizedEmail = email.ToUpperInvariant().Trim(),
            PasswordHash = passwordHash,
            FirstName = firstName.Trim(),
            LastName = lastName.Trim(),
            IsActive = true,
            IsEmailVerified = false,
            IsSuspended = false,
            Country = country,
            TimeZone = timeZone ?? "UTC",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        user.SetRolesList([primaryRole]);
        user.GenerateEmailVerificationToken();
        user._domainEvents.Add(new UserRegisteredEvent(user.Id, user.Email, primaryRole));
        return user;
    }

    public static User CreateViaOAuth(
        string email, string firstName, string lastName,
        string? linkedInId, string? googleId, string? azureAdObjectId, UserRole primaryRole)
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = email.ToLowerInvariant().Trim(),
            NormalizedEmail = email.ToUpperInvariant().Trim(),
            PasswordHash = string.Empty,
            FirstName = firstName.Trim(),
            LastName = lastName.Trim(),
            IsActive = true,
            IsEmailVerified = true,
            EmailVerifiedAt = DateTimeOffset.UtcNow,
            IsSuspended = false,
            LinkedInId = linkedInId,
            GoogleId = googleId,
            AzureAdObjectId = azureAdObjectId,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        user.SetRolesList([primaryRole]);
        user._domainEvents.Add(new UserRegisteredEvent(user.Id, user.Email, primaryRole));
        return user;
    }

    // ── Behaviour Methods ─────────────────────────────────────────────────────
    public void RecordSuccessfulLogin(string ipAddress)
    {
        FailedLoginAttempts = 0; LockedOutUntil = null;
        LastLoginAt = DateTimeOffset.UtcNow; LastLoginIp = ipAddress;
        UpdatedAt = DateTimeOffset.UtcNow;
        _domainEvents.Add(new UserLoggedInEvent(Id, Email, ipAddress));
    }

    public void RecordFailedLogin()
    {
        FailedLoginAttempts++; UpdatedAt = DateTimeOffset.UtcNow;
        if (FailedLoginAttempts >= 5)
            LockedOutUntil = DateTimeOffset.UtcNow.AddMinutes(Math.Min(FailedLoginAttempts * 5, 60));
    }

    public void VerifyEmail(string token)
    {
        if (IsEmailVerified) throw new DomainException("Email is already verified.");
        if (EmailVerificationToken != token) throw new InvalidTokenException("Invalid email verification token.");
        if (EmailVerificationTokenExpiry < DateTimeOffset.UtcNow) throw new InvalidTokenException("Token expired.");
        IsEmailVerified = true; EmailVerifiedAt = DateTimeOffset.UtcNow;
        EmailVerificationToken = null; EmailVerificationTokenExpiry = null;
        UpdatedAt = DateTimeOffset.UtcNow;
        _domainEvents.Add(new UserEmailVerifiedEvent(Id, Email));
    }

    public void GenerateEmailVerificationToken()
    {
        EmailVerificationToken = GenerateSecureToken();
        EmailVerificationTokenExpiry = DateTimeOffset.UtcNow.AddHours(24);
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void GeneratePasswordResetToken()
    {
        PasswordResetToken = GenerateSecureToken();
        PasswordResetTokenExpiry = DateTimeOffset.UtcNow.AddHours(1);
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void ResetPassword(string token, string newHash)
    {
        if (PasswordResetToken != token) throw new InvalidTokenException("Invalid password reset token.");
        if (PasswordResetTokenExpiry < DateTimeOffset.UtcNow) throw new InvalidTokenException("Token expired.");
        PasswordHash = newHash; PasswordResetToken = null; PasswordResetTokenExpiry = null;
        FailedLoginAttempts = 0; LockedOutUntil = null; UpdatedAt = DateTimeOffset.UtcNow;
        RevokeAllRefreshTokens("Password reset");
    }

    public void ChangePassword(string newHash)
    { PasswordHash = newHash; UpdatedAt = DateTimeOffset.UtcNow; RevokeAllRefreshTokens("Password changed"); }

    public void AddRole(UserRole role)
    {
        var roles = GetRolesList();
        if (!roles.Contains(role)) { roles.Add(role); SetRolesList(roles); UpdatedAt = DateTimeOffset.UtcNow; }
    }

    public void RemoveRole(UserRole role)
    {
        var roles = GetRolesList();
        if (roles.Count == 1) throw new DomainException("User must have at least one role.");
        roles.Remove(role); SetRolesList(roles); UpdatedAt = DateTimeOffset.UtcNow;
    }

    public RefreshToken AddRefreshToken(string token, string ip, string ua, DateTimeOffset expires)
    {
        foreach (var old in _refreshTokens.Where(t => t.CreatedByIp == ip && t.UserAgent == ua && t.IsActive).ToList())
            old.Revoke("Replaced by new token", ip);
        var rt = RefreshToken.Create(Id, token, ip, ua, expires);
        _refreshTokens.Add(rt); UpdatedAt = DateTimeOffset.UtcNow;
        return rt;
    }

    public void RevokeRefreshToken(string token, string ip, string? reason = null)
    {
        var rt = _refreshTokens.FirstOrDefault(t => t.Token == token)
            ?? throw new InvalidTokenException("Refresh token not found.");
        if (!rt.IsActive) throw new InvalidTokenException("Token already inactive.");
        rt.Revoke(reason ?? "Revoked by user", ip); UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void RevokeAllRefreshTokens(string reason)
    {
        foreach (var rt in _refreshTokens.Where(t => t.IsActive)) rt.Revoke(reason, string.Empty);
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Suspend(string reason, string by)
    {
        if (IsSuspended) throw new DomainException("User is already suspended.");
        IsSuspended = true; SuspensionReason = reason; SuspendedAt = DateTimeOffset.UtcNow;
        UpdatedAt = DateTimeOffset.UtcNow; UpdatedBy = by;
        RevokeAllRefreshTokens("Account suspended");
    }

    public void Reinstate(string by)
    {
        IsSuspended = false; SuspensionReason = null; SuspendedAt = null;
        UpdatedAt = DateTimeOffset.UtcNow; UpdatedBy = by;
    }

    public void UpdateProfile(string firstName, string lastName, string? phone, string? country, string? tz, string by)
    {
        FirstName = firstName.Trim(); LastName = lastName.Trim();
        PhoneNumber = phone?.Trim(); Country = country; TimeZone = tz;
        UpdatedAt = DateTimeOffset.UtcNow; UpdatedBy = by;
    }

    public void UpdateProfilePicture(string url) { ProfilePictureUrl = url; UpdatedAt = DateTimeOffset.UtcNow; }
    public void LinkAzureAdAccount(string id) { AzureAdObjectId = id; UpdatedAt = DateTimeOffset.UtcNow; }
    public void ClearDomainEvents() => _domainEvents.Clear();

    /// <summary>
    /// Generates a cryptographically secure random token for email verification
    /// and password reset flows. Uses RandomNumberGenerator (CSPRNG) — NOT Guid.NewGuid()
    /// which is not cryptographically random and must never be used for security tokens.
    /// 256 bits of entropy (32 bytes) → URL-safe Base64 → 44 chars.
    /// </summary>
    private static string GenerateSecureToken()
    {
        var bytes = new byte[32];
        System.Security.Cryptography.RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes)
            .Replace("+", "-").Replace("/", "_").TrimEnd('='); // URL-safe Base64
    }
}
