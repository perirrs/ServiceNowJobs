using FluentAssertions;
using SNHub.Auth.Application.Commands.LoginUser;
using Xunit;

namespace SNHub.Auth.UnitTests.Validators;

public sealed class LoginUserValidatorTests
{
    private readonly LoginUserCommandValidator _validator = new();

    private static LoginUserCommand Valid() => new(
        Email:    "jane@example.com",
        Password: "AnyP@ss1!");

    // ── Email ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Email_Valid_Passes() =>
        (await _validator.ValidateAsync(Valid())).IsValid.Should().BeTrue();

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("notanemail")]
    [InlineData("@nodomain")]
    [InlineData("noat.com")]
    public async Task Email_Invalid_Fails(string email)
    {
        var result = await _validator.ValidateAsync(Valid() with { Email = email });
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Email");
    }

    // ── Password ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Password_Valid_Passes() =>
        (await _validator.ValidateAsync(Valid())).IsValid.Should().BeTrue();

    [Fact]
    public async Task Password_Empty_Fails()
    {
        var result = await _validator.ValidateAsync(Valid() with { Password = "" });
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Password");
    }

    [Fact]
    public async Task Password_ExceedsMaxLength_Fails()
    {
        var longPwd = new string('A', 129);
        var result  = await _validator.ValidateAsync(Valid() with { Password = longPwd });
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Password");
    }

    // ── RememberMe is optional — any bool is valid ────────────────────────────

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RememberMe_AnyValue_Passes(bool rememberMe)
    {
        var result = await _validator.ValidateAsync(Valid() with { RememberMe = rememberMe });
        result.IsValid.Should().BeTrue();
    }

    // ── Both fields invalid ───────────────────────────────────────────────────

    [Fact]
    public async Task BothFields_Invalid_ReturnsTwoErrors()
    {
        var result = await _validator.ValidateAsync(new LoginUserCommand("bad", ""));
        result.IsValid.Should().BeFalse();
        result.Errors.Count.Should().Be(2);
    }
}
