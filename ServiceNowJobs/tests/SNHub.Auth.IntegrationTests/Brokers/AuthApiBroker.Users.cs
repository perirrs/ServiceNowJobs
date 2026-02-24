using System.Net.Http.Headers;
using System.Net.Http.Json;
using SNHub.Auth.IntegrationTests.Models;

namespace SNHub.Auth.IntegrationTests.Brokers;

public sealed partial class AuthApiBroker
{
    private const string UsersBase = "api/v1/users";
    private const string AdminBase = "api/v1/admin/users";

    // ── GET /me ───────────────────────────────────────────────────────────────

    public async Task<(HttpResponseMessage Raw, ApiResponse<UserProfile>? Body)> GetMeAsync()
    {
        var response = await _client.GetAsync($"{UsersBase}/me");
        var body = await ReadApiResponseAsync<UserProfile>(response);
        return (response, body);
    }

    // ── PUT /me ───────────────────────────────────────────────────────────────

    public async Task<(HttpResponseMessage Raw, ApiResponse<UserProfile>? Body)>
        UpdateMeAsync(UpdateProfileRequest request)
    {
        var response = await _client.PutAsJsonAsync($"{UsersBase}/me", request, JsonOptions);
        var body = await ReadApiResponseAsync<UserProfile>(response);
        return (response, body);
    }

    // ── POST /me/profile-picture ──────────────────────────────────────────────

    public async Task<(HttpResponseMessage Raw, ApiResponse<ProfilePictureResponse>? Body)>
        UploadProfilePictureAsync(byte[] fileBytes, string fileName, string contentType)
    {
        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(fileBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        content.Add(fileContent, "file", fileName);

        var response = await _client.PostAsync($"{UsersBase}/me/profile-picture", content);
        var body = await ReadApiResponseAsync<ProfilePictureResponse>(response);
        return (response, body);
    }

    // ── Admin: GET /admin/users ───────────────────────────────────────────────

    public async Task<(HttpResponseMessage Raw, ApiResponse<PagedResult<UserSummary>>? Body)>
        GetUsersAsync(int page = 1, int pageSize = 20, string? search = null, bool? isActive = null)
    {
        var url = $"{AdminBase}?page={page}&pageSize={pageSize}";
        if (search is not null) url += $"&search={Uri.EscapeDataString(search)}";
        if (isActive.HasValue) url += $"&isActive={isActive.Value}";

        var response = await _client.GetAsync(url);
        var body = await ReadApiResponseAsync<PagedResult<UserSummary>>(response);
        return (response, body);
    }

    // ── Admin: GET /admin/users/{id} ──────────────────────────────────────────

    public async Task<(HttpResponseMessage Raw, ApiResponse<UserAdmin>? Body)>
        GetUserByIdAsync(Guid id)
    {
        var response = await _client.GetAsync($"{AdminBase}/{id}");
        var body = await ReadApiResponseAsync<UserAdmin>(response);
        return (response, body);
    }

    // ── Admin: PUT /admin/users/{id}/suspend ──────────────────────────────────

    public async Task<HttpResponseMessage> SuspendUserAsync(Guid id, SuspendUserRequest request) =>
        await _client.PutAsJsonAsync($"{AdminBase}/{id}/suspend", request, JsonOptions);

    // ── Admin: PUT /admin/users/{id}/reinstate ────────────────────────────────

    public async Task<HttpResponseMessage> ReinstateUserAsync(Guid id) =>
        await _client.PutAsJsonAsync($"{AdminBase}/{id}/reinstate", new { }, JsonOptions);

    // ── Admin: PUT /admin/users/{id}/roles ────────────────────────────────────

    public async Task<HttpResponseMessage> UpdateUserRolesAsync(Guid id, UpdateUserRolesRequest request) =>
        await _client.PutAsJsonAsync($"{AdminBase}/{id}/roles", request, JsonOptions);
}

public sealed record ProfilePictureResponse(string ProfilePictureUrl);
