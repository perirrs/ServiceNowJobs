using SNHub.Profiles.IntegrationTests.Models;
using System.Net.Http.Json;

namespace SNHub.Profiles.IntegrationTests.Brokers;

public sealed class ProfilesApiBroker
{
    private readonly HttpClient _client;
    private const string Base = "/api/v1/profiles";

    public ProfilesApiBroker(HttpClient client) => _client = client;

    // Candidate
    public Task<HttpResponseMessage> GetMyCandidateProfileAsync()          => _client.GetAsync($"{Base}/candidate/me");
    public Task<HttpResponseMessage> UpsertCandidateProfileAsync(UpsertCandidateRequest r) => _client.PutAsJsonAsync($"{Base}/candidate/me", r);
    public Task<HttpResponseMessage> GetCandidateProfileAsync(Guid userId) => _client.GetAsync($"{Base}/candidate/{userId}");
    public Task<HttpResponseMessage> SearchCandidatesAsync(string? keyword = null, string? country = null, bool? remote = null, int page = 1)
    {
        var qs = new List<string>();
        if (keyword is not null) qs.Add($"keyword={keyword}");
        if (country is not null) qs.Add($"country={country}");
        if (remote.HasValue)    qs.Add($"openToRemote={remote}");
        qs.Add($"page={page}");
        return _client.GetAsync($"{Base}/candidates/search?{string.Join("&", qs)}");
    }

    // Employer
    public Task<HttpResponseMessage> GetMyEmployerProfileAsync()           => _client.GetAsync($"{Base}/employer/me");
    public Task<HttpResponseMessage> UpsertEmployerProfileAsync(UpsertEmployerRequest r) => _client.PutAsJsonAsync($"{Base}/employer/me", r);
    public Task<HttpResponseMessage> GetEmployerProfileAsync(Guid userId)  => _client.GetAsync($"{Base}/employer/{userId}");

    // File uploads
    public Task<HttpResponseMessage> UploadProfilePictureAsync(byte[] bytes, string fileName, string contentType)
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        form.Add(file, "file", fileName);
        return _client.PostAsync($"{Base}/candidate/me/picture", form);
    }

    public Task<HttpResponseMessage> UploadCvAsync(byte[] bytes, string fileName, string contentType)
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        form.Add(file, "file", fileName);
        return _client.PostAsync($"{Base}/candidate/me/cv", form);
    }
}
