// ─── RefreshToken Command Handler Tests ───────────────────────────────────────

using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Logging;
using Moq;
using SNHub.Auth.Application.Commands.ForgotPassword;
using SNHub.Auth.Application.Commands.RefreshToken;
using SNHub.Auth.Application.Commands.ResetPassword;
using SNHub.Auth.Application.Commands.RevokeToken;
using SNHub.Auth.Application.Commands.VerifyEmail;
using SNHub.Auth.Application.Interfaces;
using SNHub.Auth.Domain.Entities;
using SNHub.Auth.Domain.Enums;
using SNHub.Auth.Domain.Exceptions;
using Xunit;

namespace SNHub.Auth.UnitTests.Commands;

// ── RefreshToken ──────────────────────────────────────────────────────────────

public sealed class RefreshTokenCommandHandlerTests
{
    private readonly Mock<IUserRepository>     _users       = new();
    private readonly Mock<IUnitOfWork>         _uow         = new();
    private readonly Mock<ITokenService>       _tokens      = new();
    private readonly Mock<ICurrentUserService> _currentUser = new();
    private readonly Mock<ILogger<RefreshTokenCommandHandler>> _logger = new();

    private RefreshTokenCommandHandler Handler() => new(
        _users.Object, _uow.Object, _tokens.Object, _currentUser.Object, _logger.Object);

    private User ValidUserWithToken(out string refreshTokenValue)
    {
        var user = User.Create("u@x.com", "h", "A", "B", UserRole.Candidate);
        refreshTokenValue = "valid_refresh";
        user.AddRefreshToken(refreshTokenValue, "ip", "ua", DateTimeOffset.UtcNow.AddDays(30));
        return user;
    }

