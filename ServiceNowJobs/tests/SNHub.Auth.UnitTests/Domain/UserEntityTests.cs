using FluentAssertions;
using SNHub.Auth.Domain.Entities;
using SNHub.Auth.Domain.Enums;
using SNHub.Auth.Domain.Exceptions;
using Xunit;

namespace SNHub.Auth.UnitTests.Domain;

public sealed class UserEntityTests
{
    private static User ValidUser() =>
        User.Create("test@example.com", "password_hash", "John", "Doe", UserRole.Candidate);

    [Fact]
    public void Create_ValidInputs_SetsCorrectState()
    {
        var user = ValidUser();

        user.Id.Should().NotBeEmpty();
        user.Email.Should().Be("test@example.com");
        user.IsActive.Should().BeTrue();
        user.IsEmailVerified.Should().BeFalse();
        user.IsSuspended.Should().BeFalse();
        user.FailedLoginAttempts.Should().Be(0);
        user.Roles.Should().Contain(UserRole.Candidate);
        user.EmailVerificationToken.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Create_EmptyEmail_ThrowsDomainException()
    {
        var act = () => User.Create("", "hash", "John", "Doe", UserRole.Candidate);
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Create_NormalizesEmail()
    {
        var user = User.Create("JOHN@EXAMPLE.COM", "hash", "John", "Doe", UserRole.Candidate);
        user.Email.Should().Be("john@example.com");
    }

    [Fact]
    public void Create_RaisesUserRegisteredEvent()
    {
        var user = ValidUser();
        user.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<Domain.Events.UserRegisteredEvent>();
    }

    [Fact]
    public void RecordSuccessfulLogin_ResetsFailedAttempts()
    {
        var user = ValidUser();
        user.RecordFailedLogin();
        user.RecordFailedLogin();

        user.RecordSuccessfulLogin("1.2.3.4");

        user.FailedLoginAttempts.Should().Be(0);
        user.LastLoginIp.Should().Be("1.2.3.4");
        user.LockedOutUntil.Should().BeNull();
    }

    [Fact]
    public void RecordFailedLogin_AfterFiveAttempts_LocksAccount()
    {
        var user = ValidUser();
        for (int i = 0; i < 5; i++) user.RecordFailedLogin();

        user.IsLockedOut.Should().BeTrue();
        user.LockedOutUntil.Should().BeAfter(DateTimeOffset.UtcNow);
    }

    [Fact]
    public void VerifyEmail_ValidToken_VerifiesSuccessfully()
    {
        var user = ValidUser();
        var token = user.EmailVerificationToken!;

        user.VerifyEmail(token);

        user.IsEmailVerified.Should().BeTrue();
        user.EmailVerificationToken.Should().BeNull();
    }

    [Fact]
    public void VerifyEmail_WrongToken_ThrowsInvalidTokenException()
    {
        var user = ValidUser();
        var act = () => user.VerifyEmail("wrong");
        act.Should().Throw<InvalidTokenException>();
    }

    [Fact]
    public void VerifyEmail_AlreadyVerified_ThrowsDomainException()
    {
        var user = ValidUser();
        user.VerifyEmail(user.EmailVerificationToken!);

        var act = () => user.VerifyEmail("any");
        act.Should().Throw<DomainException>().WithMessage("*already verified*");
    }

    [Fact]
    public void Suspend_ActiveUser_SuspendsAndRevokesTokens()
    {
        var user = ValidUser();
        user.AddRefreshToken("token1", "1.2.3.4", "UA", DateTimeOffset.UtcNow.AddDays(30));

        user.Suspend("ToS violation", "admin@snhub.io");

        user.IsSuspended.Should().BeTrue();
        user.RefreshTokens.All(t => t.IsRevoked).Should().BeTrue();
    }

    [Fact]
    public void ResetPassword_ValidToken_UpdatesHashAndRevokesTokens()
    {
        var user = ValidUser();
        user.AddRefreshToken("rt1", "1.2.3.4", "UA", DateTimeOffset.UtcNow.AddDays(30));
        user.GeneratePasswordResetToken();
        var token = user.PasswordResetToken!;

        user.ResetPassword(token, "new_hash");

        user.PasswordHash.Should().Be("new_hash");
        user.PasswordResetToken.Should().BeNull();
        user.RefreshTokens.All(t => t.IsRevoked).Should().BeTrue();
    }

    [Fact]
    public void AddRefreshToken_SameDevice_RevokesOldToken()
    {
        var user = ValidUser();
        user.AddRefreshToken("old", "1.2.3.4", "UA", DateTimeOffset.UtcNow.AddDays(30));
        user.AddRefreshToken("new", "1.2.3.4", "UA", DateTimeOffset.UtcNow.AddDays(30));

        user.RefreshTokens.First(t => t.Token == "old").IsRevoked.Should().BeTrue();
        user.RefreshTokens.First(t => t.Token == "new").IsActive.Should().BeTrue();
    }
}
