using FluentAssertions;
using SNHub.Users.IntegrationTests.Brokers;
using SNHub.Users.IntegrationTests.Models;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace SNHub.Users.IntegrationTests.Apis;

[Collection(nameof(UsersApiCollection))]
public sealed partial class UsersApiTests
{
    private readonly UsersWebApplicationFactory _factory;
    private static readonly JsonSerializerOptions _json =
        new() { PropertyNameCaseInsensitive = true };

    private static readonly Guid _user1Id = Guid.NewGuid();
    private static readonly Guid _user2Id = Guid.NewGuid();
    private static readonly Guid _adminId = Guid.NewGuid();

    private static readonly string _user1Token =
        UsersWebApplicationFactory.GenerateToken(_user1Id, "Candidate");
    private static readonly string _user2Token =
        UsersWebApplicationFactory.GenerateToken(_user2Id, "Candidate");
    private static readonly string _adminToken =
        UsersWebApplicationFactory.GenerateToken(_adminId, "SuperAdmin");

    private static readonly byte[] _validJpeg = Convert.FromBase64String(
        "/9j/4AAQSkZJRgABAQEASABIAAD/2wBDAAgGBgcGBQgHBwcJCQgKDBQNDAsLDBkS" +
        "Ew8UHRofHh0aHBwgJC4nICIsIxwcKDcpLDAxNDQ0Hyc5PTgyPC4zNDL/wAARCAAB" +
        "AAEDASIAAhEBAxEB/8QAFAABAAAAAAAAAAAAAAAAAAAACf/EABQQAQAAAAAAAAAAAA" +
        "AAAAAAAP/EABQBAQAAAAAAAAAAAAAAAAAAAAD/xAAUEQEAAAAAAAAAAAAAAAAAAAAA" +
        "/9oADAMBAAIRAxEAPwCwABmX/9k=");

    public UsersApiTests(UsersWebApplicationFactory factory) => _factory = factory;

    private UsersApiBroker BrokerFor(string token)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);
        return new UsersApiBroker(client);
    }

    private UsersApiBroker AnonBroker() => new(_factory.CreateClient());
}

// ════════════════════════════════════════════════════════════════════════════════
// GET /users/me
// ════════════════════════════════════════════════════════════════════════════════

