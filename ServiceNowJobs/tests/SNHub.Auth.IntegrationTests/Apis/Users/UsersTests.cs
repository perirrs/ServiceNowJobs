using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SNHub.Auth.Infrastructure.Persistence;
using SNHub.Auth.IntegrationTests.Brokers;
using SNHub.Auth.IntegrationTests.Models;
using Xunit;

namespace SNHub.Auth.IntegrationTests.Apis.Users;

[Collection(nameof(AuthApiCollection))]
public sealed partial class UsersTests
{
    private readonly AuthApiBroker _broker;
    private readonly AuthWebApplicationFactory _factory;

    public UsersTests(AuthWebApplicationFactory factory)
    {
        _factory = factory;
        _broker  = new AuthApiBroker(factory);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string RandomEmail() =>
        $"user_{Guid.NewGuid():N}@snhub-users-test.io";

    private static RegisterRequest RandomRegisterRequest() => new(
        Email:           RandomEmail(),
        Password:        "IntegP@ss1!",
        ConfirmPassword: "IntegP@ss1!",
        FirstName:       "Jane",
        LastName:        "Doe",
        Role:            5 /* Candidate */);

    private async Task<(RegisterRequest Req, string AccessToken)> RegisterAndLoginAsync()
    {
        var req = RandomRegisterRequest();
        var (_, regBody) = await _broker.RegisterAsync(req);
        regBody!.Data.Should().NotBeNull();

        var (_, loginBody) = await _broker.LoginAsync(new LoginRequest(req.Email, req.Password));
        loginBody!.Data.Should().NotBeNull();

        _broker.SetBearerToken(loginBody!.Data!.AccessToken);
        return (req, loginBody.Data.AccessToken);
    }

    private async Task UpgradeToSuperAdminAsync(string email)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        // Set roles JSON directly so SuperAdmin login works for admin tests
        await db.Database.ExecuteSqlRawAsync(
            "UPDATE auth.users SET roles = '[1]'::jsonb WHERE normalized_email = {0}",
            email.ToUpperInvariant());
    }

