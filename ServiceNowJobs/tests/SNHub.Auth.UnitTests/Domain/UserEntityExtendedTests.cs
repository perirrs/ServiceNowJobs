using FluentAssertions;
using SNHub.Auth.Domain.Entities;
using SNHub.Auth.Domain.Enums;
using SNHub.Auth.Domain.Events;
using SNHub.Auth.Domain.Exceptions;
using Xunit;

namespace SNHub.Auth.UnitTests.Domain;

/// <summary>
/// Extended coverage for User entity behaviour methods not covered in UserEntityTests.
/// </summary>
public sealed class UserEntityExtendedTests
{
    private static User ValidUser() =>
        User.Create("test@example.com", "hash", "John", "Doe", UserRole.Candidate);

    // ── FullName ──────────────────────────────────────────────────────────────

    [Fact]
    public void FullName_ReturnsFirstAndLastJoined()
    {
        var user = User.Create("u@x.com", "h", "Jane", "Smith", UserRole.Candidate);
        user.FullName.Should().Be("Jane Smith");
    }

    // ── UpdateProfile ─────────────────────────────────────────────────────────

    [Fact]
    public void UpdateProfile_ValidInputs_UpdatesAllFields()
    {
        var user = ValidUser();

        user.UpdateProfile("Jane", "Smith", "+447911123456", "GB", "Europe/London", "admin");

        user.FirstName.Should().Be("Jane");
        user.LastName.Should().Be("Smith");
        user.PhoneNumber.Should().Be("+447911123456");
        user.Country.Should().Be("GB");
        user.TimeZone.Should().Be("Europe/London");
        user.UpdatedBy.Should().Be("admin");
    }

    [Fact]
    public void UpdateProfile_NullOptionalFields_SetsNulls()
    {
        var user = ValidUser();
        user.UpdateProfile("Jane", "Smith", null, null, null, "admin");

        user.PhoneNumber.Should().BeNull();
        user.Country.Should().BeNull();
        user.TimeZone.Should().BeNull();
    }

    [Fact]
    public void UpdateProfile_TrimsWhitespace()
    {
        var user = ValidUser();
        user.UpdateProfile("  Jane  ", "  Smith  ", null, null, null, "admin");

        user.FirstName.Should().Be("Jane");
        user.LastName.Should().Be("Smith");
    }

    // ── UpdateProfilePicture ──────────────────────────────────────────────────

    [Fact]
    public void UpdateProfilePicture_SetsUrl()
    {
        var user = ValidUser();
        user.UpdateProfilePicture("https://cdn.snhub.io/profiles/123.jpg");
        user.ProfilePictureUrl.Should().Be("https://cdn.snhub.io/profiles/123.jpg");
    }

    // ── AddRole / RemoveRole ──────────────────────────────────────────────────

    [Fact]
    public void AddRole_NewRole_AddsItToCollection()
    {
        var user = ValidUser();
        user.AddRole(UserRole.Employer);
        user.Roles.Should().Contain(UserRole.Employer);
        user.Roles.Should().Contain(UserRole.Candidate);
    }

    [Fact]
    public void AddRole_DuplicateRole_DoesNotDuplicate()
    {
        var user = ValidUser();
        user.AddRole(UserRole.Candidate); // already has this
        user.Roles.Count(r => r == UserRole.Candidate).Should().Be(1);
    }

    [Fact]
    public void RemoveRole_ExistingRole_RemovesIt()
    {
        var user = ValidUser();
        user.AddRole(UserRole.Employer);

        user.RemoveRole(UserRole.Employer);

        user.Roles.Should().NotContain(UserRole.Employer);
        user.Roles.Should().Contain(UserRole.Candidate);
    }

    [Fact]
    public void RemoveRole_LastRole_ThrowsDomainException()
    {
        var user  = ValidUser();
        var act = () => user.RemoveRole(UserRole.Candidate);
        act.Should().Throw<DomainException>().WithMessage("*at least one role*");
    }

    // ── Reinstate ─────────────────────────────────────────────────────────────

    [Fact]
    public void Reinstate_SuspendedUser_ClearsSuspension()
    {
        var user = ValidUser();
        user.Suspend("Test reason", "admin");

        user.Reinstate("super_admin");

        user.IsSuspended.Should().BeFalse();
        user.SuspensionReason.Should().BeNull();
        user.SuspendedAt.Should().BeNull();
        user.UpdatedBy.Should().Be("super_admin");
    }

    [Fact]
    public void Suspend_AlreadySuspended_ThrowsDomainException()
    {
        var user = ValidUser();
        user.Suspend("First reason", "admin");

        var act = () => user.Suspend("Second reason", "admin");
        act.Should().Throw<DomainException>().WithMessage("*already suspended*");
    }

    // ── ChangePassword ────────────────────────────────────────────────────────

    [Fact]
    public void ChangePassword_UpdatesHashAndRevokesTokens()
    {
        var user = ValidUser();
        user.AddRefreshToken("rt1", "1.2.3.4", "UA", DateTimeOffset.UtcNow.AddDays(30));

        user.ChangePassword("new_hash");

        user.PasswordHash.Should().Be("new_hash");
        user.RefreshTokens.All(t => t.IsRevoked).Should().BeTrue();
    }

    // ── RevokeAllRefreshTokens ────────────────────────────────────────────────

