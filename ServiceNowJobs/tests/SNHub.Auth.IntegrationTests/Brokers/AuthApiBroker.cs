using Xunit;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using SNHub.Auth.IntegrationTests.Models;

namespace SNHub.Auth.IntegrationTests.Brokers;

/// <summary>
/// Typed HTTP client for the SNHub Auth API.
/// Each partial class file adds methods for one endpoint group.
/// </summary>
public sealed partial class AuthApiBroker
{
    private readonly HttpClient _client;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public AuthApiBroker(AuthWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    public void SetBearerToken(string token) =>
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

    public void ClearBearerToken() =>
        _client.DefaultRequestHeaders.Authorization = null;

    private async Task<HttpResponseMessage> PostAsync<TBody>(string url, TBody body) =>
        await _client.PostAsJsonAsync(url, body, JsonOptions);

    private async Task<TResult?> DeserializeAsync<TResult>(HttpResponseMessage response)
    {
        var json = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<TResult>(json, JsonOptions);
    }

    private async Task<ApiResponse<TData>?> ReadApiResponseAsync<TData>(HttpResponseMessage response)
    {
        var json = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<ApiResponse<TData>>(json, JsonOptions);
    }

    private async Task<ApiErrorResponse?> ReadApiErrorAsync(HttpResponseMessage response)
    {
        var json = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<ApiErrorResponse>(json, JsonOptions);
    }
}
