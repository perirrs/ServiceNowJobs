using SNHub.Users.IntegrationTests.Models;
using System.Net.Http.Json;
using Xunit;

namespace SNHub.Users.IntegrationTests.Brokers;

[CollectionDefinition(nameof(UsersApiCollection))]
public sealed class UsersApiCollection
    : ICollectionFixture<UsersWebApplicationFactory> { }

public sealed class UsersApiBroker
{
    private readonly HttpClient _client;
    private const string ProfileBase = "/api/v1/users";
    private const string AdminBase   = "/api/v1/admin/users";

    public UsersApiBroker(HttpClient client) => _client = client;

    // ── Self profile ──────────────────────────────────────────────────────────
    public Task<HttpResponseMessage> GetMyProfileAsync()
        => _client.GetAsync($"{ProfileBase}/me");

    public Task<HttpResponseMessage> UpdateMyProfileAsync(UpdateProfileRequest req)
        => _client.PutAsJsonAsync($"{ProfileBase}/me", req);

    public Task<HttpResponseMessage> GetPublicProfileAsync(Guid userId)
        => _client.GetAsync($"{ProfileBase}/{userId}");

    public Task<HttpResponseMessage> UploadProfilePictureAsync(
        byte[] bytes, string fileName, string contentType)
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        form.Add(file, "file", fileName);
        return _client.PostAsync($"{ProfileBase}/me/picture", form);
    }

    // ── Admin endpoints ───────────────────────────────────────────────────────
    public Task<HttpResponseMessage> AdminGetUsersAsync(
        string? search = null, bool? isDeleted = null, int page = 1, int pageSize = 20)
    {
        var qs = new List<string> { $"page={page}", $"pageSize={pageSize}" };
        if (search    != null) qs.Add($"search={Uri.EscapeDataString(search)}");
        if (isDeleted != null) qs.Add($"isDeleted={isDeleted}");
        return _client.GetAsync($"{AdminBase}?{string.Join("&", qs)}");
    }

    public Task<HttpResponseMessage> AdminGetUserAsync(Guid userId)
        => _client.GetAsync($"{AdminBase}/{userId}");

    public Task<HttpResponseMessage> AdminDeleteUserAsync(Guid userId)
        => _client.DeleteAsync($"{AdminBase}/{userId}");

    public Task<HttpResponseMessage> AdminReinstateUserAsync(Guid userId)
        => _client.PostAsync($"{AdminBase}/{userId}/reinstate", null);
}
