using System.Net.Http.Json;
using System.Text.Json;
using SNHub.Jobs.IntegrationTests.Models;

namespace SNHub.Jobs.IntegrationTests.Brokers;

public sealed class JobsApiBroker
{
    private readonly HttpClient _client;
    private const string Base = "/api/v1/jobs";

    public JobsApiBroker(HttpClient client) => _client = client;

    // ── Search / Read ─────────────────────────────────────────────────────────

    public async Task<HttpResponseMessage> SearchJobsAsync(
        string? keyword = null, string? country = null,
        string? jobType = null, string? workMode = null,
        decimal? salaryMin = null, decimal? salaryMax = null,
        int page = 1, int pageSize = 20)
    {
        var qs = BuildQuery(
            ("keyword",  keyword),
            ("country",  country),
            ("jobType",  jobType),
            ("workMode", workMode),
            ("salaryMin", salaryMin?.ToString()),
            ("salaryMax", salaryMax?.ToString()),
            ("page",     page.ToString()),
            ("pageSize", pageSize.ToString()));

        return await _client.GetAsync($"{Base}{qs}");
    }

    public Task<HttpResponseMessage> GetJobAsync(Guid id) =>
        _client.GetAsync($"{Base}/{id}");

    public Task<HttpResponseMessage> GetMyJobsAsync(string? status = null, int page = 1) =>
        _client.GetAsync($"{Base}/mine{BuildQuery(("status", status), ("page", page.ToString()))}");

    // ── Commands ──────────────────────────────────────────────────────────────

    public Task<HttpResponseMessage> CreateJobAsync(CreateJobRequest request) =>
        _client.PostAsJsonAsync(Base, request);

    public Task<HttpResponseMessage> UpdateJobAsync(Guid id, UpdateJobRequest request) =>
        _client.PutAsJsonAsync($"{Base}/{id}", request);

    public Task<HttpResponseMessage> PublishJobAsync(Guid id) =>
        _client.PostAsync($"{Base}/{id}/publish", null);

    public Task<HttpResponseMessage> PauseJobAsync(Guid id) =>
        _client.PostAsync($"{Base}/{id}/pause", null);

    public Task<HttpResponseMessage> CloseJobAsync(Guid id) =>
        _client.DeleteAsync($"{Base}/{id}");

    // ── Helpers ───────────────────────────────────────────────────────────────

    public async Task<(HttpResponseMessage Response, T? Body)> DeserializeAsync<T>(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode) return (response, default);
        var body = await response.Content.ReadFromJsonAsync<T>(
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        return (response, body);
    }

    private static string BuildQuery(params (string Key, string? Value)[] pairs)
    {
        var parts = pairs
            .Where(p => p.Value is not null)
            .Select(p => $"{p.Key}={Uri.EscapeDataString(p.Value!)}");
        var qs = string.Join("&", parts);
        return qs.Length > 0 ? "?" + qs : string.Empty;
    }
}
