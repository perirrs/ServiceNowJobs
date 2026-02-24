using FluentAssertions;
using FluentValidation;
using Moq;
using SNHub.Auth.Application.Commands.ForgotPassword;
using SNHub.Auth.Application.Commands.ResendVerification;
using SNHub.Auth.Application.Commands.ResetPassword;
using SNHub.Auth.Application.Commands.VerifyEmail;
using SNHub.Auth.Application.Interfaces;
using SNHub.Auth.Domain.Entities;
using SNHub.Auth.Domain.Enums;
using SNHub.Auth.Domain.Exceptions;
using Xunit;

namespace SNHub.Auth.UnitTests.Commands;

// ════════════════════════════════════════════════════════════════════════════════
// ResendVerification — Handler
// ════════════════════════════════════════════════════════════════════════════════

public sealed class ResendVerificationCommandHandlerTests
{
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IUnitOfWork>     _uow   = new();
    private readonly Mock<IEmailService>   _email = new();

    private static readonly Mock<Microsoft.Extensions.Logging.ILogger<ResendVerificationCommandHandler>>
        _logger = new();

    private ResendVerificationCommandHandler Handler() =>
        new(_users.Object, _uow.Object, _email.Object, _logger.Object);

    private static User MakeUnverifiedUser(string email = "u@test.com") =>
        User.Create(email, "hash", "First", "Last", UserRole.Candidate);

