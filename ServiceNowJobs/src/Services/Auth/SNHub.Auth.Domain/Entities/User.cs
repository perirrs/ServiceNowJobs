using SNHub.Auth.Domain.Enums;
using SNHub.Auth.Domain.Events;
using SNHub.Auth.Domain.Exceptions;

namespace SNHub.Auth.Domain.Entities;

/// <summary>
/// User aggregate root. All state changes must go through this entity's methods.
/// No external dependencies — pure domain logic only.
/// </summary>
public sealed class User
{
    private readonly List<RefreshToken> _refreshTokens = [];
    private readonly List<UserRole> _roles = [];
    private readonly List<DomainEvent> _domainEvents = [];

    private User() { }

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

    public IReadOnlyCollection<RefreshToken> RefreshTokens => _refreshTokens.AsReadOnly();
    public IReadOnlyCollection<UserRole> Roles => _roles.AsReadOnly();
    public IReadOnlyCollection<DomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    public string FullName => $"{FirstName} {LastName}".Trim();
    public bool IsLockedOut => LockedOutUntil.HasValue && LockedOutUntil > DateTimeOffset.UtcNow;

    // ─── Factory Methods ─────────────────────────────────────────────────────

    public static User Create(
        string email,
        string passwordHash,
        string firstName,
        string lastName,
        UserRole primaryRole,
        string? country = null,
        string? timeZone = null)
    {
        if (string.IsNullOrWhiteSpace(email))
            throw new DomainException("Email is required.");

        if (string.IsNullOrWhiteSpace(passwordHash))
            throw new DomainException("Password hash is required.");

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
            FailedLoginAttempts = 0,
            Country = country,
            TimeZone = timeZone ?? "UTC",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        user._roles.Add(primaryRole);
        user.GenerateEmailVerificationToken();
        user._domainEvents.Add(new UserRegisteredEvent(user.Id, user.Email, primaryRole));

        return user;
    }

    public static User CreateViaOAuth(
        string email,
        string firstName,
        string lastName,
        string? linkedInId,
        string? googleId,
        string? azureAdObjectId,
        UserRole primaryRole)
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
            FailedLoginAttempts = 0,
            LinkedInId = linkedInId,
            GoogleId = googleId,
            AzureAdObjectId = azureAdObjectId,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        user._roles.Add(primaryRole);
        user._domainEvents.Add(new UserRegisteredEvent(user.Id, user.Email, primaryRole));

