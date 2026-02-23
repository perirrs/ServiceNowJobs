using Xunit;
using FluentAssertions;
using SNHub.Auth.IntegrationTests.Brokers;
using SNHub.Auth.IntegrationTests.Models;

namespace SNHub.Auth.IntegrationTests.Apis.Auth;

[Collection(nameof(AuthApiCollection))]
public sealed partial class AuthApiTests
{
    private readonly AuthApiBroker          _broker;
    private readonly AuthWebApplicationFactory _factory;

    public AuthApiTests(AuthWebApplicationFactory factory)
    {
        _factory = factory;
        _broker  = new AuthApiBroker(factory);
    }

    // ── Random data factories ─────────────────────────────────────────────────

    private static string RandomEmail() =>
        $"test_{Guid.NewGuid():N}@snhub-integration.io";

    private static RegisterRequest RandomRegisterRequest(int role = 5 /*Candidate*/) => new(
        Email:           RandomEmail(),
        Password:        "IntegP@ss1!",
        ConfirmPassword: "IntegP@ss1!",
        FirstName:       "Test",
        LastName:        "User",
        Role:            role);

    private static LoginRequest LoginFrom(RegisterRequest req) => new(
        Email:    req.Email,
        Password: req.Password);

    /// <summary>Register a fresh user and return the auth response.</summary>
    private async Task<(RegisterRequest Req, AuthResponse Auth)> RegisterFreshUserAsync()
    {
        var req = RandomRegisterRequest();
        var (_, body) = await _broker.RegisterAsync(req);
        body.Should().NotBeNull();
        body!.Data.Should().NotBeNull();
        return (req, body.Data!);
    }

    /// <summary>Register, then login and return auth tokens.</summary>
    private async Task<AuthResponse> RegisterAndLoginAsync()
    {
        var (req, _) = await RegisterFreshUserAsync();
        var (_, loginBody) = await _broker.LoginAsync(LoginFrom(req));
        loginBody.Should().NotBeNull();
        return loginBody!.Data!;
    }
}