    // ── GET /me ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetMe_AuthenticatedUser_Returns200WithProfile()
    {
        await _factory.ResetDatabaseAsync();
        var (req, _) = await RegisterAndLoginAsync();

        var (response, body) = await _broker.GetMeAsync();

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        body.Should().NotBeNull();
        body!.Data!.Email.Should().Be(req.Email.ToLower());
        body.Data.FirstName.Should().Be("Jane");
        body.Data.LastName.Should().Be("Doe");
        body.Data.IsEmailVerified.Should().BeFalse();
        body.Data.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task GetMe_NoToken_Returns401()
    {
        await _factory.ResetDatabaseAsync();
        _broker.ClearBearerToken();

        var (response, _) = await _broker.GetMeAsync();

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetMe_InvalidToken_Returns401()
    {
        await _factory.ResetDatabaseAsync();
        _broker.SetBearerToken("not.a.valid.jwt");

        var (response, _) = await _broker.GetMeAsync();

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.Unauthorized);
    }

    // ── PUT /me ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateMe_ValidRequest_Returns200WithUpdatedProfile()
    {
        await _factory.ResetDatabaseAsync();
        await RegisterAndLoginAsync();

        var updateRequest = new UpdateProfileRequest(
            FirstName:   "UpdatedFirst",
            LastName:    "UpdatedLast",
            PhoneNumber: "+447911123456",
            Country:     "GB",
            TimeZone:    "Europe/London");

        var (response, body) = await _broker.UpdateMeAsync(updateRequest);

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        body!.Data!.FirstName.Should().Be("UpdatedFirst");
        body.Data.LastName.Should().Be("UpdatedLast");
        body.Data.PhoneNumber.Should().Be("+447911123456");
        body.Data.Country.Should().Be("GB");
    }

    [Fact]
    public async Task UpdateMe_InvalidFirstName_Returns400()
    {
        await _factory.ResetDatabaseAsync();
        await RegisterAndLoginAsync();

        var (response, _) = await _broker.UpdateMeAsync(
            new UpdateProfileRequest("", "Last"));

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UpdateMe_InvalidPhone_Returns400()
    {
        await _factory.ResetDatabaseAsync();
        await RegisterAndLoginAsync();

        var (response, _) = await _broker.UpdateMeAsync(
            new UpdateProfileRequest("First", "Last", PhoneNumber: "not-a-phone"));

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UpdateMe_NoToken_Returns401()
    {
        await _factory.ResetDatabaseAsync();
        _broker.ClearBearerToken();

        var (response, _) = await _broker.UpdateMeAsync(
            new UpdateProfileRequest("First", "Last"));

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task UpdateMe_ClearsOptionalFieldsWithNull_Returns200()
    {
        await _factory.ResetDatabaseAsync();
        await RegisterAndLoginAsync();

        // First set some optional fields
        await _broker.UpdateMeAsync(new UpdateProfileRequest(
            "Jane", "Doe", PhoneNumber: "+447911123456", Country: "GB"));

        // Then clear them
        var (response, body) = await _broker.UpdateMeAsync(
            new UpdateProfileRequest("Jane", "Doe"));

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        body!.Data!.PhoneNumber.Should().BeNull();
        body.Data.Country.Should().BeNull();
    }

    // ── POST /me/profile-picture ──────────────────────────────────────────────

    [Fact]
    public async Task UploadProfilePicture_ValidJpeg_Returns200WithUrl()
    {
        await _factory.ResetDatabaseAsync();
        await RegisterAndLoginAsync();

        // Minimal valid JPEG bytes (1x1 pixel)
        var jpegBytes = Convert.FromBase64String(
            "/9j/4AAQSkZJRgABAQAAAQABAAD/2wBDAAgGBgcGBQgHBwcJCQgKDBQNDAsLDBkSEw8U" +
            "HRofHh0aHBwgJC4nICIsIxwcKDcpLDAxNDQ0Hyc5PTgyPC4zNDL/2wBDAQkJCQwLDBgN" +
            "DRgyIRwhMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIy" +
            "MjL/wAARCAABAAEDASIAAhEBAxEB/8QAFgABAQEAAAAAAAAAAAAAAAAABgUE/8QAHxAAAQME" +
            "AwAAAAAAAAAAAAAAAQIDBBEhMVH/xAAUAQEAAAAAAAAAAAAAAAAAAAAA/8QAFBEBAAAAAAAAAA" +
            "AAAAAAAAAAAP/aAAwDAQACEQMRAD8AoJpJJqO5KlBPvlwADM//2Q==");

        var (response, body) = await _broker.UploadProfilePictureAsync(
            jpegBytes, "avatar.jpg", "image/jpeg");

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        body!.Data!.ProfilePictureUrl.Should().NotBeNullOrEmpty();
        body.Data.ProfilePictureUrl.Should().StartWith("https://");
    }

    [Fact]
    public async Task UploadProfilePicture_TooLargeFile_Returns400()
    {
        await _factory.ResetDatabaseAsync();
        await RegisterAndLoginAsync();

        var largeFile = new byte[6 * 1024 * 1024]; // 6 MB — over 5 MB limit

        var (response, _) = await _broker.UploadProfilePictureAsync(
            largeFile, "big.jpg", "image/jpeg");

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UploadProfilePicture_UnsupportedContentType_Returns400()
    {
        await _factory.ResetDatabaseAsync();
        await RegisterAndLoginAsync();

        var (response, _) = await _broker.UploadProfilePictureAsync(
            new byte[] { 0x47, 0x49, 0x46 }, "file.gif", "image/gif");

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UploadProfilePicture_NoToken_Returns401()
    {
        await _factory.ResetDatabaseAsync();
        _broker.ClearBearerToken();

        var (response, _) = await _broker.UploadProfilePictureAsync(
            new byte[] { 1, 2, 3 }, "img.jpg", "image/jpeg");

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.Unauthorized);
    }
}
