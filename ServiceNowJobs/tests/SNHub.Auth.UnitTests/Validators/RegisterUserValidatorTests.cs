using FluentAssertions;
using SNHub.Auth.Application.Commands.RegisterUser;
using SNHub.Auth.Domain.Enums;
using Xunit;

namespace SNHub.Auth.UnitTests.Validators;

public sealed class RegisterUserValidatorTests
{
    private readonly RegisterUserCommandValidator _validator = new();

    private static RegisterUserCommand Valid() => new(
        Email:           "john@example.com",
        Password:        "SecureP@ss1",
        ConfirmPassword: "SecureP@ss1",
        FirstName:       "John",
        LastName:        "Doe",
        Role:            UserRole.Candidate);

    // ── Email ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Email_Valid_Passes() =>
        (await _validator.ValidateAsync(Valid())).IsValid.Should().BeTrue();

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("notanemail")]
    [InlineData("@nodomain.com")]
    [InlineData("noat.com")]
    public async Task Email_Invalid_Fails(string email)
    {
        var result = await _validator.ValidateAsync(Valid() with { Email = email });
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Email");
    }

    [Fact]
    public async Task Email_ExceedsMaxLength_Fails()
    {
        var longEmail = new string('a', 250) + "@x.com";
        var result = await _validator.ValidateAsync(Valid() with { Email = longEmail });
        result.IsValid.Should().BeFalse();
    }

    // ── Password ──────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("short1A!")]     // valid — exactly 8 chars with all requirements
    [InlineData("V@lidPa55word")]
    public async Task Password_Valid_Passes(string password)
    {
        var result = await _validator.ValidateAsync(
            Valid() with { Password = password, ConfirmPassword = password });
        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]            // empty
    [InlineData("weak")]        // too short, no uppercase/digit/special
    [InlineData("nouppercase1!")] // no uppercase
    [InlineData("NOLOWERCASE1!")] // no lowercase
    [InlineData("NoSpecial123")]  // no special char
    [InlineData("NoNumber@A!")]   // no digit
    [InlineData("Short1@")]       // only 7 chars
    public async Task Password_Weak_Fails(string password)
    {
        var result = await _validator.ValidateAsync(
            Valid() with { Password = password, ConfirmPassword = password });
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Password");
    }

    // ── ConfirmPassword ───────────────────────────────────────────────────────

    [Fact]
    public async Task ConfirmPassword_Mismatch_Fails()
    {
        var result = await _validator.ValidateAsync(
            Valid() with { ConfirmPassword = "DifferentP@ss1" });
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "ConfirmPassword");
    }

    // ── FirstName ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("John")]
    [InlineData("Mary-Jane")]
    [InlineData("O'Brien")]
    [InlineData("Jean Pierre")]
    public async Task FirstName_Valid_Passes(string name)
    {
        var result = await _validator.ValidateAsync(Valid() with { FirstName = name });
        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("John123")]
    [InlineData("John<script>")]
    public async Task FirstName_Invalid_Fails(string name)
    {
        var result = await _validator.ValidateAsync(Valid() with { FirstName = name });
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "FirstName");
    }

    // ── LastName ──────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("")]
    [InlineData("Doe123")]
    public async Task LastName_Invalid_Fails(string name)
    {
        var result = await _validator.ValidateAsync(Valid() with { LastName = name });
        result.IsValid.Should().BeFalse();
    }

    // ── Role ──────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(UserRole.Candidate)]
    [InlineData(UserRole.Employer)]
    [InlineData(UserRole.HiringManager)]
    public async Task Role_AllowedSelfAssign_Passes(UserRole role)
    {
        var result = await _validator.ValidateAsync(Valid() with { Role = role });
        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(UserRole.SuperAdmin)]
    [InlineData(UserRole.Moderator)]
    public async Task Role_Privileged_Fails(UserRole role)
    {
        var result = await _validator.ValidateAsync(Valid() with { Role = role });
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Role");
    }

    // ── Optional Fields ───────────────────────────────────────────────────────

    [Theory]
    [InlineData("+447911123456")]
    [InlineData("+12125551234")]
    public async Task PhoneNumber_ValidE164_Passes(string phone)
    {
        var result = await _validator.ValidateAsync(Valid() with { PhoneNumber = phone });
        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("07911 123456")]  // UK local format without +
    [InlineData("not-a-phone")]
    [InlineData("00000")]
    public async Task PhoneNumber_Invalid_Fails(string phone)
    {
        var result = await _validator.ValidateAsync(Valid() with { PhoneNumber = phone });
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task PhoneNumber_Null_IsOptional_Passes()
    {
        var result = await _validator.ValidateAsync(Valid() with { PhoneNumber = null });
        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("GB")]
    [InlineData("US")]
    [InlineData("IND")] // 3-char ISO
    public async Task Country_ValidISOCode_Passes(string country)
    {
        var result = await _validator.ValidateAsync(Valid() with { Country = country });
        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("X")]         // too short
    [InlineData("GBRI")]      // too long
    [InlineData("12")]        // numeric
    public async Task Country_InvalidCode_Fails(string country)
    {
        var result = await _validator.ValidateAsync(Valid() with { Country = country });
        result.IsValid.Should().BeFalse();
    }

    // ── Multiple errors at once ───────────────────────────────────────────────

    [Fact]
    public async Task AllFieldsInvalid_ReturnsMultipleErrors()
    {
        var cmd = new RegisterUserCommand(
            Email: "bad",
            Password: "weak",
            ConfirmPassword: "mismatch",
            FirstName: "",
            LastName: "",
            Role: UserRole.SuperAdmin);

        var result = await _validator.ValidateAsync(cmd);

        result.IsValid.Should().BeFalse();
        result.Errors.Count.Should().BeGreaterThan(4);
    }
}