public sealed partial class UsersApiTests
{
    [Fact]
    public async Task GetMyProfile_NoProfileYet_Returns404()
    {
        await _factory.ResetDatabaseAsync();
        var response = await BrokerFor(_user1Token).GetMyProfileAsync();
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetMyProfile_AfterUpdate_Returns200()
    {
        await _factory.ResetDatabaseAsync();
        await BrokerFor(_user1Token).UpdateMyProfileAsync(
            new UpdateProfileRequest(FirstName: "Alice", LastName: "Walker", Headline: "ITSM Lead"));

        var response = await BrokerFor(_user1Token).GetMyProfileAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<UserProfileResponse>(_json);
        body!.FirstName.Should().Be("Alice");
        body.Headline.Should().Be("ITSM Lead");
    }

    [Fact]
    public async Task GetMyProfile_NoAuth_Returns401()
    {
        var response = await AnonBroker().GetMyProfileAsync();
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}

// ════════════════════════════════════════════════════════════════════════════════
// PUT /users/me
// ════════════════════════════════════════════════════════════════════════════════

public sealed partial class UsersApiTests
{
    [Fact]
    public async Task UpdateMyProfile_ValidRequest_Returns200()
    {
        await _factory.ResetDatabaseAsync();
        var response = await BrokerFor(_user1Token).UpdateMyProfileAsync(
            new UpdateProfileRequest(FirstName: "Bob", LastName: "Jones",
                Headline: "Flow Designer", YearsOfExperience: 4, IsPublic: true));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<UserProfileResponse>(_json);
        body!.FirstName.Should().Be("Bob");
        body.YearsOfExperience.Should().Be(4);
    }

    [Fact]
    public async Task UpdateMyProfile_SecondCall_Updates()
    {
        await _factory.ResetDatabaseAsync();
        await BrokerFor(_user1Token).UpdateMyProfileAsync(
            new UpdateProfileRequest(Headline: "First"));
        var response = await BrokerFor(_user1Token).UpdateMyProfileAsync(
            new UpdateProfileRequest(Headline: "Second"));

        var body = await response.Content.ReadFromJsonAsync<UserProfileResponse>(_json);
        body!.Headline.Should().Be("Second");
    }

    [Fact]
    public async Task UpdateMyProfile_HeadlineTooLong_Returns400()
    {
        var response = await BrokerFor(_user1Token).UpdateMyProfileAsync(
            new UpdateProfileRequest(Headline: new string('x', 201)));
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UpdateMyProfile_InvalidLinkedInUrl_Returns400()
    {
        var response = await BrokerFor(_user1Token).UpdateMyProfileAsync(
            new UpdateProfileRequest(LinkedInUrl: "not-a-url"));
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UpdateMyProfile_NoAuth_Returns401()
    {
        var response = await AnonBroker().UpdateMyProfileAsync(
            new UpdateProfileRequest(FirstName: "Anon"));
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}

// ════════════════════════════════════════════════════════════════════════════════
// GET /users/{userId} (public profile)
// ════════════════════════════════════════════════════════════════════════════════

public sealed partial class UsersApiTests
{
    [Fact]
    public async Task GetPublicProfile_PublicUser_Returns200()
    {
        await _factory.ResetDatabaseAsync();
        await BrokerFor(_user1Token).UpdateMyProfileAsync(
            new UpdateProfileRequest(Headline: "Public profile", IsPublic: true));

        var response = await AnonBroker().GetPublicProfileAsync(_user1Id);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<UserProfileResponse>(_json);
        body!.Headline.Should().Be("Public profile");
    }

    [Fact]
    public async Task GetPublicProfile_PrivateUser_AnonymousCaller_Returns404()
    {
        await _factory.ResetDatabaseAsync();
        await BrokerFor(_user1Token).UpdateMyProfileAsync(
            new UpdateProfileRequest(IsPublic: false));

        var response = await AnonBroker().GetPublicProfileAsync(_user1Id);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetPublicProfile_PrivateUser_Owner_Returns200()
    {
        await _factory.ResetDatabaseAsync();
        await BrokerFor(_user1Token).UpdateMyProfileAsync(
            new UpdateProfileRequest(IsPublic: false));

        // Owner can see their own private profile
        var response = await BrokerFor(_user1Token).GetPublicProfileAsync(_user1Id);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetPublicProfile_NonExistent_Returns404()
    {
        var response = await AnonBroker().GetPublicProfileAsync(Guid.NewGuid());
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}

// ════════════════════════════════════════════════════════════════════════════════
// POST /users/me/picture
// ════════════════════════════════════════════════════════════════════════════════

public sealed partial class UsersApiTests
{
    [Fact]
    public async Task UploadPicture_ValidJpeg_Returns200WithUrl()
    {
        await _factory.ResetDatabaseAsync();
        var response = await BrokerFor(_user1Token)
            .UploadProfilePictureAsync(_validJpeg, "photo.jpg", "image/jpeg");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<UploadedFileResponse>(_json);
        body!.Url.Should().StartWith("https://");
        body.Url.Should().Contain("profile-pictures");
    }

    [Fact]
    public async Task UploadPicture_WrongContentType_Returns400()
    {
        var response = await BrokerFor(_user1Token)
            .UploadProfilePictureAsync(_validJpeg, "doc.pdf", "application/pdf");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UploadPicture_NoAuth_Returns401()
    {
        var response = await AnonBroker()
            .UploadProfilePictureAsync(_validJpeg, "photo.jpg", "image/jpeg");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task UploadPicture_UrlPersistedOnProfile()
    {
        await _factory.ResetDatabaseAsync();
        await BrokerFor(_user1Token).UpdateMyProfileAsync(new UpdateProfileRequest());
        await BrokerFor(_user1Token)
            .UploadProfilePictureAsync(_validJpeg, "photo.jpg", "image/jpeg");

        var profile = await BrokerFor(_user1Token).GetMyProfileAsync();
        var body    = await profile.Content.ReadFromJsonAsync<UserProfileResponse>(_json);
        body!.ProfilePictureUrl.Should().StartWith("https://");
    }
}

// ════════════════════════════════════════════════════════════════════════════════
// Admin — GET /admin/users
// ════════════════════════════════════════════════════════════════════════════════

public sealed partial class UsersApiTests
{
    [Fact]
    public async Task AdminGetUsers_Returns200()
    {
        await _factory.ResetDatabaseAsync();
        await BrokerFor(_user1Token).UpdateMyProfileAsync(
            new UpdateProfileRequest(FirstName: "Alice"));
        await BrokerFor(_user2Token).UpdateMyProfileAsync(
            new UpdateProfileRequest(FirstName: "Bob"));

        var response = await BrokerFor(_adminToken).AdminGetUsersAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<PagedUserResponse>(_json);
        body!.Total.Should().Be(2);
    }

    [Fact]
    public async Task AdminGetUsers_SearchFilter_ReturnsMatching()
    {
        await _factory.ResetDatabaseAsync();
        await BrokerFor(_user1Token).UpdateMyProfileAsync(
            new UpdateProfileRequest(FirstName: "Alice"));
        await BrokerFor(_user2Token).UpdateMyProfileAsync(
            new UpdateProfileRequest(FirstName: "Bob"));

        var response = await BrokerFor(_adminToken).AdminGetUsersAsync(search: "Ali");
        var body     = await response.Content.ReadFromJsonAsync<PagedUserResponse>(_json);
        body!.Total.Should().Be(1);
        body.Items[0].FirstName.Should().Be("Alice");
    }

    [Fact]
    public async Task AdminGetUsers_IsDeletedFilter_ReturnsDeleted()
    {
        await _factory.ResetDatabaseAsync();
        await BrokerFor(_user1Token).UpdateMyProfileAsync(new UpdateProfileRequest());
        await BrokerFor(_user2Token).UpdateMyProfileAsync(new UpdateProfileRequest());
        await BrokerFor(_adminToken).AdminDeleteUserAsync(_user1Id);

        var response = await BrokerFor(_adminToken).AdminGetUsersAsync(isDeleted: true);
        var body     = await response.Content.ReadFromJsonAsync<PagedUserResponse>(_json);
        body!.Total.Should().Be(1);
        body.Items[0].UserId.Should().Be(_user1Id);
    }

    [Fact]
    public async Task AdminGetUsers_Pagination_Works()
    {
        await _factory.ResetDatabaseAsync();
        // Create 3 users; admin + user1 + user2
        await BrokerFor(_adminToken).UpdateMyProfileAsync(new UpdateProfileRequest());
        await BrokerFor(_user1Token).UpdateMyProfileAsync(new UpdateProfileRequest());
        await BrokerFor(_user2Token).UpdateMyProfileAsync(new UpdateProfileRequest());

        var response = await BrokerFor(_adminToken).AdminGetUsersAsync(page: 1, pageSize: 2);
        var body     = await response.Content.ReadFromJsonAsync<PagedUserResponse>(_json);
        body!.Items.Should().HaveCount(2);
        body.HasNextPage.Should().BeTrue();
    }

    [Fact]
    public async Task AdminGetUsers_CandidateRole_Returns403()
    {
        var response = await BrokerFor(_user1Token).AdminGetUsersAsync();
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AdminGetUsers_NoAuth_Returns401()
    {
        var response = await AnonBroker().AdminGetUsersAsync();
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}

// ════════════════════════════════════════════════════════════════════════════════
// Admin — GET /admin/users/{id}
// ════════════════════════════════════════════════════════════════════════════════

public sealed partial class UsersApiTests
{
    [Fact]
    public async Task AdminGetUser_ExistingUser_Returns200()
    {
        await _factory.ResetDatabaseAsync();
        await BrokerFor(_user1Token).UpdateMyProfileAsync(
            new UpdateProfileRequest(FirstName: "Alice"));

        var response = await BrokerFor(_adminToken).AdminGetUserAsync(_user1Id);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<AdminUserResponse>(_json);
        body!.UserId.Should().Be(_user1Id);
    }

    [Fact]
    public async Task AdminGetUser_NonExistent_Returns404()
    {
        var response = await BrokerFor(_adminToken).AdminGetUserAsync(Guid.NewGuid());
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}

// ════════════════════════════════════════════════════════════════════════════════
// Admin — DELETE /admin/users/{id} (soft-delete)
// ════════════════════════════════════════════════════════════════════════════════

public sealed partial class UsersApiTests
{
    [Fact]
    public async Task AdminDeleteUser_ActiveUser_Returns204()
    {
        await _factory.ResetDatabaseAsync();
        await BrokerFor(_user1Token).UpdateMyProfileAsync(new UpdateProfileRequest());

        var response = await BrokerFor(_adminToken).AdminDeleteUserAsync(_user1Id);
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task AdminDeleteUser_UserAppearsInDeletedFilter()
    {
        await _factory.ResetDatabaseAsync();
        await BrokerFor(_user1Token).UpdateMyProfileAsync(new UpdateProfileRequest());
        await BrokerFor(_adminToken).AdminDeleteUserAsync(_user1Id);

        var body = await (await BrokerFor(_adminToken).AdminGetUserAsync(_user1Id))
            .Content.ReadFromJsonAsync<AdminUserResponse>(_json);
        body!.IsDeleted.Should().BeTrue();
        body.DeletedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task AdminDeleteUser_AlreadyDeleted_Returns409()
    {
        await _factory.ResetDatabaseAsync();
        await BrokerFor(_user1Token).UpdateMyProfileAsync(new UpdateProfileRequest());
        await BrokerFor(_adminToken).AdminDeleteUserAsync(_user1Id);

        var response = await BrokerFor(_adminToken).AdminDeleteUserAsync(_user1Id);
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task AdminDeleteUser_NonExistent_Returns404()
    {
        var response = await BrokerFor(_adminToken).AdminDeleteUserAsync(Guid.NewGuid());
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task AdminDeleteUser_CandidateRole_Returns403()
    {
        var response = await BrokerFor(_user1Token).AdminDeleteUserAsync(_user2Id);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}

// ════════════════════════════════════════════════════════════════════════════════
// Admin — POST /admin/users/{id}/reinstate
// ════════════════════════════════════════════════════════════════════════════════

public sealed partial class UsersApiTests
{
    [Fact]
    public async Task AdminReinstateUser_DeletedUser_Returns204()
    {
        await _factory.ResetDatabaseAsync();
        await BrokerFor(_user1Token).UpdateMyProfileAsync(new UpdateProfileRequest());
        await BrokerFor(_adminToken).AdminDeleteUserAsync(_user1Id);

        var response = await BrokerFor(_adminToken).AdminReinstateUserAsync(_user1Id);
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task AdminReinstateUser_UserNoLongerDeleted()
    {
        await _factory.ResetDatabaseAsync();
        await BrokerFor(_user1Token).UpdateMyProfileAsync(new UpdateProfileRequest());
        await BrokerFor(_adminToken).AdminDeleteUserAsync(_user1Id);
        await BrokerFor(_adminToken).AdminReinstateUserAsync(_user1Id);

        var body = await (await BrokerFor(_adminToken).AdminGetUserAsync(_user1Id))
            .Content.ReadFromJsonAsync<AdminUserResponse>(_json);
        body!.IsDeleted.Should().BeFalse();
        body.DeletedAt.Should().BeNull();
    }

    [Fact]
    public async Task AdminReinstateUser_NotDeleted_Returns409()
    {
        await _factory.ResetDatabaseAsync();
        await BrokerFor(_user1Token).UpdateMyProfileAsync(new UpdateProfileRequest());

        var response = await BrokerFor(_adminToken).AdminReinstateUserAsync(_user1Id);
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task AdminReinstateUser_NonExistent_Returns404()
    {
        var response = await BrokerFor(_adminToken).AdminReinstateUserAsync(Guid.NewGuid());
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}

// ════════════════════════════════════════════════════════════════════════════════
// Health
// ════════════════════════════════════════════════════════════════════════════════

public sealed partial class UsersApiTests
{
    [Fact]
    public async Task Health_Returns200()
    {
        var response = await _factory.CreateClient().GetAsync("/health");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
