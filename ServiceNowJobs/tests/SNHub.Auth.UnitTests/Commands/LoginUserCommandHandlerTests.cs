using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using SNHub.Auth.Application.Commands.LoginUser;
using SNHub.Auth.Application.Interfaces;
using SNHub.Auth.Domain.Entities;
using SNHub.Auth.Domain.Enums;
using SNHub.Auth.Domain.Exceptions;
using Xunit;

namespace SNHub.Auth.UnitTests.Commands;

public sealed class LoginUserCommandHandlerTests
{
    private readonly Mock<IUserRepository>    _users       = new();
    private readonly Mock<IUnitOfWork>        _uow         = new();
    private readonly Mock<IPasswordHasher>    _hasher      = new();
    private readonly Mock<ITokenService>      _tokens      = new();
    private readonly Mock<ICurrentUserService> _currentUser = new();
    private readonly Mock<ILogger<LoginUserCommandHandler>> _logger = new();

    private LoginUserCommandHandler Handler() => new(
        _users.Object, _uow.Object, _hasher.Object,
        _tokens.Object, _currentUser.Object, _logger.Object);

    private static LoginUserCommand ValidCommand() => new(
        Email:    "jane@example.com",
        Password: "Pass@123!");

    private User ValidActiveUser()
    {
        var user = User.Create(
            "jane@example.com", "hashed_pass",
            "Jane", "Doe", UserRole.Candidate);
        // Verify email so login doesn't fail on that
        user.VerifyEmail(user.EmailVerificationToken!);
        return user;
    }

    private void SetupHappyPath(User user)
    {
        _users.Setup(r => r.GetByEmailWithTokensAsync("jane@example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        _hasher.Setup(h => h.VerifyPassword("Pass@123!", user.PasswordHash)).Returns(true);
        _tokens.Setup(t => t.GenerateAccessToken(user)).Returns("access_token");
        _tokens.Setup(t => t.GenerateRefreshToken()).Returns("refresh_token");
        _currentUser.Setup(c => c.IpAddress).Returns("10.0.0.1");
        _currentUser.Setup(c => c.UserAgent).Returns("TestAgent");
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
    }

    // ── Happy path ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_ValidCredentials_ReturnsAuthResponse()
    {
        var user = ValidActiveUser();
        SetupHappyPath(user);

        var result = await Handler().Handle(ValidCommand(), CancellationToken.None);

        result.Should().NotBeNull();
        result.AccessToken.Should().Be("access_token");
        result.RefreshToken.Should().Be("refresh_token");
        result.User.Email.Should().Be("jane@example.com");
    }

    [Fact]
    public async Task Handle_ValidCredentials_RecordsSuccessfulLogin()
    {
        var user = ValidActiveUser();
        SetupHappyPath(user);

        await Handler().Handle(ValidCommand(), CancellationToken.None);

        user.FailedLoginAttempts.Should().Be(0);
        user.LastLoginIp.Should().Be("10.0.0.1");
    }

    [Fact]
    public async Task Handle_ValidCredentials_SavesChanges()
    {
        var user = ValidActiveUser();
        SetupHappyPath(user);

        await Handler().Handle(ValidCommand(), CancellationToken.None);

        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_RememberMe_SetsLongerRefreshExpiry()
    {
        var user = ValidActiveUser();
        _users.Setup(r => r.GetByEmailWithTokensAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        _hasher.Setup(h => h.VerifyPassword(It.IsAny<string>(), It.IsAny<string>())).Returns(true);
        _tokens.Setup(t => t.GenerateAccessToken(It.IsAny<User>())).Returns("at");
        _tokens.Setup(t => t.GenerateRefreshToken()).Returns("rt");
        _currentUser.Setup(c => c.IpAddress).Returns("ip");
        _currentUser.Setup(c => c.UserAgent).Returns("ua");
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var result = await Handler().Handle(
            ValidCommand() with { RememberMe = true }, CancellationToken.None);

        // RememberMe = 90 days vs default 30
        result.RefreshTokenExpiry.Should()
            .BeCloseTo(DateTimeOffset.UtcNow.AddDays(90), precision: TimeSpan.FromSeconds(5));
    }

    // ── User not found ────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_UserNotFound_ThrowsInvalidCredentials()
    {
        _users.Setup(r => r.GetByEmailWithTokensAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        var act = () => Handler().Handle(ValidCommand(), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidCredentialsException>();
    }

    // ── Wrong password ────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_WrongPassword_ThrowsInvalidCredentials()
    {
        var user = ValidActiveUser();
        _users.Setup(r => r.GetByEmailWithTokensAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        _hasher.Setup(h => h.VerifyPassword(It.IsAny<string>(), It.IsAny<string>())).Returns(false);
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var act = () => Handler().Handle(ValidCommand(), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidCredentialsException>();
    }

    [Fact]
    public async Task Handle_WrongPassword_IncrementsFailedAttempts()
    {
        var user = ValidActiveUser();
        _users.Setup(r => r.GetByEmailWithTokensAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        _hasher.Setup(h => h.VerifyPassword(It.IsAny<string>(), It.IsAny<string>())).Returns(false);
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        try { await Handler().Handle(ValidCommand(), CancellationToken.None); } catch { }

        user.FailedLoginAttempts.Should().Be(1);
    }

    // ── Locked account ────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_LockedAccount_ThrowsAccountLockedException()
    {
        var user = ValidActiveUser();
        // Simulate 5 failed attempts to trigger lockout
        for (int i = 0; i < 5; i++) user.RecordFailedLogin();

        _users.Setup(r => r.GetByEmailWithTokensAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        var act = () => Handler().Handle(ValidCommand(), CancellationToken.None);

        await act.Should().ThrowAsync<AccountLockedException>();
    }

    // ── Suspended account ─────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_SuspendedAccount_ThrowsAccountSuspendedException()
    {
        var user = ValidActiveUser();
        user.Suspend("ToS violation", "admin");

        _users.Setup(r => r.GetByEmailWithTokensAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        var act = () => Handler().Handle(ValidCommand(), CancellationToken.None);

        await act.Should().ThrowAsync<AccountSuspendedException>();
    }

    // ── Validator ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("", "password")]
    [InlineData("notanemail", "password")]
    [InlineData("jane@example.com", "")]
    public async Task Validator_InvalidInputs_Fails(string email, string password)
    {
        var validator = new LoginUserCommandValidator();
        var cmd = new LoginUserCommand(email, password);

        var result = await validator.ValidateAsync(cmd);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validator_ValidInputs_Passes()
    {
        var validator = new LoginUserCommandValidator();
        var result = await validator.ValidateAsync(ValidCommand());
        result.IsValid.Should().BeTrue();
    }
}
