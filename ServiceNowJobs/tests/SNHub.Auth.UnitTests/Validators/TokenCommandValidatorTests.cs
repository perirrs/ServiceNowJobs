using FluentAssertions;
using SNHub.Auth.Application.Commands.ForgotPassword;
using SNHub.Auth.Application.Commands.RefreshToken;
using SNHub.Auth.Application.Commands.ResetPassword;
using SNHub.Auth.Application.Commands.RevokeToken;
using SNHub.Auth.Application.Commands.VerifyEmail;
using Xunit;

namespace SNHub.Auth.UnitTests.Validators;

// ── RefreshToken ──────────────────────────────────────────────────────────────

public sealed class RefreshTokenValidatorTests
{
    private readonly RefreshTokenCommandValidator _validator = new();

    [Fact]
    public async Task BothTokensProvided_Passes()
    {
        var result = await _validator.ValidateAsync(new RefreshTokenCommand("access", "refresh"));
        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("", "refresh")]
    [InlineData("access", "")]
    [InlineData("", "")]
    public async Task EmptyToken_Fails(string access, string refresh)
    {
        var result = await _validator.ValidateAsync(new RefreshTokenCommand(access, refresh));
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task EmptyAccessToken_ErrorOnCorrectProperty()
    {
        var result = await _validator.ValidateAsync(new RefreshTokenCommand("", "rt"));
        result.Errors.Should().Contain(e => e.PropertyName == "AccessToken");
    }

    [Fact]
    public async Task EmptyRefreshToken_ErrorOnCorrectProperty()
    {
        var result = await _validator.ValidateAsync(new RefreshTokenCommand("at", ""));
        result.Errors.Should().Contain(e => e.PropertyName == "RefreshToken");
    }
}

// ── RevokeToken ───────────────────────────────────────────────────────────────

public sealed class RevokeTokenValidatorTests
{
    private readonly RevokeTokenCommandValidator _validator = new();

    [Fact]
    public async Task ValidToken_Passes()
    {
        var result = await _validator.ValidateAsync(new RevokeTokenCommand("valid_refresh_token"));
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task EmptyToken_Fails()
    {
        var result = await _validator.ValidateAsync(new RevokeTokenCommand(""));
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "RefreshToken");
    }

    [Fact]
    public async Task WhitespaceToken_Fails()
    {
        var result = await _validator.ValidateAsync(new RevokeTokenCommand("   "));
        result.IsValid.Should().BeFalse();
    }
}

// ── ForgotPassword ────────────────────────────────────────────────────────────

public sealed class ForgotPasswordValidatorTests
{
    private readonly ForgotPasswordCommandValidator _validator = new();

    [Theory]
    [InlineData("user@example.com")]
    [InlineData("user+tag@domain.co.uk")]
    public async Task ValidEmail_Passes(string email)
    {
        var result = await _validator.ValidateAsync(new ForgotPasswordCommand(email));
        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("notanemail")]
    [InlineData("@nodomain")]
    public async Task InvalidEmail_Fails(string email)
    {
        var result = await _validator.ValidateAsync(new ForgotPasswordCommand(email));
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Email");
    }
}

// ── ResetPassword ─────────────────────────────────────────────────────────────

public sealed class ResetPasswordValidatorTests
{
    private readonly ResetPasswordCommandValidator _validator = new();

    private static ResetPasswordCommand Valid() => new(
        Email:              "u@example.com",
        Token:              "valid_reset_token",
        NewPassword:        "NewSecureP@ss1!",
        ConfirmNewPassword: "NewSecureP@ss1!");

    [Fact]
    public async Task ValidCommand_Passes() =>
        (await _validator.ValidateAsync(Valid())).IsValid.Should().BeTrue();

    [Theory]
    [InlineData("")]
    [InlineData("notanemail")]
    public async Task Email_Invalid_Fails(string email)
    {
        var result = await _validator.ValidateAsync(Valid() with { Email = email });
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Email");
    }

    [Fact]
    public async Task Token_Empty_Fails()
    {
        var result = await _validator.ValidateAsync(Valid() with { Token = "" });
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Token");
    }

    [Theory]
    [InlineData("weak")]             // too short
    [InlineData("nouppercase1!")]    // no uppercase
    [InlineData("NOLOWERCASE1!")]    // no lowercase
    [InlineData("NoSpecial123")]     // no special
    [InlineData("NoNumber@A!")]      // no digit
    public async Task NewPassword_Weak_Fails(string password)
    {
        var result = await _validator.ValidateAsync(
            Valid() with { NewPassword = password, ConfirmNewPassword = password });
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "NewPassword");
    }

    [Fact]
    public async Task ConfirmPassword_Mismatch_Fails()
    {
        var result = await _validator.ValidateAsync(
            Valid() with { ConfirmNewPassword = "DifferentP@ss1!" });
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "ConfirmNewPassword");
    }

    [Fact]
    public async Task MultipleInvalid_ReturnsMultipleErrors()
    {
        var cmd    = new ResetPasswordCommand("", "", "weak", "mismatch");
        var result = await _validator.ValidateAsync(cmd);
        result.IsValid.Should().BeFalse();
        result.Errors.Count.Should().BeGreaterThan(2);
    }
}

// ── VerifyEmail ───────────────────────────────────────────────────────────────

public sealed class VerifyEmailValidatorTests
{
    private readonly VerifyEmailCommandValidator _validator = new();

    [Fact]
    public async Task ValidInputs_Passes()
    {
        var result = await _validator.ValidateAsync(
            new VerifyEmailCommand("u@example.com", "verification_token"));
        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("notanemail")]
    public async Task Email_Invalid_Fails(string email)
    {
        var result = await _validator.ValidateAsync(
            new VerifyEmailCommand(email, "token"));
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Email");
    }

    [Fact]
    public async Task Token_Empty_Fails()
    {
        var result = await _validator.ValidateAsync(
            new VerifyEmailCommand("u@example.com", ""));
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Token");
    }

    [Fact]
    public async Task BothInvalid_ReturnsMultipleErrors()
    {
        var result = await _validator.ValidateAsync(new VerifyEmailCommand("bad", ""));
        result.IsValid.Should().BeFalse();
        result.Errors.Count.Should().Be(2);
    }
}
