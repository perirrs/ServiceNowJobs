using System.Net;
using FluentAssertions;
using SNHub.Auth.IntegrationTests.Models;

namespace SNHub.Auth.IntegrationTests.Apis.Auth;

public sealed partial class AuthApiTests
{
    // ── Register — happy path ─────────────────────────────────────────────────

    [Fact]
    public async Task Register_ValidRequest_Returns201WithTokens()
    {
        // given
        var request = RandomRegisterRequest();

        // when
        var (response, body) = await _broker.RegisterAsync(request);

        // then
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        body.Should().NotBeNull();
        body!.Success.Should().BeTrue();
        body.Data.Should().NotBeNull();
        body.Data!.AccessToken.Should().NotBeNullOrEmpty();
        body.Data.RefreshToken.Should().NotBeNullOrEmpty();
        body.Data.AccessTokenExpiry.Should().BeAfter(DateTimeOffset.UtcNow);
        body.Data.RefreshTokenExpiry.Should().BeAfter(DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task Register_ValidRequest_ReturnsCorrectUserProfile()
    {
        // given
        var request = RandomRegisterRequest();

        // when
        var (_, body) = await _broker.RegisterAsync(request);

        // then
        var user = body!.Data!.User;
        user.Email.Should().Be(request.Email.ToLowerInvariant());
        user.FirstName.Should().Be(request.FirstName);
        user.LastName.Should().Be(request.LastName);
        user.IsEmailVerified.Should().BeFalse();   // needs verification
        user.IsActive.Should().BeTrue();
        user.Roles.Should().Contain("Candidate");
        user.Id.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Register_AsEmployer_ReturnsEmployerRole()
    {
        // given — role 2 = Employer
        var request = RandomRegisterRequest(role: 3);

        // when
        var (_, body) = await _broker.RegisterAsync(request);

        // then
        body!.Data!.User.Roles.Should().Contain("Employer");
    }

    [Fact]
    public async Task Register_RefreshTokenExpiry_IsApproximately30Days()
    {
        // given
        var request = RandomRegisterRequest();

        // when
        var (_, body) = await _broker.RegisterAsync(request);

        // then
        body!.Data!.RefreshTokenExpiry
            .Should()
            .BeCloseTo(DateTimeOffset.UtcNow.AddDays(30), precision: TimeSpan.FromMinutes(2));
    }

    // ── Register — conflict ───────────────────────────────────────────────────

    [Fact]
    public async Task Register_DuplicateEmail_Returns409()
    {
        // given — register once first
        var request = RandomRegisterRequest();
        await _broker.RegisterAsync(request);

        // when — register again with same email
        var (response, _) = await _broker.RegisterAsync(request);

        // then
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Register_DuplicateEmail_CaseInsensitive_Returns409()
    {
        // given
        var request = RandomRegisterRequest();
        await _broker.RegisterAsync(request);

        // when — uppercase variant
        var (response, _) = await _broker.RegisterAsync(
            request with { Email = request.Email.ToUpperInvariant() });

        // then
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    // ── Register — validation failures ───────────────────────────────────────

    [Fact]
    public async Task Register_EmptyEmail_Returns400()
    {
        var (response, _) = await _broker.RegisterAsync(
            RandomRegisterRequest() with { Email = "" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Register_InvalidEmailFormat_Returns400()
    {
        var (response, _) = await _broker.RegisterAsync(
            RandomRegisterRequest() with { Email = "notanemail" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Register_WeakPassword_Returns400()
    {
        var (response, _) = await _broker.RegisterAsync(
            RandomRegisterRequest() with { Password = "weak", ConfirmPassword = "weak" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Register_PasswordMismatch_Returns400()
    {
        var (response, _) = await _broker.RegisterAsync(
            RandomRegisterRequest() with { ConfirmPassword = "DifferentP@ss1!" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Register_EmptyFirstName_Returns400()
    {
        var (response, _) = await _broker.RegisterAsync(
            RandomRegisterRequest() with { FirstName = "" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData(1)]  // SuperAdmin = 1
    [InlineData(2)]  // Moderator = 2
    public async Task Register_PrivilegedRole_Returns400(int role)
    {
        var (response, _) = await _broker.RegisterAsync(
            RandomRegisterRequest(role: role));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Register_InvalidPhoneNumber_Returns400()
    {
        var (response, _) = await _broker.RegisterAsync(
            RandomRegisterRequest() with { PhoneNumber = "not-a-phone" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── Register — error response structure ──────────────────────────────────

    [Fact]
    public async Task Register_ValidationFailure_ReturnsStructuredError()
    {
        var (response, _) = await _broker.RegisterAsync(
            RandomRegisterRequest() with { Email = "bad" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var json = await response.Content.ReadAsStringAsync();
        json.Should().Contain("ErrorCode");
        json.Should().Contain("Errors");
    }
}
