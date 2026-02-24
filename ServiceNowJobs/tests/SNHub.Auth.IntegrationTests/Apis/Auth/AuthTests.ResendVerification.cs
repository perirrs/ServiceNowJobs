using Xunit;
using System.Net;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SNHub.Auth.Infrastructure.Persistence;
using SNHub.Auth.IntegrationTests.Models;

namespace SNHub.Auth.IntegrationTests.Apis.Auth;

public sealed partial class AuthApiTests
{
    // ── ResendVerification — happy path ───────────────────────────────────────

    /// <summary>
    /// Unverified user requests a new verification link — should always return 202
    /// regardless of whether the email exists, preventing email enumeration.
    /// </summary>
    [Fact]
    public async Task ResendVerification_UnverifiedUser_Returns202()
    {
        // given — freshly registered user (email not yet verified)
        await _factory.ResetDatabaseAsync();
        var (req, _) = await RegisterFreshUserAsync();

        // when
        var response = await _broker.ResendVerificationAsync(
            new ResendVerificationRequest(req.Email));

        // then
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
    }

    [Fact]
    public async Task ResendVerification_UnverifiedUser_RegeneratesToken()
    {
        // given — register and capture original token
        await _factory.ResetDatabaseAsync();
        var (req, _) = await RegisterFreshUserAsync();
        var originalToken = await GetEmailVerificationTokenFromDbAsync(req.Email);

        // when — resend
        await _broker.ResendVerificationAsync(new ResendVerificationRequest(req.Email));

        // then — token should have changed
        var newToken = await GetEmailVerificationTokenFromDbAsync(req.Email);
        newToken.Should().NotBeNull();
        newToken.Should().NotBe(originalToken, "resend must invalidate the old token");
    }

    [Fact]
    public async Task ResendVerification_UnverifiedUser_NewTokenVerifiesEmail()
    {
        // given — register and resend
        await _factory.ResetDatabaseAsync();
        var (req, _) = await RegisterFreshUserAsync();
        await _broker.ResendVerificationAsync(new ResendVerificationRequest(req.Email));

        // when — verify with the NEW token
        var newToken = await GetEmailVerificationTokenFromDbAsync(req.Email);
        var response = await _broker.VerifyEmailAsync(
            new VerifyEmailRequest(req.Email, newToken!));

        // then
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // and user is now verified in DB
        var isVerified = await IsEmailVerifiedInDbAsync(req.Email);
        isVerified.Should().BeTrue();
    }

    [Fact]
    public async Task ResendVerification_UnverifiedUser_OldTokenNoLongerWorks()
    {
        // given — capture old token, then resend
        await _factory.ResetDatabaseAsync();
        var (req, _) = await RegisterFreshUserAsync();
        var oldToken = await GetEmailVerificationTokenFromDbAsync(req.Email);
        await _broker.ResendVerificationAsync(new ResendVerificationRequest(req.Email));

        // when — try to verify with the OLD token
        var response = await _broker.VerifyEmailAsync(
            new VerifyEmailRequest(req.Email, oldToken!));

        // then — old token must fail
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── ResendVerification — email enumeration protection ─────────────────────

    [Fact]
    public async Task ResendVerification_UnknownEmail_Returns202()
    {
        // Never reveal that no account exists with this email
        var response = await _broker.ResendVerificationAsync(
            new ResendVerificationRequest("nobody@snhub-integration.io"));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
    }

    [Fact]
    public async Task ResendVerification_AlreadyVerifiedEmail_Returns202()
    {
        // Never reveal that the account is already verified
        await _factory.ResetDatabaseAsync();
        var (req, _) = await RegisterFreshUserAsync();

        // Verify the email first
        var token = await GetEmailVerificationTokenFromDbAsync(req.Email);
        await _broker.VerifyEmailAsync(new VerifyEmailRequest(req.Email, token!));

        // when — resend for an already-verified account
        var response = await _broker.ResendVerificationAsync(
            new ResendVerificationRequest(req.Email));

        // then — still 202, no hint that verification was already done
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
    }

    [Fact]
    public async Task ResendVerification_AlreadyVerifiedEmail_DoesNotChangeToken()
    {
        // Resending for an already-verified account must NOT generate a new token
        await _factory.ResetDatabaseAsync();
        var (req, _) = await RegisterFreshUserAsync();

        var token = await GetEmailVerificationTokenFromDbAsync(req.Email);
        await _broker.VerifyEmailAsync(new VerifyEmailRequest(req.Email, token));

        // After verification the token column must be NULL — use nullable helper
        // (the regular helper asserts token is not null, which would throw here)
        var tokenAfterVerification = await TryGetEmailVerificationTokenFromDbAsync(req.Email);
        tokenAfterVerification.Should().BeNull("token is cleared when email is verified");

        // when — resend for already-verified account
        await _broker.ResendVerificationAsync(new ResendVerificationRequest(req.Email));

        // then — token must remain null (no new token generated for verified accounts)
        var tokenAfterResend = await TryGetEmailVerificationTokenFromDbAsync(req.Email);
        tokenAfterResend.Should().BeNull("resend must not regenerate token for verified accounts");
    }

    // ── ResendVerification — validation ──────────────────────────────────────

    [Fact]
    public async Task ResendVerification_EmptyEmail_Returns400()
    {
        var response = await _broker.ResendVerificationAsync(
            new ResendVerificationRequest(""));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ResendVerification_InvalidEmailFormat_Returns400()
    {
        var response = await _broker.ResendVerificationAsync(
            new ResendVerificationRequest("not-an-email"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── Helper: check verified state in DB ───────────────────────────────────

    private async Task<bool> IsEmailVerifiedInDbAsync(string email)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        return await db.Users
            .AsNoTracking()
            .Where(u => u.NormalizedEmail == email.ToUpperInvariant())
            .Select(u => u.IsEmailVerified)
            .FirstOrDefaultAsync();
    }

    /// <summary>
    /// Reads the verification token without asserting it is non-null.
    /// Use this when the token may legitimately be null (e.g. after verification clears it).
    /// </summary>
    private async Task<string?> TryGetEmailVerificationTokenFromDbAsync(string email)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        return await db.Users
            .AsNoTracking()
            .Where(u => u.NormalizedEmail == email.ToUpperInvariant())
            .Select(u => u.EmailVerificationToken)
            .FirstOrDefaultAsync();
    }
}
