using System.Net.Http.Json;
using SNHub.Applications.IntegrationTests.Models;

namespace SNHub.Applications.IntegrationTests.Brokers;

public sealed class ApplicationsApiBroker
{
    private readonly HttpClient _client;
    private const string Base = "/api/v1/applications";

    public ApplicationsApiBroker(HttpClient client) => _client = client;

    public Task<HttpResponseMessage> ApplyAsync(Guid jobId, ApplyRequest request) =>
        _client.PostAsJsonAsync($"{Base}/jobs/{jobId}", request);

    public Task<HttpResponseMessage> GetByIdAsync(Guid id) =>
        _client.GetAsync($"{Base}/{id}");

    public Task<HttpResponseMessage> GetMineAsync(int page = 1, int pageSize = 20) =>
        _client.GetAsync($"{Base}/mine?page={page}&pageSize={pageSize}");

    public Task<HttpResponseMessage> GetForJobAsync(Guid jobId, string? status = null) =>
        _client.GetAsync($"{Base}/jobs/{jobId}" + (status is not null ? $"?status={status}" : ""));

    public Task<HttpResponseMessage> UpdateStatusAsync(Guid id, UpdateStatusRequest request) =>
        _client.PutAsJsonAsync($"{Base}/{id}/status", request);

    public Task<HttpResponseMessage> WithdrawAsync(Guid id) =>
        _client.DeleteAsync($"{Base}/{id}");
}
