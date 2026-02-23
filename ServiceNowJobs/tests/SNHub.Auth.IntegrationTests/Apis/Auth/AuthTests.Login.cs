using System.Net;
using FluentAssertions;

namespace SNHub.Auth.IntegrationTests.Apis.Auth;

public sealed partial class AuthApiTests
{
    // ── Login — happy path ────────────────────────────────────────────────────

    [Fact]
    public async Task Login_ValidCredentials_Returns200WithTokens()
    {
        // given
        var (req, _) = await RegisterFreshUserAsync();

        // when
        var (response, body) = await _broker.LoginAsync(LoginFrom(req));

        // then
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        body!.Success.Should().BeTrue();
        body.Data!.AccessToken.Should().NotBeNullOrEmpty();
        body.Data.RefreshToken.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Login_ValidCredentials_ReturnsCorrectUserProfile()
    {
        // given
        var (req, _) = await RegisterFreshUserAsync();

        // when
        var (_, body) = await _broker.LoginAsync(LoginFrom(req));

        // then
        body!.Data!.User.Email.Should().Be(req.Email.ToLowerInvariant());
        body.Data.User.FirstName.Should().Be(req.FirstName);
    }

    [Fact]
    public async Task Login_RememberMe_ReturnsLongerRefreshExpiry()
    {
        // given
        var (req, _) = await RegisterFreshUserAsync();

        // when
        var (_, body) = await _broker.LoginAsync(LoginFrom(req) with { RememberMe = true });

        // then — 90-day expiry for "remember me"
        body!.Data!.RefreshTokenExpiry
            .Should()
            .BeCloseTo(DateTimeOffset.UtcNow.AddDays(90), precision: TimeSpan.FromMinutes(2));
    }

    [Fact]
    public async Task Login_DefaultSession_Returns30DayRefreshExpiry()
    {
        // given
        var (req, _) = await RegisterFreshUserAsync();

        // when
        var (_, body) = await _broker.LoginAsync(LoginFrom(req));

        // then — 30-day expiry for normal session
        body!.Data!.RefreshTokenExpiry
            .Should()
            .BeCloseTo(DateTimeOffset.UtcNow.AddDays(30), precision: TimeSpan.FromMinutes(2));
    }

    // ── Login — invalid credentials ───────────────────────────────────────────

    [Fact]
    public async Task Login_WrongPassword_Returns401()
    {
        // given
        var (req, _) = await RegisterFreshUserAsync();

        // when
        var (response, _) = await _broker.LoginAsync(LoginFrom(req) with { Password = "WrongP@ss1!" });

        // then
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Login_UnknownEmail_Returns401()
    {
        // when
        var (response, _) = await _broker.LoginAsync(new Models.LoginRequest(
            "nobody@nonexistent-snhub.io", "SomeP@ss1!"));

        // then — same 401 as wrong password (never reveal if email exists)
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Login_UnknownEmail_SameErrorAsWrongPassword()
    {
        // Security: error messages must be identical to prevent email enumeration
        var (req, _) = await RegisterFreshUserAsync();

        var (wrongPasswordResp, _) = await _broker.LoginAsync(LoginFrom(req) with { Password = "WrongP@ss1!" });
        var (unknownEmailResp, _) = await _broker.LoginAsync(
            new Models.LoginRequest("nobody@snhub.io", "SomeP@ss1!"));

        wrongPasswordResp.StatusCode.Should().Be(unknownEmailResp.StatusCode);
    }

    // ── Login — account lockout ───────────────────────────────────────────────

    [Fact]
    public async Task Login_FiveWrongPasswords_LocksAccount()
    {
        // given
        var (req, _) = await RegisterFreshUserAsync();
        var wrongLogin = LoginFrom(req) with { Password = "WrongP@ss1!" };

        // when — 5 failed attempts
        for (int i = 0; i < 5; i++)
            await _broker.LoginAsync(wrongLogin);

        // then — account locked
        var (response, _) = await _broker.LoginAsync(LoginFrom(req));
        response.StatusCode.Should().Be(HttpStatusCode.Locked);    // 423
    }

    // ── Login — validation ────────────────────────────────────────────────────

    [Fact]
    public async Task Login_EmptyEmail_Returns400()
    {
        var (response, _) = await _broker.LoginAsync(new Models.LoginRequest("", "P@ss1!"));
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Login_EmptyPassword_Returns400()
    {
        var (response, _) = await _broker.LoginAsync(
            new Models.LoginRequest("u@x.com", ""));
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Login_InvalidEmailFormat_Returns400()
    {
        var (response, _) = await _broker.LoginAsync(
            new Models.LoginRequest("notanemail", "P@ss1!"));
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