    [Fact]
    public async Task Handle_ValidToken_ReturnsNewTokenPair()
    {
        var user = ValidUserWithToken(out var rt);
        _users.Setup(r => r.GetByRefreshTokenAsync(rt, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        _tokens.Setup(t => t.GenerateAccessToken(user)).Returns("new_access");
        _tokens.Setup(t => t.GenerateRefreshToken()).Returns("new_refresh");
        _currentUser.Setup(c => c.IpAddress).Returns("ip");
        _currentUser.Setup(c => c.UserAgent).Returns("ua");
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var result = await Handler().Handle(
            new RefreshTokenCommand("old_access", rt), CancellationToken.None);

        result.AccessToken.Should().Be("new_access");
        result.RefreshToken.Should().Be("new_refresh");
    }

    [Fact]
    public async Task Handle_ValidToken_RevokesOldToken()
    {
        var user = ValidUserWithToken(out var rt);
        _users.Setup(r => r.GetByRefreshTokenAsync(rt, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        _tokens.Setup(t => t.GenerateAccessToken(It.IsAny<User>())).Returns("at");
        _tokens.Setup(t => t.GenerateRefreshToken()).Returns("new_rt");
        _currentUser.Setup(c => c.IpAddress).Returns("ip");
        _currentUser.Setup(c => c.UserAgent).Returns("ua");
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        await Handler().Handle(new RefreshTokenCommand("at", rt), CancellationToken.None);

        user.RefreshTokens.First(t => t.Token == rt).IsRevoked.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_TokenNotFound_ThrowsInvalidTokenException()
    {
        _users.Setup(r => r.GetByRefreshTokenAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        var act = () => Handler().Handle(
            new RefreshTokenCommand("at", "bad_refresh"), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidTokenException>();
    }

    [Fact]
    public async Task Handle_SuspendedUser_ThrowsAccountSuspendedException()
    {
        var user = ValidUserWithToken(out var rt);
        user.Suspend("ban", "admin");
        _users.Setup(r => r.GetByRefreshTokenAsync(rt, It.IsAny<CancellationToken>())).ReturnsAsync(user);

        var act = () => Handler().Handle(new RefreshTokenCommand("at", rt), CancellationToken.None);

        await act.Should().ThrowAsync<AccountSuspendedException>();
    }

    [Theory]
    [InlineData("", "rt")]
    [InlineData("at", "")]
    public async Task Validator_EmptyFields_Fails(string at, string rt)
    {
        var result = await new RefreshTokenCommandValidator()
            .ValidateAsync(new RefreshTokenCommand(at, rt));
        result.IsValid.Should().BeFalse();
    }
}

// ── ForgotPassword ────────────────────────────────────────────────────────────

public sealed class ForgotPasswordCommandHandlerTests
{
    private readonly Mock<IUserRepository> _users  = new();
    private readonly Mock<IUnitOfWork>     _uow    = new();
    private readonly Mock<IEmailService>   _email  = new();
    private readonly Mock<ILogger<ForgotPasswordCommandHandler>> _logger = new();

    private ForgotPasswordCommandHandler Handler() => new(
        _users.Object, _uow.Object, _email.Object, _logger.Object);

    [Fact]
    public async Task Handle_KnownEmail_GeneratesResetTokenAndSendsEmail()
    {
        var user = User.Create("u@x.com", "h", "A", "B", UserRole.Candidate);
        _users.Setup(r => r.GetByEmailAsync("u@x.com", It.IsAny<CancellationToken>())).ReturnsAsync(user);
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        await Handler().Handle(new ForgotPasswordCommand("u@x.com"), CancellationToken.None);

        user.PasswordResetToken.Should().NotBeNullOrEmpty();
        user.PasswordResetTokenExpiry.Should().BeAfter(DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task Handle_UnknownEmail_DoesNotThrow()
    {
        // Security: must not reveal whether email exists
        _users.Setup(r => r.GetByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        var act = () => Handler().Handle(
            new ForgotPasswordCommand("ghost@x.com"), CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Theory]
    [InlineData("")]
    [InlineData("notanemail")]
    public async Task Validator_InvalidEmail_Fails(string email)
    {
        var result = await new ForgotPasswordCommandValidator()
            .ValidateAsync(new ForgotPasswordCommand(email));
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validator_ValidEmail_Passes()
    {
        var result = await new ForgotPasswordCommandValidator()
            .ValidateAsync(new ForgotPasswordCommand("valid@email.com"));
        result.IsValid.Should().BeTrue();
    }
}

// ── ResetPassword ─────────────────────────────────────────────────────────────

public sealed class ResetPasswordCommandHandlerTests
{
    private readonly Mock<IUserRepository> _users  = new();
    private readonly Mock<IUnitOfWork>     _uow    = new();
    private readonly Mock<IPasswordHasher> _hasher = new();
    private readonly Mock<ILogger<ResetPasswordCommandHandler>> _logger = new();

    private ResetPasswordCommandHandler Handler() => new(
        _users.Object, _uow.Object, _hasher.Object, _logger.Object);

    private User UserWithResetToken(out string token)
    {
        var user = User.Create("u@x.com", "old_hash", "A", "B", UserRole.Candidate);
        user.GeneratePasswordResetToken();
        token = user.PasswordResetToken!;
        return user;
    }

    [Fact]
    public async Task Handle_ValidToken_UpdatesPasswordHash()
    {
        var user = UserWithResetToken(out var token);
        _users.Setup(r => r.GetByEmailAsync("u@x.com", It.IsAny<CancellationToken>())).ReturnsAsync(user);
        _hasher.Setup(h => h.HashPassword("NewP@ss1!")).Returns("new_hash");
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        await Handler().Handle(
            new ResetPasswordCommand("u@x.com", token, "NewP@ss1!", "NewP@ss1!"),
            CancellationToken.None);

        user.PasswordHash.Should().Be("new_hash");
        user.PasswordResetToken.Should().BeNull();
    }

    [Fact]
    public async Task Handle_InvalidToken_ThrowsInvalidTokenException()
    {
        var user = UserWithResetToken(out _);
        _users.Setup(r => r.GetByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        var act = () => Handler().Handle(
            new ResetPasswordCommand("u@x.com", "wrong_token", "NewP@ss1!", "NewP@ss1!"),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidTokenException>();
    }

    [Fact]
    public async Task Handle_UserNotFound_ThrowsInvalidTokenException()
    {
        _users.Setup(r => r.GetByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        var act = () => Handler().Handle(
            new ResetPasswordCommand("x@x.com", "t", "NewP@ss1!", "NewP@ss1!"),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidTokenException>();
    }

    [Theory]
    [InlineData("", "token", "NewP@ss1!", "NewP@ss1!")]    // empty email
    [InlineData("x@x.com", "", "NewP@ss1!", "NewP@ss1!")]  // empty token
    [InlineData("x@x.com", "t", "weak", "weak")]           // weak password
    [InlineData("x@x.com", "t", "NewP@ss1!", "Mismatch1!")] // mismatch
    public async Task Validator_InvalidInputs_Fails(
        string email, string token, string pwd, string confirm)
    {
        var result = await new ResetPasswordCommandValidator()
            .ValidateAsync(new ResetPasswordCommand(email, token, pwd, confirm));
        result.IsValid.Should().BeFalse();
    }
}

// ── RevokeToken ───────────────────────────────────────────────────────────────

public sealed class RevokeTokenCommandHandlerTests
{
    private readonly Mock<IUserRepository>     _users       = new();
    private readonly Mock<IUnitOfWork>         _uow         = new();
    private readonly Mock<ICurrentUserService> _currentUser = new();
    private readonly Mock<ILogger<RevokeTokenCommandHandler>> _logger = new();

    private RevokeTokenCommandHandler Handler() => new(
        _users.Object, _uow.Object, _currentUser.Object, _logger.Object);

    [Fact]
    public async Task Handle_ValidToken_RevokesAndSaves()
    {
        var user = User.Create("u@x.com", "h", "A", "B", UserRole.Candidate);
        user.AddRefreshToken("tok", "ip", "ua", DateTimeOffset.UtcNow.AddDays(30));
        _users.Setup(r => r.GetByRefreshTokenAsync("tok", It.IsAny<CancellationToken>())).ReturnsAsync(user);
        _currentUser.Setup(c => c.IpAddress).Returns("ip");
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        await Handler().Handle(new RevokeTokenCommand("tok"), CancellationToken.None);

        user.RefreshTokens.First(t => t.Token == "tok").IsRevoked.Should().BeTrue();
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_TokenNotFound_ThrowsInvalidTokenException()
    {
        _users.Setup(r => r.GetByRefreshTokenAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        var act = () => Handler().Handle(
            new RevokeTokenCommand("bad_token"), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidTokenException>();
    }

    [Fact]
    public async Task Validator_EmptyToken_Fails()
    {
        var result = await new RevokeTokenCommandValidator()
            .ValidateAsync(new RevokeTokenCommand(""));
        result.IsValid.Should().BeFalse();
    }
}

// ── VerifyEmail ───────────────────────────────────────────────────────────────

public sealed class VerifyEmailCommandHandlerTests
{
    private readonly Mock<IUserRepository> _users  = new();
    private readonly Mock<IUnitOfWork>     _uow    = new();
    private readonly Mock<ILogger<VerifyEmailCommandHandler>> _logger = new();

    private VerifyEmailCommandHandler Handler() => new(
        _users.Object, _uow.Object, _logger.Object);

    [Fact]
    public async Task Handle_ValidToken_MarksEmailVerified()
    {
        var user = User.Create("u@x.com", "h", "A", "B", UserRole.Candidate);
        var token = user.EmailVerificationToken!;
        _users.Setup(r => r.GetByEmailAsync("u@x.com", It.IsAny<CancellationToken>())).ReturnsAsync(user);
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        await Handler().Handle(
            new VerifyEmailCommand("u@x.com", token), CancellationToken.None);

        user.IsEmailVerified.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_WrongToken_ThrowsInvalidTokenException()
    {
        var user = User.Create("u@x.com", "h", "A", "B", UserRole.Candidate);
        _users.Setup(r => r.GetByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(user);

        var act = () => Handler().Handle(
            new VerifyEmailCommand("u@x.com", "wrong_token"), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidTokenException>();
    }

    [Fact]
    public async Task Handle_UserNotFound_ThrowsInvalidTokenException()
    {
        _users.Setup(r => r.GetByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        var act = () => Handler().Handle(
            new VerifyEmailCommand("ghost@x.com", "t"), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidTokenException>();
    }

    [Theory]
    [InlineData("", "token")]
    [InlineData("notanemail", "token")]
    [InlineData("u@x.com", "")]
    public async Task Validator_InvalidInputs_Fails(string email, string token)
    {
        var result = await new VerifyEmailCommandValidator()
            .ValidateAsync(new VerifyEmailCommand(email, token));
        result.IsValid.Should().BeFalse();
    }
}
