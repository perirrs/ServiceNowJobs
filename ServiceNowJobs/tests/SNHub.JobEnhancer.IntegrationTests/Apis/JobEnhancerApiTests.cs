using FluentAssertions;
using SNHub.JobEnhancer.IntegrationTests.Brokers;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Xunit;

namespace SNHub.JobEnhancer.IntegrationTests.Apis;

// ── Shared helpers ────────────────────────────────────────────────────────────

file static class Json
{
    public static readonly JsonSerializerOptions Opts =
        new() { PropertyNameCaseInsensitive = true };
}

file sealed class EnhancementResponse
{
    public Guid     Id               { get; set; }
    public Guid     JobId            { get; set; }
    public string   Status           { get; set; } = string.Empty;
    public string   OriginalTitle    { get; set; } = string.Empty;
    public string?  EnhancedTitle    { get; set; }
    public int      ScoreBefore      { get; set; }
    public int      ScoreAfter       { get; set; }
    public int      ScoreImprovement { get; set; }
    public BiasIssueResponse[]    BiasIssues      { get; set; } = [];
    public string[]               MissingFields   { get; set; } = [];
    public ImprovementResponse[]  Improvements    { get; set; } = [];
    public string[]               SuggestedSkills { get; set; } = [];
    public bool     IsAccepted       { get; set; }
}

file sealed class BiasIssueResponse
{
    public string Text       { get; set; } = string.Empty;
    public string Severity   { get; set; } = string.Empty;
    public string Suggestion { get; set; } = string.Empty;
}

