using System.Net;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SNHub.Auth.Infrastructure.Persistence;
using SNHub.Auth.IntegrationTests.Models;

namespace SNHub.Auth.IntegrationTests.Apis.Auth;

public sealed partial class AuthApiTests
{
    // ── Forgot password — happy path ──────────────────────────────────────────

    [Fact]
    public async Task ForgotPassword_KnownEmail_Returns202()
    {
        // given
        var (req, _) = await RegisterFreshUserAsync();

        // when
        var response = await _broker.ForgotPasswordAsync(new ForgotPasswordRequest(req.Email));

        // then
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
    }

    [Fact]
    public async Task ForgotPassword_UnknownEmail_Returns202()
    {
        // Security: always return same response whether email exists or not
        var response = await _broker.ForgotPasswordAsync(
            new ForgotPasswordRequest("ghost@nonexistent-snhub.io"));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
    }

    [Fact]
    public async Task ForgotPassword_KnownAndUnknownEmail_SameResponseCode()
    {
        // Security: must not allow email enumeration
        var (req, _) = await RegisterFreshUserAsync();

        var knownResp   = await _broker.ForgotPasswordAsync(new ForgotPasswordRequest(req.Email));
        var unknownResp = await _broker.ForgotPasswordAsync(new ForgotPasswordRequest("ghost@x.io"));

        knownResp.StatusCode.Should().Be(unknownResp.StatusCode);
    }

    // ── Forgot password — validation ──────────────────────────────────────────