    [Fact]
    public void RevokeAllRefreshTokens_RevokesAllActiveTokens()
    {
        var user = ValidUser();
        user.AddRefreshToken("rt1", "1.2.3.4", "UA1", DateTimeOffset.UtcNow.AddDays(30));
        user.AddRefreshToken("rt2", "5.6.7.8", "UA2", DateTimeOffset.UtcNow.AddDays(30));

        user.RevokeAllRefreshTokens("Test reason");

        user.RefreshTokens.All(t => t.IsRevoked).Should().BeTrue();
    }

    // ── RevokeRefreshToken ────────────────────────────────────────────────────

    [Fact]
    public void RevokeRefreshToken_InactiveToken_ThrowsInvalidTokenException()
    {
        var user = ValidUser();
        user.AddRefreshToken("rt1", "1.2.3.4", "UA", DateTimeOffset.UtcNow.AddDays(30));
        user.RevokeRefreshToken("rt1", "1.2.3.4", "Logout"); // revoke once

        var act = () => user.RevokeRefreshToken("rt1", "1.2.3.4"); // try again
        act.Should().Throw<InvalidTokenException>().WithMessage("*already inactive*");
    }

    [Fact]
    public void RevokeRefreshToken_NonExistentToken_ThrowsInvalidTokenException()
    {
        var user = ValidUser();
        var act  = () => user.RevokeRefreshToken("doesnt_exist", "1.2.3.4");
        act.Should().Throw<InvalidTokenException>().WithMessage("*not found*");
    }

    // ── CreateViaOAuth ────────────────────────────────────────────────────────

    [Fact]
    public void CreateViaOAuth_SetsEmailAsVerified()
    {
        var user = User.CreateViaOAuth(
            "oauth@example.com", "OAuth", "User",
            null, "google_id_123", null, UserRole.Candidate);

        user.IsEmailVerified.Should().BeTrue();
        user.GoogleId.Should().Be("google_id_123");
        user.EmailVerificationToken.Should().BeNull();
    }

    [Fact]
    public void CreateViaOAuth_HasEmptyPasswordHash()
    {
        var user = User.CreateViaOAuth(
            "oauth@example.com", "OAuth", "User",
            "linkedin_id", null, null, UserRole.Employer);

        user.PasswordHash.Should().BeEmpty();
        user.LinkedInId.Should().Be("linkedin_id");
    }

    [Fact]
    public void CreateViaOAuth_RaisesUserRegisteredEvent()
    {
        var user = User.CreateViaOAuth(
            "oauth@example.com", "A", "B",
            null, null, "az_object_id", UserRole.Candidate);

        user.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<UserRegisteredEvent>();
    }

    // ── ClearDomainEvents ─────────────────────────────────────────────────────

    [Fact]
    public void ClearDomainEvents_RemovesAllEvents()
    {
        var user = ValidUser();
        user.DomainEvents.Should().NotBeEmpty();

        user.ClearDomainEvents();

        user.DomainEvents.Should().BeEmpty();
    }

    // ── Lockout escalation ────────────────────────────────────────────────────

    [Fact]
    public void RecordFailedLogin_FourAttempts_NotYetLocked()
    {
        var user = ValidUser();
        for (int i = 0; i < 4; i++) user.RecordFailedLogin();

        user.IsLockedOut.Should().BeFalse();
        user.FailedLoginAttempts.Should().Be(4);
    }

    [Fact]
    public void RecordFailedLogin_MoreThanFive_ExtendsLockoutDuration()
    {
        var user = ValidUser();
        for (int i = 0; i < 8; i++) user.RecordFailedLogin();

        // 8 attempts → min(8*5, 60) = 40 min lockout (> 5-attempt 25 min)
        user.IsLockedOut.Should().BeTrue();
        user.LockedOutUntil.Should().BeAfter(DateTimeOffset.UtcNow.AddMinutes(25));
    }

    // ── Domain events after actions ───────────────────────────────────────────

    [Fact]
    public void RecordSuccessfulLogin_RaisesUserLoggedInEvent()
    {
        var user = ValidUser();
        user.ClearDomainEvents();

        user.RecordSuccessfulLogin("1.2.3.4");

        user.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<UserLoggedInEvent>();
    }

    [Fact]
    public void VerifyEmail_ValidToken_RaisesUserEmailVerifiedEvent()
    {
        var user  = ValidUser();
        var token = user.EmailVerificationToken!;
        user.ClearDomainEvents();

        user.VerifyEmail(token);

        user.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<UserEmailVerifiedEvent>();
    }

    // ── LinkAzureAdAccount ────────────────────────────────────────────────────

    [Fact]
    public void LinkAzureAdAccount_SetsObjectId()
    {
        var user = ValidUser();
        user.LinkAzureAdAccount("az-object-id-999");
        user.AzureAdObjectId.Should().Be("az-object-id-999");
    }

    // ── IsLockedOut — expired lockout ─────────────────────────────────────────

    [Fact]
    public void RecordSuccessfulLogin_AfterLockout_ClearsLockedOutUntil()
    {
        var user = ValidUser();
        for (int i = 0; i < 5; i++) user.RecordFailedLogin();
        user.IsLockedOut.Should().BeTrue();

        user.RecordSuccessfulLogin("1.2.3.4");

        user.IsLockedOut.Should().BeFalse();
        user.LockedOutUntil.Should().BeNull();
    }
}
