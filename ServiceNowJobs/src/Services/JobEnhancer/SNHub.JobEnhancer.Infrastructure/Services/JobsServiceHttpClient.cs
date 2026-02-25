using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SNHub.JobEnhancer.Application.Interfaces;

namespace SNHub.JobEnhancer.Infrastructure.Services;

public sealed class JobsServiceHttpClient : IJobsServiceClient
{
    private readonly HttpClient _http;
    private readonly ILogger<JobsServiceHttpClient> _logger;

    private static readonly JsonSerializerOptions _opts =
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public JobsServiceHttpClient(HttpClient http, ILogger<JobsServiceHttpClient> logger)
    { _http = http; _logger = logger; }

    public async Task ApplyEnhancementAsync(
        Guid jobId, string? enhancedTitle, string? enhancedDescription,
        string? enhancedRequirements, string[] suggestedSkills, CancellationToken ct = default)
    {
        var payload = new
        {
            enhancedTitle,
            enhancedDescription,
            enhancedRequirements,
            suggestedSkills
        };

        var content = new StringContent(
            JsonSerializer.Serialize(payload, _opts), Encoding.UTF8, "application/json");

        var response = await _http.PatchAsync(
            $"/api/v1/jobs/{jobId}/apply-enhancement", content, ct);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            _logger.LogError(
                "Jobs service returned {Status} for job {JobId}: {Body}",
                response.StatusCode, jobId, body);
        }
    }
}

/// <summary>No-op implementation for tests where Jobs service isn't running.</summary>
public sealed class StubJobsServiceClient : IJobsServiceClient
{
    public List<(Guid JobId, string? Title, string? Description, string? Requirements, string[] Skills)>
        Calls { get; } = [];

    public Task ApplyEnhancementAsync(
        Guid jobId, string? enhancedTitle, string? enhancedDescription,
        string? enhancedRequirements, string[] suggestedSkills, CancellationToken ct = default)
    {
        Calls.Add((jobId, enhancedTitle, enhancedDescription, enhancedRequirements, suggestedSkills));
        return Task.CompletedTask;
    }
}