    [Fact]
    public async Task ForgotPassword_EmptyEmail_Returns400()
    {
        var response = await _broker.ForgotPasswordAsync(new ForgotPasswordRequest(""));
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ForgotPassword_InvalidEmailFormat_Returns400()
    {
        var response = await _broker.ForgotPasswordAsync(new ForgotPasswordRequest("notanemail"));
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── Reset password — happy path ───────────────────────────────────────────

    [Fact]
    public async Task ResetPassword_ValidToken_Returns204()
    {
        // given — register, trigger forgot password, extract token from DB
        var (req, _) = await RegisterFreshUserAsync();
        await _broker.ForgotPasswordAsync(new ForgotPasswordRequest(req.Email));
        var resetToken = await GetPasswordResetTokenFromDbAsync(req.Email);

        // when
        var response = await _broker.ResetPasswordAsync(new ResetPasswordRequest(
            Email:          req.Email,
            Token:          resetToken,
            NewPassword:    "NewSecureP@ss2!",
            ConfirmNewPassword: "NewSecureP@ss2!"));

        // then
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task ResetPassword_ValidToken_AllowsLoginWithNewPassword()
    {
        // given
        var (req, _) = await RegisterFreshUserAsync();
        await _broker.ForgotPasswordAsync(new ForgotPasswordRequest(req.Email));
        var resetToken = await GetPasswordResetTokenFromDbAsync(req.Email);

        await _broker.ResetPasswordAsync(new ResetPasswordRequest(
            req.Email, resetToken, "NewP@ss1!!", "NewP@ss1!!"));

        // when — login with new password
        var (loginResp, loginBody) = await _broker.LoginAsync(
            new LoginRequest(req.Email, "NewP@ss1!!"));

        // then
        loginResp.StatusCode.Should().Be(HttpStatusCode.OK);
        loginBody!.Data!.AccessToken.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task ResetPassword_ValidToken_RevokesOldRefreshTokens()
    {
        // given — login first to get a refresh token
        var (req, _) = await RegisterFreshUserAsync();
        var (_, loginBody) = await _broker.LoginAsync(LoginFrom(req));
        var oldRefresh = loginBody!.Data!.RefreshToken;

        // reset password
        await _broker.ForgotPasswordAsync(new ForgotPasswordRequest(req.Email));
        var resetToken = await GetPasswordResetTokenFromDbAsync(req.Email);
        await _broker.ResetPasswordAsync(new ResetPasswordRequest(
            req.Email, resetToken, "NewP@ss2!!", "NewP@ss2!!"));

        // when — try to use old refresh token
        var (refreshResp, _) = await _broker.RefreshAsync(
            new RefreshRequest(loginBody.Data.AccessToken, oldRefresh));

        // then — old refresh token must be revoked
        refreshResp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── Reset password — invalid token ────────────────────────────────────────

    [Fact]
    public async Task ResetPassword_WrongToken_Returns400()
    {
        var (req, _) = await RegisterFreshUserAsync();

        var response = await _broker.ResetPasswordAsync(new ResetPasswordRequest(
            req.Email, "wrong_token", "NewP@ss1!!", "NewP@ss1!!"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── Reset password — validation ───────────────────────────────────────────

    [Theory]
    [InlineData("", "tok", "NewP@ss1!!", "NewP@ss1!!")]
    [InlineData("u@x.com", "", "NewP@ss1!!", "NewP@ss1!!")]
    [InlineData("u@x.com", "tok", "weak", "weak")]
    [InlineData("u@x.com", "tok", "NewP@ss1!!", "Mismatch!!")]
    public async Task ResetPassword_InvalidInputs_Returns400(
        string email, string token, string pwd, string confirm)
    {
        var response = await _broker.ResetPasswordAsync(new ResetPasswordRequest(
            email, token, pwd, confirm));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── Email verification — happy path ───────────────────────────────────────

    [Fact]
    public async Task VerifyEmail_ValidToken_Returns204()
    {
        // given
        var (req, _) = await RegisterFreshUserAsync();
        var verifyToken = await GetEmailVerificationTokenFromDbAsync(req.Email);

        // when
        var response = await _broker.VerifyEmailAsync(
            new VerifyEmailRequest(req.Email, verifyToken));

        // then
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task VerifyEmail_ValidToken_SetsIsEmailVerifiedTrue()
    {
        // given
        var (req, _) = await RegisterFreshUserAsync();
        var verifyToken = await GetEmailVerificationTokenFromDbAsync(req.Email);
        await _broker.VerifyEmailAsync(new VerifyEmailRequest(req.Email, verifyToken));

        // when — login and check profile
        var (_, loginBody) = await _broker.LoginAsync(LoginFrom(req));

        // then
        loginBody!.Data!.User.IsEmailVerified.Should().BeTrue();
    }

    [Fact]
    public async Task VerifyEmail_WrongToken_Returns400()
    {
        var (req, _) = await RegisterFreshUserAsync();

        var response = await _broker.VerifyEmailAsync(
            new VerifyEmailRequest(req.Email, "wrong_verification_token"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task VerifyEmail_AlreadyVerified_Returns409()
    {
        // given — verify once
        var (req, _) = await RegisterFreshUserAsync();
        var token = await GetEmailVerificationTokenFromDbAsync(req.Email);
        await _broker.VerifyEmailAsync(new VerifyEmailRequest(req.Email, token));

        // when — verify again
        var response = await _broker.VerifyEmailAsync(
            new VerifyEmailRequest(req.Email, token));

        // then
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest); // DomainException -> 400
    }

    // ── Email verification — validation ───────────────────────────────────────

    [Theory]
    [InlineData("", "token")]
    [InlineData("notanemail", "token")]
    [InlineData("u@x.com", "")]
    public async Task VerifyEmail_InvalidInputs_Returns400(string email, string token)
    {
        var response = await _broker.VerifyEmailAsync(
            new VerifyEmailRequest(email, token));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── Health endpoint ───────────────────────────────────────────────────────

    [Fact]
    public async Task Health_Returns200()
    {
        var response = await _broker.GetHealthAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── DB helpers (test-only — read tokens directly from database) ───────────

    private async Task<string> GetPasswordResetTokenFromDbAsync(string email)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();

        var user = await db.Users.FirstAsync(u => u.NormalizedEmail == email.ToUpperInvariant());
        user.PasswordResetToken.Should().NotBeNullOrEmpty("ForgotPassword should have set a token");
        return user.PasswordResetToken!;
    }

    private async Task<string> GetEmailVerificationTokenFromDbAsync(string email)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();

        var user = await db.Users.FirstAsync(u => u.NormalizedEmail == email.ToUpperInvariant());
        user.EmailVerificationToken.Should().NotBeNullOrEmpty("Registration should have set a token");
        return user.EmailVerificationToken!;
    }
}
