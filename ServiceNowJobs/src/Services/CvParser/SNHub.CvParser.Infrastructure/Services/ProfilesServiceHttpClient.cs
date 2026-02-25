using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SNHub.CvParser.Application.Commands.ApplyParsedCv;

namespace SNHub.CvParser.Infrastructure.Services;

public sealed class ProfilesServiceHttpClient : IProfilesServiceClient
{
    private readonly HttpClient _http;
    private readonly ILogger<ProfilesServiceHttpClient> _logger;

    private static readonly JsonSerializerOptions _opts =
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public ProfilesServiceHttpClient(HttpClient http, ILogger<ProfilesServiceHttpClient> logger)
    { _http = http; _logger = logger; }

    public async Task ApplyParsedDataAsync(
        Guid userId, ProfilePatch patch, CancellationToken ct = default)
    {
        var payload = JsonSerializer.Serialize(patch, _opts);
        var content = new StringContent(payload, Encoding.UTF8, "application/json");

        var response = await _http.PatchAsync(
            $"/api/v1/profiles/{userId}/apply-cv-data", content, ct);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            _logger.LogError(
                "Profiles service returned {Status} for user {UserId}: {Body}",
                response.StatusCode, userId, body);
            response.EnsureSuccessStatusCode(); // throws HttpRequestException
        }

        _logger.LogInformation("CV data applied to profile for user {UserId}", userId);
    }
}

/// <summary>No-op implementation for integration tests where Profiles service isn't running.</summary>
public sealed class StubProfilesServiceClient : IProfilesServiceClient
{
    public Task ApplyParsedDataAsync(Guid userId, ProfilePatch patch, CancellationToken ct = default)
        => Task.CompletedTask;
}