        return user;
    }

    // ─── Behaviour Methods ────────────────────────────────────────────────────

    public void RecordSuccessfulLogin(string ipAddress)
    {
        FailedLoginAttempts = 0;
        LockedOutUntil = null;
        LastLoginAt = DateTimeOffset.UtcNow;
        LastLoginIp = ipAddress;
        UpdatedAt = DateTimeOffset.UtcNow;
        _domainEvents.Add(new UserLoggedInEvent(Id, Email, ipAddress));
    }

    public void RecordFailedLogin()
    {
        FailedLoginAttempts++;
        UpdatedAt = DateTimeOffset.UtcNow;

        if (FailedLoginAttempts >= 5)
        {
            var lockoutMinutes = Math.Min(FailedLoginAttempts * 5, 60);
            LockedOutUntil = DateTimeOffset.UtcNow.AddMinutes(lockoutMinutes);
        }
    }

    public void VerifyEmail(string token)
    {
        if (IsEmailVerified)
            throw new DomainException("Email is already verified.");

        if (string.IsNullOrWhiteSpace(EmailVerificationToken) || EmailVerificationToken != token)
            throw new InvalidTokenException("Invalid email verification token.");

        if (EmailVerificationTokenExpiry < DateTimeOffset.UtcNow)
            throw new InvalidTokenException("Email verification token has expired.");

        IsEmailVerified = true;
        EmailVerifiedAt = DateTimeOffset.UtcNow;
        EmailVerificationToken = null;
        EmailVerificationTokenExpiry = null;
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

    public void ResetPassword(string token, string newPasswordHash)
    {
        if (string.IsNullOrWhiteSpace(PasswordResetToken) || PasswordResetToken != token)
            throw new InvalidTokenException("Invalid or expired password reset token.");

        if (PasswordResetTokenExpiry < DateTimeOffset.UtcNow)
            throw new InvalidTokenException("Password reset token has expired.");

        PasswordHash = newPasswordHash;
        PasswordResetToken = null;
        PasswordResetTokenExpiry = null;
        FailedLoginAttempts = 0;
        LockedOutUntil = null;
        UpdatedAt = DateTimeOffset.UtcNow;
        RevokeAllRefreshTokens("Password reset");
    }

    public void ChangePassword(string newPasswordHash)
    {
        PasswordHash = newPasswordHash;
        UpdatedAt = DateTimeOffset.UtcNow;
        RevokeAllRefreshTokens("Password changed");
    }

    public void AddRole(UserRole role)
    {
        if (!_roles.Contains(role))
        {
            _roles.Add(role);
            UpdatedAt = DateTimeOffset.UtcNow;
        }
    }

    public void RemoveRole(UserRole role)
    {
        if (_roles.Count == 1)
            throw new DomainException("User must have at least one role.");

        _roles.Remove(role);
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public RefreshToken AddRefreshToken(
        string token,
        string ipAddress,
        string userAgent,
        DateTimeOffset expiresAt)
    {
        var existingFromDevice = _refreshTokens
            .Where(t => t.CreatedByIp == ipAddress && t.UserAgent == userAgent && t.IsActive)
            .ToList();

        foreach (var old in existingFromDevice)
            old.Revoke("Replaced by new token", ipAddress);

        var refreshToken = RefreshToken.Create(Id, token, ipAddress, userAgent, expiresAt);
        _refreshTokens.Add(refreshToken);
        UpdatedAt = DateTimeOffset.UtcNow;
        return refreshToken;
    }

    public void RevokeRefreshToken(string token, string ipAddress, string? reason = null)
    {
        var refreshToken = _refreshTokens.FirstOrDefault(t => t.Token == token)
            ?? throw new InvalidTokenException("Refresh token not found.");

        if (!refreshToken.IsActive)
            throw new InvalidTokenException("Refresh token is already inactive.");

        refreshToken.Revoke(reason ?? "Revoked by user", ipAddress);
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void RevokeAllRefreshTokens(string reason)
    {
        foreach (var token in _refreshTokens.Where(t => t.IsActive))
            token.Revoke(reason, string.Empty);

        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Suspend(string reason, string suspendedBy)
    {
        if (IsSuspended)
            throw new DomainException("User is already suspended.");

        IsSuspended = true;
        SuspensionReason = reason;
        SuspendedAt = DateTimeOffset.UtcNow;
        UpdatedAt = DateTimeOffset.UtcNow;
        UpdatedBy = suspendedBy;
        RevokeAllRefreshTokens("Account suspended");
    }

    public void Reinstate(string reinstatedBy)
    {
        IsSuspended = false;
        SuspensionReason = null;
        SuspendedAt = null;
        UpdatedAt = DateTimeOffset.UtcNow;
        UpdatedBy = reinstatedBy;
    }

    public void UpdateProfile(
        string firstName,
        string lastName,
        string? phoneNumber,
        string? country,
        string? timeZone,
        string updatedBy)
    {
        FirstName = firstName.Trim();
        LastName = lastName.Trim();
        PhoneNumber = phoneNumber?.Trim();
        Country = country;
        TimeZone = timeZone;
        UpdatedAt = DateTimeOffset.UtcNow;
        UpdatedBy = updatedBy;
    }

    public void UpdateProfilePicture(string url)
    {
        ProfilePictureUrl = url;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void LinkAzureAdAccount(string objectId)
    {
        AzureAdObjectId = objectId;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void ClearDomainEvents() => _domainEvents.Clear();

    private static string GenerateSecureToken()
        => Convert.ToBase64String(Guid.NewGuid().ToByteArray())
         + Convert.ToBase64String(Guid.NewGuid().ToByteArray());
}
