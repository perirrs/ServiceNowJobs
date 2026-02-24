using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SNHub.Auth.Infrastructure.Persistence;
using SNHub.Auth.IntegrationTests.Brokers;
using SNHub.Auth.IntegrationTests.Models;
using System.Net;
using Xunit;

namespace SNHub.Auth.IntegrationTests.Apis.Users;

[Collection(nameof(AuthApiCollection))]
public sealed class AdminUsersTests
{
    private readonly AuthApiBroker _broker;
    private readonly AuthWebApplicationFactory _factory;

    public AdminUsersTests(AuthWebApplicationFactory factory)
    {
        _factory = factory;
        _broker  = new AuthApiBroker(factory);
    }

    // ── Setup helpers ─────────────────────────────────────────────────────────

    private static string RandomEmail() =>
        $"admin_{Guid.NewGuid():N}@snhub-admin-test.io";

    private static RegisterRequest RandomCandidate() => new(
        Email:           RandomEmail(),
        Password:        "IntegP@ss1!",
        ConfirmPassword: "IntegP@ss1!",
        FirstName:       "Test",
        LastName:        "Candidate",
        Role:            5 /* Candidate */);

    /// <summary>Register a user, promote them to SuperAdmin via DB, login, set token.</summary>
    private async Task<(RegisterRequest Req, AuthResponse Auth)> LoginAsSuperAdminAsync()
    {
        var req = RandomCandidate();
        var (_, regBody) = await _broker.RegisterAsync(req);
        regBody!.Data.Should().NotBeNull();

        // Promote to SuperAdmin directly in DB (can't register as SuperAdmin via API)
        await SetRoleInDbAsync(req.Email, roleJson: "[1]");

        var (_, loginBody) = await _broker.LoginAsync(new LoginRequest(req.Email, req.Password));
        loginBody!.Data.Should().NotBeNull();

        _broker.SetBearerToken(loginBody.Data!.AccessToken);
        return (req, loginBody.Data);
    }

    /// <summary>Register a candidate user, login, set token. Returns the user's ID.</summary>
    private async Task<(RegisterRequest Req, Guid UserId)> RegisterCandidateAsync()
    {
        var req = RandomCandidate();
        var (_, regBody) = await _broker.RegisterAsync(req);
        regBody!.Data.Should().NotBeNull();
        return (req, regBody.Data!.User.Id);
    }

