using System.Net;
using FluentAssertions;
using SNHub.Auth.IntegrationTests.Models;

namespace SNHub.Auth.IntegrationTests.Apis.Auth;

public sealed partial class AuthApiTests
{
    // ── Refresh — happy path ──────────────────────────────────────────────────

    [Fact]
    public async Task Refresh_ValidTokenPair_Returns200WithNewTokens()
    {
        // given
        var auth = await RegisterAndLoginAsync();

        // when
        var (response, body) = await _broker.RefreshAsync(new RefreshRequest(
            AccessToken:  auth.AccessToken,
            RefreshToken: auth.RefreshToken));

        // then
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        body!.Success.Should().BeTrue();
        body.Data!.AccessToken.Should().NotBeNullOrEmpty();
        body.Data.RefreshToken.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Refresh_ValidToken_ReturnsNewDistinctTokens()
    {
        // given
        var auth = await RegisterAndLoginAsync();

        // when
        var (_, body) = await _broker.RefreshAsync(new RefreshRequest(
            auth.AccessToken, auth.RefreshToken));

        // then — tokens must change
        body!.Data!.AccessToken.Should().NotBe(auth.AccessToken);
        body.Data.RefreshToken.Should().NotBe(auth.RefreshToken);
    }

    [Fact]
    public async Task Refresh_OldRefreshToken_AfterUse_Returns400()
    {
        // given — use the token once to get new tokens
        var auth = await RegisterAndLoginAsync();
        var first = new RefreshRequest(auth.AccessToken, auth.RefreshToken);
        await _broker.RefreshAsync(first);

        // when — try to reuse the original token
        var (response, _) = await _broker.RefreshAsync(first);

        // then — InvalidTokenException -> 400 (not 401)
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── Refresh — invalid tokens ──────────────────────────────────────────────

    [Fact]
    public async Task Refresh_InvalidRefreshToken_Returns400()
    {
        // InvalidTokenException ("Invalid refresh token.") -> 400 INVALID_TOKEN
        var (response, _) = await _broker.RefreshAsync(new RefreshRequest(
            AccessToken:  "some.jwt.token",
            RefreshToken: "completely_invalid_token_12345"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Refresh_InvalidToken_ErrorCodeIsInvalidToken()
    {
        var (response, _) = await _broker.RefreshAsync(new RefreshRequest(
            "fake.jwt", "fake_refresh"));

        var json = await response.Content.ReadAsStringAsync();
        json.Should().Contain("INVALID_TOKEN");
    }

    // ── Refresh — validation ──────────────────────────────────────────────────

    [Fact]
    public async Task Refresh_EmptyAccessToken_Returns400()
    {
        var (response, _) = await _broker.RefreshAsync(new RefreshRequest("", "refresh_token"));
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Refresh_EmptyRefreshToken_Returns400()
    {
        var (response, _) = await _broker.RefreshAsync(new RefreshRequest("access_token", ""));
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── Revoke — happy path ───────────────────────────────────────────────────

    [Fact]
    public async Task Revoke_ValidRefreshToken_Returns204()
    {
        // given
        var auth = await RegisterAndLoginAsync();

        // when
        var response = await _broker.RevokeAsync(new RevokeRequest(auth.RefreshToken));

        // then
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Revoke_ValidToken_ThenRefreshReturns400()
    {
        // given — revoke the token
        var auth = await RegisterAndLoginAsync();
        await _broker.RevokeAsync(new RevokeRequest(auth.RefreshToken));

        // when — try to refresh with revoked token
        var (response, _) = await _broker.RefreshAsync(new RefreshRequest(
            auth.AccessToken, auth.RefreshToken));

        // then — InvalidTokenException -> 400
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── Revoke — already revoked ──────────────────────────────────────────────

    [Fact]
    public async Task Revoke_AlreadyRevokedToken_Returns400()
    {
        // given — revoke once
        var auth = await RegisterAndLoginAsync();
        await _broker.RevokeAsync(new RevokeRequest(auth.RefreshToken));

        // when — revoke again
        var response = await _broker.RevokeAsync(new RevokeRequest(auth.RefreshToken));

        // then — InvalidTokenException -> 400
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Revoke_InvalidToken_Returns400()
    {
        // InvalidTokenException ("Token not found.") -> 400 INVALID_TOKEN
        var response = await _broker.RevokeAsync(new RevokeRequest("totally_fake_token"));
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Revoke_InvalidToken_ErrorCodeIsInvalidToken()
    {
        var response = await _broker.RevokeAsync(new RevokeRequest("fake_token_xyz"));
        var json = await response.Content.ReadAsStringAsync();
        json.Should().Contain("INVALID_TOKEN");
    }
}
