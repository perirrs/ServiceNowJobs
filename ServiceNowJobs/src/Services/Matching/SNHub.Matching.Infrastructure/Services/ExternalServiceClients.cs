using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using SNHub.Matching.Application.Interfaces;

namespace SNHub.Matching.Infrastructure.Services;

// ── HTTP client implementations ───────────────────────────────────────────────

public sealed class JobsServiceHttpClient : IJobsServiceClient
{
    private readonly HttpClient _http;
    private readonly ILogger<JobsServiceHttpClient> _logger;

    public JobsServiceHttpClient(HttpClient http, ILogger<JobsServiceHttpClient> logger)
    { _http = http; _logger = logger; }

    public async Task<JobData?> GetJobAsync(Guid jobId, CancellationToken ct = default)
    {
        try
        {
            return await _http.GetFromJsonAsync<JobData>(
                $"/api/v1/jobs/{jobId}/internal", ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch job {JobId} from Jobs service", jobId);
            return null;
        }
    }
}

public sealed class ProfilesServiceHttpClient : IProfilesServiceClient
{
    private readonly HttpClient _http;
    private readonly ILogger<ProfilesServiceHttpClient> _logger;

    public ProfilesServiceHttpClient(HttpClient http, ILogger<ProfilesServiceHttpClient> logger)
    { _http = http; _logger = logger; }

    public async Task<CandidateData?> GetCandidateAsync(Guid userId, CancellationToken ct = default)
    {
        try
        {
            return await _http.GetFromJsonAsync<CandidateData>(
                $"/api/v1/profiles/candidates/{userId}/internal", ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch candidate {UserId} from Profiles service", userId);
            return null;
        }
    }

    public async Task<IEnumerable<CandidateData>> GetPublicCandidatesAsync(
        CancellationToken ct = default)
    {
        try
        {
            return await _http.GetFromJsonAsync<IEnumerable<CandidateData>>(
                "/api/v1/profiles/candidates/internal", ct) ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch candidates from Profiles service");
            return [];
        }
    }
}

// ── Stub implementations for tests ───────────────────────────────────────────

public sealed class StubJobsServiceClient : IJobsServiceClient
{
    private readonly Dictionary<Guid, JobData> _store = new();

    public void Seed(JobData job) => _store[job.Id] = job;

    public Task<JobData?> GetJobAsync(Guid jobId, CancellationToken ct = default)
        => Task.FromResult(_store.TryGetValue(jobId, out var j) ? j : null);
}

public sealed class StubProfilesServiceClient : IProfilesServiceClient
{
    private readonly Dictionary<Guid, CandidateData> _store = new();

    public void Seed(CandidateData candidate) => _store[candidate.UserId] = candidate;

    public Task<CandidateData?> GetCandidateAsync(Guid userId, CancellationToken ct = default)
        => Task.FromResult(_store.TryGetValue(userId, out var c) ? c : null);

    public Task<IEnumerable<CandidateData>> GetPublicCandidatesAsync(CancellationToken ct = default)
        => Task.FromResult<IEnumerable<CandidateData>>([.. _store.Values]);
}