    [Fact]
    public async Task Handle_UnverifiedUser_RegeneratesTokenAndPersists()
    {
        // arrange
        var user = MakeUnverifiedUser();
        _users.Setup(x => x.GetByEmailAsync(user.Email, It.IsAny<CancellationToken>()))
              .ReturnsAsync(user);

        // act
        await Handler().Handle(new ResendVerificationCommand(user.Email), CancellationToken.None);

        // assert — token was generated and saved
        user.EmailVerificationToken.Should().NotBeNullOrEmpty();
        _uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_UnverifiedUser_SendsVerificationEmail()
    {
        // arrange
        var user = MakeUnverifiedUser();
        _users.Setup(x => x.GetByEmailAsync(user.Email, It.IsAny<CancellationToken>()))
              .ReturnsAsync(user);

        // act
        await Handler().Handle(new ResendVerificationCommand(user.Email), CancellationToken.None);

        // assert — email service was called with the new token
        _email.Verify(x => x.SendEmailVerificationAsync(
            user.Email, user.FirstName,
            It.Is<string>(t => !string.IsNullOrEmpty(t)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_UnknownEmail_DoesNotThrowOrSendEmail()
    {
        // arrange — user not found
        _users.Setup(x => x.GetByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync((User?)null);

        // act — must not throw (silent no-op prevents email enumeration)
        var act = async () => await Handler().Handle(
            new ResendVerificationCommand("ghost@test.com"), CancellationToken.None);

        await act.Should().NotThrowAsync();
        _email.Verify(x => x.SendEmailVerificationAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_AlreadyVerifiedUser_DoesNotSendEmail()
    {
        // arrange — user exists but is already verified
        var user = User.Create("v@test.com", "hash", "Verified", "User", UserRole.Candidate);
        user.GenerateEmailVerificationToken();
        user.VerifyEmail(user.EmailVerificationToken!); // actually verify it

        _users.Setup(x => x.GetByEmailAsync(user.Email, It.IsAny<CancellationToken>()))
              .ReturnsAsync(user);

        // act
        await Handler().Handle(new ResendVerificationCommand(user.Email), CancellationToken.None);

        // assert — silent no-op: email not sent, DB not touched
        _email.Verify(x => x.SendEmailVerificationAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_AlreadyVerifiedUser_ReturnsUnitValue()
    {
        var user = User.Create("v2@test.com", "hash", "V", "U", UserRole.Candidate);
        user.GenerateEmailVerificationToken();
        user.VerifyEmail(user.EmailVerificationToken!);

        _users.Setup(x => x.GetByEmailAsync(user.Email, It.IsAny<CancellationToken>()))
              .ReturnsAsync(user);

        // act — must not throw
        var act = async () => await Handler().Handle(
            new ResendVerificationCommand(user.Email), CancellationToken.None);

        await act.Should().NotThrowAsync();
    }
}

// ════════════════════════════════════════════════════════════════════════════════
// ResendVerification — Validator
// ════════════════════════════════════════════════════════════════════════════════

public sealed class ResendVerificationCommandValidatorTests
{
    private readonly ResendVerificationCommandValidator _sut = new();

    [Fact]
    public void Validate_ValidEmail_Passes()
    {
        var result = _sut.Validate(new ResendVerificationCommand("user@example.com"));
        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("", "Email is required.")]
    [InlineData("not-an-email", "A valid email address is required.")]
    public void Validate_InvalidEmail_Fails(string email, string expectedError)
    {
        var result = _sut.Validate(new ResendVerificationCommand(email));
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.ErrorMessage == expectedError);
    }

    [Fact]
    public void Validate_EmailTooLong_Fails()
    {
        var longEmail = new string('a', 251) + "@x.com"; // 257 chars — exceeds MaximumLength(256)
        var result = _sut.Validate(new ResendVerificationCommand(longEmail));
        result.IsValid.Should().BeFalse();
    }
}

// ════════════════════════════════════════════════════════════════════════════════
// ForgotPassword — additional validator coverage
// ════════════════════════════════════════════════════════════════════════════════

public sealed class ForgotPasswordCommandValidatorTests
{
    private readonly ForgotPasswordCommandValidator _sut = new();

    [Theory]
    [InlineData("user@example.com")]
    [InlineData("admin+tag@company.co.uk")]
    [InlineData("first.last@subdomain.example.org")]
    public void Validate_ValidEmail_Passes(string email)
    {
        var result = _sut.Validate(new ForgotPasswordCommand(email));
        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-an-email")]
    [InlineData("missing@")]
    [InlineData("@no-local.com")]
    public void Validate_InvalidEmail_Fails(string email)
    {
        var result = _sut.Validate(new ForgotPasswordCommand(email));
        result.IsValid.Should().BeFalse();
    }
}

// ════════════════════════════════════════════════════════════════════════════════
// ResetPassword — additional validator coverage
// ════════════════════════════════════════════════════════════════════════════════

public sealed class ResetPasswordCommandValidatorTests
{
    private readonly ResetPasswordCommandValidator _sut = new();

    private static ResetPasswordCommand ValidCommand() => new(
        Email:              "user@example.com",
        Token:              "some-valid-token",
        NewPassword:        "SecureP@ss1!",
        ConfirmNewPassword: "SecureP@ss1!");

    [Fact]
    public void Validate_ValidCommand_Passes()
    {
        var result = _sut.Validate(ValidCommand());
        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("short1!", "short1!", false)]        // too short (<8)
    [InlineData("alllowercase1!", "alllowercase1!", false)]  // no uppercase
    [InlineData("ALLUPPERCASE1!", "ALLUPPERCASE1!", false)]  // no lowercase
    [InlineData("NoNumbers!!!", "NoNumbers!!!", false)]       // no digit
    [InlineData("NoSpecial123", "NoSpecial123", false)]       // no special char
    public void Validate_WeakPassword_Fails(string pwd, string confirm, bool expectedValid)
    {
        var result = _sut.Validate(ValidCommand() with
        {
            NewPassword = pwd,
            ConfirmNewPassword = confirm
        });
        result.IsValid.Should().Be(expectedValid);
    }

    [Fact]
    public void Validate_MismatchedPasswords_Fails()
    {
        var result = _sut.Validate(ValidCommand() with { ConfirmNewPassword = "DifferentP@ss1!" });
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.ErrorMessage == "Passwords do not match.");
    }

    [Fact]
    public void Validate_EmptyToken_Fails()
    {
        var result = _sut.Validate(ValidCommand() with { Token = "" });
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_InvalidEmail_Fails()
    {
        var result = _sut.Validate(ValidCommand() with { Email = "not-valid" });
        result.IsValid.Should().BeFalse();
    }
}

// ════════════════════════════════════════════════════════════════════════════════
// VerifyEmail — additional validator coverage
// ════════════════════════════════════════════════════════════════════════════════

public sealed class VerifyEmailCommandValidatorTests
{
    private readonly VerifyEmailCommandValidator _sut = new();

    [Fact]
    public void Validate_ValidCommand_Passes()
    {
        var result = _sut.Validate(new VerifyEmailCommand("user@example.com", "abc123token"));
        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("", "abc123", false)]           // empty email
    [InlineData("not-email", "abc123", false)]  // invalid email format
    [InlineData("u@e.com", "", false)]          // empty token
    [InlineData("u@e.com", "valid-token", true)] // happy path
    public void Validate_VariousInputs_ReturnsExpected(string email, string token, bool expectedValid)
    {
        var result = _sut.Validate(new VerifyEmailCommand(email, token));
        result.IsValid.Should().Be(expectedValid);
    }

    [Fact]
    public void Validate_EmailTooLong_Fails()
    {
        var email = new string('a', 251) + "@x.com"; // 257 chars — exceeds MaximumLength(256)
        var result = _sut.Validate(new VerifyEmailCommand(email, "token"));
        result.IsValid.Should().BeFalse();
    }
}