file sealed class ImprovementResponse
{
    public string Category    { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

file sealed class AcceptResponse
{
    public Guid   EnhancementId { get; set; }
    public bool   Accepted      { get; set; }
    public string Message       { get; set; } = string.Empty;
}

file static class Payloads
{
    public static StringContent CleanJob(Guid jobId) => Json(new
    {
        jobId,
        title       = "Senior ServiceNow Developer",
        description = "We are looking for an experienced ServiceNow developer to join our team. " +
                      "You will build ITSM and HRSD solutions on the Now Platform. " +
                      "We offer competitive salary, remote working, and excellent benefits.",
        requirements = "5+ years ServiceNow. CSA required. Flow Designer experience a plus."
    });

    public static StringContent BiasedJob(Guid jobId) => Json(new
    {
        jobId,
        title       = "ServiceNow Rockstar Ninja",
        description = "We need a rockstar ninja developer who is a native English speaker. " +
                      "Join our young, energetic team. He/she must be passionate about coding " +
                      "and willing to work in a fast-paced environment. Must be a digital native.",
        requirements = (string?)null
    });

    private static StringContent Json(object payload) =>
        new(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
}

// ══════════════════════════════════════════════════════════════════════════════
// POST /api/v1/enhance  — Enhance a description
// ══════════════════════════════════════════════════════════════════════════════

[Collection(nameof(JobEnhancerApiCollection))]
public sealed class EnhanceDescriptionTests
{
    private readonly JobEnhancerWebApplicationFactory _factory;
    private static readonly Guid   _userId = Guid.NewGuid();
    private static readonly string _token  =
        JobEnhancerWebApplicationFactory.GenerateToken(_userId);

    public EnhanceDescriptionTests(JobEnhancerWebApplicationFactory factory)
        => _factory = factory;

    private HttpClient AuthClient()
    {
        var c = _factory.CreateClient();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        return c;
    }

    [Fact]
    public async Task Enhance_CleanDescription_ReturnsCompleted()
    {
        await _factory.ResetDatabaseAsync();
        var jobId    = Guid.NewGuid();
        var response = await AuthClient().PostAsync(
            "/api/v1/enhance", Payloads.CleanJob(jobId));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<EnhancementResponse>(Json.Opts);
        body!.Status.Should().Be("Completed");
        body.JobId.Should().Be(jobId);
        body.EnhancedTitle.Should().NotBeNullOrEmpty();
        body.ScoreAfter.Should().BeGreaterThan(0);
        body.IsAccepted.Should().BeFalse();
    }

    [Fact]
    public async Task Enhance_BiasedDescription_ReturnsBiasIssues()
    {
        await _factory.ResetDatabaseAsync();
        var response = await AuthClient().PostAsync(
            "/api/v1/enhance", Payloads.BiasedJob(Guid.NewGuid()));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<EnhancementResponse>(Json.Opts);
        body!.Status.Should().Be("Completed");
        body.BiasIssues.Should().NotBeEmpty();
        body.BiasIssues.Should().Contain(b => b.Severity == "High");
    }

    [Fact]
    public async Task Enhance_MissingFields_ReturnsMissingFieldsList()
    {
        await _factory.ResetDatabaseAsync();
        var response = await AuthClient().PostAsync(
            "/api/v1/enhance", Payloads.BiasedJob(Guid.NewGuid()));

        var body = await response.Content.ReadFromJsonAsync<EnhancementResponse>(Json.Opts);
        body!.MissingFields.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Enhance_ReturnsSuggestedSkills()
    {
        await _factory.ResetDatabaseAsync();
        var response = await AuthClient().PostAsync(
            "/api/v1/enhance", Payloads.CleanJob(Guid.NewGuid()));

        var body = await response.Content.ReadFromJsonAsync<EnhancementResponse>(Json.Opts);
        body!.SuggestedSkills.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Enhance_ReturnsImprovements()
    {
        await _factory.ResetDatabaseAsync();
        var response = await AuthClient().PostAsync(
            "/api/v1/enhance", Payloads.CleanJob(Guid.NewGuid()));

        var body = await response.Content.ReadFromJsonAsync<EnhancementResponse>(Json.Opts);
        body!.Improvements.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Enhance_PersistedInDatabase_CanBeRetrieved()
    {
        await _factory.ResetDatabaseAsync();
        var jobId    = Guid.NewGuid();
        var enhanced = await AuthClient().PostAsync(
            "/api/v1/enhance", Payloads.CleanJob(jobId));
        var created = await enhanced.Content.ReadFromJsonAsync<EnhancementResponse>(Json.Opts);

        var fetched = await AuthClient().GetAsync($"/api/v1/enhance/{created!.Id}");
        fetched.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await fetched.Content.ReadFromJsonAsync<EnhancementResponse>(Json.Opts);
        body!.Id.Should().Be(created.Id);
        body.JobId.Should().Be(jobId);
    }

    [Fact]
    public async Task Enhance_ShortDescription_Returns400()
    {
        var payload = new StringContent(JsonSerializer.Serialize(new
        {
            jobId = Guid.NewGuid(), title = "Dev", description = "Too short", requirements = (string?)null
        }), Encoding.UTF8, "application/json");

        var response = await AuthClient().PostAsync("/api/v1/enhance", payload);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Enhance_NoAuth_Returns401()
    {
        var response = await _factory.CreateClient().PostAsync(
            "/api/v1/enhance", Payloads.CleanJob(Guid.NewGuid()));
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}

// ══════════════════════════════════════════════════════════════════════════════
// POST /api/v1/enhance/{id}/accept
// ══════════════════════════════════════════════════════════════════════════════

[Collection(nameof(JobEnhancerApiCollection))]
public sealed class AcceptEnhancementTests
{
    private readonly JobEnhancerWebApplicationFactory _factory;
    private static readonly Guid   _userId = Guid.NewGuid();
    private static readonly string _token  =
        JobEnhancerWebApplicationFactory.GenerateToken(_userId);

    public AcceptEnhancementTests(JobEnhancerWebApplicationFactory factory)
        => _factory = factory;

    private HttpClient AuthClient()
    {
        var c = _factory.CreateClient();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        return c;
    }

    [Fact]
    public async Task Accept_ValidEnhancement_Returns200()
    {
        await _factory.ResetDatabaseAsync();
        var created = (await (await AuthClient().PostAsync(
            "/api/v1/enhance", Payloads.CleanJob(Guid.NewGuid())))
            .Content.ReadFromJsonAsync<EnhancementResponse>(Json.Opts))!;
        var response = await AuthClient().PostAsync(
            $"/api/v1/enhance/{created.Id}/accept", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<AcceptResponse>(Json.Opts);
        body!.Accepted.Should().BeTrue();
        body.Message.Should().Contain("accepted");
    }

    [Fact]
    public async Task Accept_SetsIsAcceptedFlag()
    {
        await _factory.ResetDatabaseAsync();
        var created = (await (await AuthClient().PostAsync(
            "/api/v1/enhance", Payloads.CleanJob(Guid.NewGuid())))
            .Content.ReadFromJsonAsync<EnhancementResponse>(Json.Opts))!;
        await AuthClient().PostAsync($"/api/v1/enhance/{created.Id}/accept", null);

        var fetched = await AuthClient().GetAsync($"/api/v1/enhance/{created.Id}");
        var body    = await fetched.Content.ReadFromJsonAsync<EnhancementResponse>(Json.Opts);
        body!.IsAccepted.Should().BeTrue();
    }

    [Fact]
    public async Task Accept_NotifiesJobsService()
    {
        await _factory.ResetDatabaseAsync();
        var jobId   = Guid.NewGuid();
        var created = await (await AuthClient().PostAsync(
            "/api/v1/enhance",
            new StringContent(JsonSerializer.Serialize(new
            {
                jobId,
                title       = "ServiceNow Developer",
                description = "Experienced ServiceNow developer needed to build ITSM workflows. " +
                              "We offer great benefits and remote working options.",
                requirements = (string?)null
            }), Encoding.UTF8, "application/json"))).Content
            .ReadFromJsonAsync<EnhancementResponse>(Json.Opts);

        await AuthClient().PostAsync($"/api/v1/enhance/{created!.Id}/accept", null);

        _factory.JobsClient.Calls.Should().Contain(c => c.JobId == jobId);
    }

    [Fact]
    public async Task Accept_Twice_Returns409()
    {
        await _factory.ResetDatabaseAsync();
        var created = (await (await AuthClient().PostAsync(
            "/api/v1/enhance", Payloads.CleanJob(Guid.NewGuid())))
            .Content.ReadFromJsonAsync<EnhancementResponse>(Json.Opts))!;
        await AuthClient().PostAsync($"/api/v1/enhance/{created.Id}/accept", null);
        var second = await AuthClient().PostAsync($"/api/v1/enhance/{created.Id}/accept", null);
        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Accept_OtherUser_Returns403()
    {
        await _factory.ResetDatabaseAsync();
        var created = (await (await AuthClient().PostAsync(
            "/api/v1/enhance", Payloads.CleanJob(Guid.NewGuid())))
            .Content.ReadFromJsonAsync<EnhancementResponse>(Json.Opts))!;
        var otherToken = JobEnhancerWebApplicationFactory.GenerateToken(Guid.NewGuid());
        var client     = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", otherToken);

        var response = await client.PostAsync($"/api/v1/enhance/{created.Id}/accept", null);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Accept_NotFound_Returns404()
    {
        var response = await AuthClient().PostAsync(
            $"/api/v1/enhance/{Guid.NewGuid()}/accept", null);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Accept_NoAuth_Returns401()
    {
        var response = await _factory.CreateClient().PostAsync(
            $"/api/v1/enhance/{Guid.NewGuid()}/accept", null);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}

// ══════════════════════════════════════════════════════════════════════════════
// GET /api/v1/enhance/{id}
// ══════════════════════════════════════════════════════════════════════════════

[Collection(nameof(JobEnhancerApiCollection))]
public sealed class GetEnhancementTests
{
    private readonly JobEnhancerWebApplicationFactory _factory;
    private static readonly Guid   _userId = Guid.NewGuid();
    private static readonly string _token  =
        JobEnhancerWebApplicationFactory.GenerateToken(_userId);

    public GetEnhancementTests(JobEnhancerWebApplicationFactory factory) => _factory = factory;

    private HttpClient AuthClient()
    {
        var c = _factory.CreateClient();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        return c;
    }

    [Fact]
    public async Task GetById_OwnResult_Returns200()
    {
        await _factory.ResetDatabaseAsync();
        var created  = await (await AuthClient().PostAsync(
            "/api/v1/enhance", Payloads.CleanJob(Guid.NewGuid())))
            .Content.ReadFromJsonAsync<EnhancementResponse>(Json.Opts);

        var response = await AuthClient().GetAsync($"/api/v1/enhance/{created!.Id}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetById_OtherUser_Returns403()
    {
        await _factory.ResetDatabaseAsync();
        var created    = await (await AuthClient().PostAsync(
            "/api/v1/enhance", Payloads.CleanJob(Guid.NewGuid())))
            .Content.ReadFromJsonAsync<EnhancementResponse>(Json.Opts);

        var otherToken = JobEnhancerWebApplicationFactory.GenerateToken(Guid.NewGuid());
        var client     = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", otherToken);

        var response = await client.GetAsync($"/api/v1/enhance/{created!.Id}");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetById_NotFound_Returns404()
    {
        var response = await AuthClient().GetAsync($"/api/v1/enhance/{Guid.NewGuid()}");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetById_NoAuth_Returns401()
    {
        var response = await _factory.CreateClient()
            .GetAsync($"/api/v1/enhance/{Guid.NewGuid()}");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}

// ══════════════════════════════════════════════════════════════════════════════
// GET /api/v1/enhance/jobs/{jobId}  &  GET /api/v1/enhance/my-enhancements
// ══════════════════════════════════════════════════════════════════════════════

[Collection(nameof(JobEnhancerApiCollection))]
public sealed class ListEnhancementsTests
{
    private readonly JobEnhancerWebApplicationFactory _factory;
    private static readonly Guid   _userId = Guid.NewGuid();
    private static readonly string _token  =
        JobEnhancerWebApplicationFactory.GenerateToken(_userId);

    public ListEnhancementsTests(JobEnhancerWebApplicationFactory factory) => _factory = factory;

    private HttpClient AuthClient()
    {
        var c = _factory.CreateClient();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        return c;
    }

    [Fact]
    public async Task GetByJob_ReturnsEnhancementsForJob()
    {
        await _factory.ResetDatabaseAsync();
        var jobId = Guid.NewGuid();
        await AuthClient().PostAsync("/api/v1/enhance",
            new StringContent(JsonSerializer.Serialize(new
            {
                jobId, title = "Dev",
                description = "We are seeking an experienced ServiceNow developer to join our team.",
                requirements = (string?)null
            }), Encoding.UTF8, "application/json"));

        var response = await AuthClient().GetAsync($"/api/v1/enhance/jobs/{jobId}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<EnhancementResponse[]>(Json.Opts);
        body!.Should().HaveCount(1);
        body[0].JobId.Should().Be(jobId);
    }

    [Fact]
    public async Task GetMyEnhancements_ReturnsAllForUser_NewestFirst()
    {
        await _factory.ResetDatabaseAsync();
        await AuthClient().PostAsync("/api/v1/enhance", Payloads.CleanJob(Guid.NewGuid()));
        await AuthClient().PostAsync("/api/v1/enhance", Payloads.BiasedJob(Guid.NewGuid()));

        var response = await AuthClient().GetAsync("/api/v1/enhance/my-enhancements");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<EnhancementResponse[]>(Json.Opts);
        body!.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetMyEnhancements_OnlyOwnResults()
    {
        await _factory.ResetDatabaseAsync();
        await AuthClient().PostAsync("/api/v1/enhance", Payloads.CleanJob(Guid.NewGuid()));

        var otherToken = JobEnhancerWebApplicationFactory.GenerateToken(Guid.NewGuid());
        var other      = _factory.CreateClient();
        other.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", otherToken);

        var response = await other.GetAsync("/api/v1/enhance/my-enhancements");
        var body     = await response.Content.ReadFromJsonAsync<EnhancementResponse[]>(Json.Opts);
        body!.Should().BeEmpty();
    }

    [Fact]
    public async Task GetMyEnhancements_NoAuth_Returns401()
    {
        var response = await _factory.CreateClient()
            .GetAsync("/api/v1/enhance/my-enhancements");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}

// ══════════════════════════════════════════════════════════════════════════════
// Health
// ══════════════════════════════════════════════════════════════════════════════

[Collection(nameof(JobEnhancerApiCollection))]
public sealed class JobEnhancerHealthTests
{
    private readonly JobEnhancerWebApplicationFactory _factory;
    public JobEnhancerHealthTests(JobEnhancerWebApplicationFactory factory)
        => _factory = factory;

    [Fact]
    public async Task Health_Returns200()
        => (await _factory.CreateClient().GetAsync("/health"))
            .StatusCode.Should().Be(HttpStatusCode.OK);
}