    private async Task SetRoleInDbAsync(string email, string roleJson)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        // roleJson is a test-controlled constant (never user input) but we still
        // avoid string interpolation in SQL to suppress the EF1002 warning.
        await db.Database.ExecuteSqlRawAsync(
            "UPDATE auth.users SET roles = {0}::jsonb WHERE normalized_email = {1}",
            roleJson, email.ToUpperInvariant());
    }

    // ── GET /admin/users ──────────────────────────────────────────────────────

    [Fact]
    public async Task GetUsers_AsSuperAdmin_Returns200WithPagedList()
    {
        await _factory.ResetDatabaseAsync();
        await LoginAsSuperAdminAsync();
        await RegisterCandidateAsync();
        await RegisterCandidateAsync();

        var (response, body) = await _broker.GetUsersAsync(page: 1, pageSize: 20);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        body!.Data!.TotalCount.Should().BeGreaterThanOrEqualTo(2);
        body.Data.Items.Should().NotBeEmpty();
    }

    [Fact]
    public async Task GetUsers_WithSearch_FiltersResults()
    {
        await _factory.ResetDatabaseAsync();
        await LoginAsSuperAdminAsync();

        var unique = $"unique_{Guid.NewGuid():N}";
        var req = new RegisterRequest(
            $"{unique}@snhub.io", "IntegP@ss1!", "IntegP@ss1!", "Test", "User", 5);
        await _broker.RegisterAsync(req);

        var (response, body) = await _broker.GetUsersAsync(search: unique);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        body!.Data!.Items.Should().ContainSingle(u => u.Email.Contains(unique));
    }

    [Fact]
    public async Task GetUsers_WithoutToken_Returns401()
    {
        await _factory.ResetDatabaseAsync();
        _broker.ClearBearerToken();

        var (response, _) = await _broker.GetUsersAsync();

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetUsers_AsCandidate_Returns403()
    {
        await _factory.ResetDatabaseAsync();

        // Login as a regular candidate (not admin)
        var req = RandomCandidate();
        await _broker.RegisterAsync(req);
        var (_, loginBody) = await _broker.LoginAsync(new LoginRequest(req.Email, req.Password));
        _broker.SetBearerToken(loginBody!.Data!.AccessToken);

        var (response, _) = await _broker.GetUsersAsync();

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetUsers_PaginationWorks()
    {
        await _factory.ResetDatabaseAsync();
        await LoginAsSuperAdminAsync();

        // Register 3 more users (4 total including the admin)
        for (var i = 0; i < 3; i++) await RegisterCandidateAsync();

        var (resp1, page1) = await _broker.GetUsersAsync(page: 1, pageSize: 2);
        var (resp2, page2) = await _broker.GetUsersAsync(page: 2, pageSize: 2);

        resp1.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        resp2.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);

        page1.Should().NotBeNull();
        page1!.Data.Should().NotBeNull();
        page2.Should().NotBeNull();
        page2!.Data.Should().NotBeNull();

        page1.Data!.Page.Should().Be(1);
        page1.Data.PageSize.Should().Be(2);
        page1.Data.Items.Count().Should().BeLessOrEqualTo(2);
        page2.Data!.Page.Should().Be(2);
    }

    // ── GET /admin/users/{id} ─────────────────────────────────────────────────

    [Fact]
    public async Task GetUserById_ExistingUser_Returns200WithDetails()
    {
        await _factory.ResetDatabaseAsync();
        await LoginAsSuperAdminAsync();
        var (req, userId) = await RegisterCandidateAsync();

        var (response, body) = await _broker.GetUserByIdAsync(userId);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        body!.Data!.Id.Should().Be(userId);
        body.Data.Email.Should().Be(req.Email.ToLower());
        body.Data.IsSuspended.Should().BeFalse();
        body.Data.FailedLoginAttempts.Should().Be(0);
    }

    [Fact]
    public async Task GetUserById_NonExistentUser_Returns404()
    {
        await _factory.ResetDatabaseAsync();
        await LoginAsSuperAdminAsync();

        var (response, _) = await _broker.GetUserByIdAsync(Guid.NewGuid());

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── PUT /admin/users/{id}/suspend ─────────────────────────────────────────

    [Fact]
    public async Task SuspendUser_ValidUser_Returns204AndUserIsSuspended()
    {
        await _factory.ResetDatabaseAsync();
        await LoginAsSuperAdminAsync();
        var (_, userId) = await RegisterCandidateAsync();

        var response = await _broker.SuspendUserAsync(
            userId, new SuspendUserRequest("Violated terms of service."));

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Verify suspension via GET
        var (_, adminBody) = await _broker.GetUserByIdAsync(userId);
        adminBody!.Data!.IsSuspended.Should().BeTrue();
        adminBody.Data.SuspensionReason.Should().Be("Violated terms of service.");
    }

    [Fact]
    public async Task SuspendUser_EmptyReason_Returns400()
    {
        await _factory.ResetDatabaseAsync();
        await LoginAsSuperAdminAsync();
        var (_, userId) = await RegisterCandidateAsync();

        var response = await _broker.SuspendUserAsync(
            userId, new SuspendUserRequest(""));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task SuspendUser_NonExistentUser_Returns404()
    {
        await _factory.ResetDatabaseAsync();
        await LoginAsSuperAdminAsync();

        var response = await _broker.SuspendUserAsync(
            Guid.NewGuid(), new SuspendUserRequest("Test reason."));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task SuspendUser_AsCandidate_Returns403()
    {
        await _factory.ResetDatabaseAsync();

        var req = RandomCandidate();
        await _broker.RegisterAsync(req);
        var (_, loginBody) = await _broker.LoginAsync(new LoginRequest(req.Email, req.Password));
        _broker.SetBearerToken(loginBody!.Data!.AccessToken);

        var response = await _broker.SuspendUserAsync(
            Guid.NewGuid(), new SuspendUserRequest("reason"));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── PUT /admin/users/{id}/reinstate ───────────────────────────────────────

    [Fact]
    public async Task ReinstateUser_SuspendedUser_Returns204AndUserIsReinstated()
    {
        await _factory.ResetDatabaseAsync();
        await LoginAsSuperAdminAsync();
        var (_, userId) = await RegisterCandidateAsync();

        // First suspend
        await _broker.SuspendUserAsync(userId, new SuspendUserRequest("Temporary ban."));

        // Then reinstate
        var reinstateResponse = await _broker.ReinstateUserAsync(userId);

        reinstateResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var (_, adminBody) = await _broker.GetUserByIdAsync(userId);
        adminBody!.Data!.IsSuspended.Should().BeFalse();
        adminBody.Data.SuspensionReason.Should().BeNull();
    }

    [Fact]
    public async Task ReinstateUser_NotSuspended_Returns400()
    {
        await _factory.ResetDatabaseAsync();
        await LoginAsSuperAdminAsync();
        var (_, userId) = await RegisterCandidateAsync();

        // Reinstate a user who is not suspended
        var response = await _broker.ReinstateUserAsync(userId);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ReinstateUser_NonExistentUser_Returns404()
    {
        await _factory.ResetDatabaseAsync();
        await LoginAsSuperAdminAsync();

        var response = await _broker.ReinstateUserAsync(Guid.NewGuid());

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── PUT /admin/users/{id}/roles ───────────────────────────────────────────

    [Fact]
    public async Task UpdateUserRoles_ValidRoles_Returns204()
    {
        await _factory.ResetDatabaseAsync();
        await LoginAsSuperAdminAsync();
        var (_, userId) = await RegisterCandidateAsync();

        // Change to Employer (3) + HiringManager (4)
        var response = await _broker.UpdateUserRolesAsync(
            userId, new UpdateUserRolesRequest(new[] { 3, 4 }));

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var (_, adminBody) = await _broker.GetUserByIdAsync(userId);
        adminBody!.Data!.Roles.Should().ContainInOrder("Employer", "HiringManager");
    }

    [Fact]
    public async Task UpdateUserRoles_AssignSuperAdmin_Returns400()
    {
        await _factory.ResetDatabaseAsync();
        await LoginAsSuperAdminAsync();
        var (_, userId) = await RegisterCandidateAsync();

        // SuperAdmin role = 1 — should be rejected by validator
        var response = await _broker.UpdateUserRolesAsync(
            userId, new UpdateUserRolesRequest(new[] { 1 }));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UpdateUserRoles_EmptyRoles_Returns400()
    {
        await _factory.ResetDatabaseAsync();
        await LoginAsSuperAdminAsync();
        var (_, userId) = await RegisterCandidateAsync();

        var response = await _broker.UpdateUserRolesAsync(
            userId, new UpdateUserRolesRequest(Array.Empty<int>()));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UpdateUserRoles_NonExistentUser_Returns404()
    {
        await _factory.ResetDatabaseAsync();
        await LoginAsSuperAdminAsync();

        var response = await _broker.UpdateUserRolesAsync(
            Guid.NewGuid(), new UpdateUserRolesRequest(new[] { 5 }));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
