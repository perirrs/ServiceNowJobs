using Xunit;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SNHub.Auth.Infrastructure.Persistence;
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

    // ── DB state helpers (test-only — manipulate user state directly) ─────────

    /// <summary>
    /// Suspends a user directly in the DB, bypassing the API.
    /// Used for testing scenarios that require an admin action not yet exposed
    /// as an API endpoint (will be wired to /admin in a later step).
    /// </summary>
    private async Task SuspendUserInDbAsync(string email, string reason)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();

        var user = await db.Users.FirstAsync(u => u.NormalizedEmail == email.ToUpperInvariant());
        user.Suspend(reason, "test-admin");
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Deactivates a user directly in the DB (soft-delete simulation).
    /// The login handler treats !IsActive identically to user-not-found.
    /// </summary>
    private async Task DeactivateUserInDbAsync(string email)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();

        // Use raw SQL to set is_active = false — User.Deactivate() may not exist yet
        await db.Database.ExecuteSqlRawAsync(
            "UPDATE auth.users SET is_active = false, updated_at = now() WHERE normalized_email = {0}",
            email.ToUpperInvariant());
    }
}
