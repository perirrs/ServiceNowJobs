using Microsoft.Extensions.DependencyInjection;
using FluentAssertions;
using SNHub.Matching.Application.Interfaces;
using SNHub.Matching.IntegrationTests.Brokers;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace SNHub.Matching.IntegrationTests.Apis;

// ── Response models ───────────────────────────────────────────────────────────

file sealed class EmbeddingStatusResponse
{
    public Guid    DocumentId   { get; set; }
    public string  DocumentType { get; set; } = string.Empty;
    public string  Status       { get; set; } = string.Empty;
    public int     RetryCount   { get; set; }
}

file sealed class MatchResultsResponse<T>
{
    public int    Total          { get; set; }
    public int    Page           { get; set; }
    public int    PageSize       { get; set; }
    public bool   EmbeddingReady { get; set; }
    public T[]    Results        { get; set; } = [];
}

file sealed class JobMatchResponse
{
    public Guid   JobId        { get; set; }
    public string Title        { get; set; } = string.Empty;
    public double Score        { get; set; }
    public int    ScorePercent { get; set; }
}

file sealed class CandidateMatchResponse
{
    public Guid   UserId        { get; set; }
    public double Score         { get; set; }
    public int    ScorePercent  { get; set; }
    public string[] MatchedSkills { get; set; } = [];
}

file static class Json
{
    public static readonly JsonSerializerOptions Opts =
        new() { PropertyNameCaseInsensitive = true };
}

// ── Seed helpers ──────────────────────────────────────────────────────────────

file static class Seeds
{
    public static JobData ActiveJob(Guid jobId, Guid employerId) => new()
    {
        Id = jobId, EmployerId = employerId,
        Title = "Senior ServiceNow Developer",
        Description = "Build ITSM and HRSD flows in Xanadu",
        Requirements = "5+ years ServiceNow experience",
        CompanyName = "Acme Corp", Location = "London", Country = "GB",
        WorkMode = "Remote", ExperienceLevel = "Senior", JobType = "FullTime",
        Skills = ["ITSM", "HRSD", "Flow Designer"],
        ServiceNowVersions = ["Xanadu", "Washington"],
        IsActive = true, SalaryMin = 70000, SalaryMax = 90000, SalaryCurrency = "GBP",
        IsSalaryVisible = true, CreatedAt = DateTimeOffset.UtcNow
    };

    public static CandidateData Candidate(Guid userId) => new()
    {
        UserId = userId, FirstName = "Jane", LastName = "Smith",
        Headline = "Senior SNow Developer | ITSM & HRSD",
        Bio = "8 years building ServiceNow solutions",
        CurrentRole = "Senior ServiceNow Developer",
        Location = "London", Country = "GB",
        YearsOfExperience = 8, ExperienceLevel = "Senior",
        Availability = "OpenToOpportunities", OpenToRemote = true,
        Skills = ["ITSM", "HRSD", "Flow Designer", "Integration Hub"],
        Certifications = ["CSA", "CIS-ITSM"],
        ServiceNowVersions = ["Xanadu", "Washington"],
        UpdatedAt = DateTimeOffset.UtcNow
    };
}

// ══════════════════════════════════════════════════════════════════════════════
// POST /api/v1/matching/index/my-profile
// ══════════════════════════════════════════════════════════════════════════════

[Collection(nameof(MatchingApiCollection))]
public sealed class IndexMyProfileTests
{
    private readonly MatchingWebApplicationFactory _factory;
    private static readonly Guid   _userId = Guid.NewGuid();
    private static readonly string _token  =
        MatchingWebApplicationFactory.GenerateToken(_userId);

    public IndexMyProfileTests(MatchingWebApplicationFactory factory) => _factory = factory;

    private HttpClient AuthClient()
    {
        var c = _factory.CreateClient();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        return c;
    }

    [Fact]
    public async Task IndexProfile_ReturnsAccepted_WithPendingStatus()
    {
        await _factory.ResetDatabaseAsync();
        var response = await AuthClient().PostAsync(
            "/api/v1/matching/index/my-profile", null);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var body = await response.Content.ReadFromJsonAsync<EmbeddingStatusResponse>(Json.Opts);
        body!.DocumentId.Should().Be(_userId);
        body.DocumentType.Should().Be("CandidateProfile");
        body.Status.Should().Be("Pending");
    }

    [Fact]
    public async Task IndexProfile_CalledTwice_ResetsToPending()
    {
        await _factory.ResetDatabaseAsync();
        await AuthClient().PostAsync("/api/v1/matching/index/my-profile", null);
        var second = await AuthClient().PostAsync("/api/v1/matching/index/my-profile", null);

        second.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var body = await second.Content.ReadFromJsonAsync<EmbeddingStatusResponse>(Json.Opts);
        body!.Status.Should().Be("Pending");
    }

    [Fact]
    public async Task IndexProfile_NoAuth_Returns401()
    {
        var response = await _factory.CreateClient()
            .PostAsync("/api/v1/matching/index/my-profile", null);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}

// ══════════════════════════════════════════════════════════════════════════════
// POST /api/v1/matching/index/jobs/{jobId}
// ══════════════════════════════════════════════════════════════════════════════

[Collection(nameof(MatchingApiCollection))]
public sealed class IndexJobTests
{
    private readonly MatchingWebApplicationFactory _factory;
    private static readonly Guid   _employerId = Guid.NewGuid();
    private static readonly string _token =
        MatchingWebApplicationFactory.GenerateToken(_employerId, "Employer");

    public IndexJobTests(MatchingWebApplicationFactory factory) => _factory = factory;

    private HttpClient AuthClient()
    {
        var c = _factory.CreateClient();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        return c;
    }

    [Fact]
    public async Task IndexJob_Returns202WithPendingStatus()
    {
        await _factory.ResetDatabaseAsync();
        var jobId    = Guid.NewGuid();
        var response = await AuthClient().PostAsync(
            $"/api/v1/matching/index/jobs/{jobId}", null);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var body = await response.Content.ReadFromJsonAsync<EmbeddingStatusResponse>(Json.Opts);
        body!.DocumentId.Should().Be(jobId);
        body.DocumentType.Should().Be("Job");
        body.Status.Should().Be("Pending");
    }

    [Fact]
    public async Task IndexJob_NoAuth_Returns401()
    {
        var response = await _factory.CreateClient()
            .PostAsync($"/api/v1/matching/index/jobs/{Guid.NewGuid()}", null);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}

// ══════════════════════════════════════════════════════════════════════════════
// GET /api/v1/matching/my-job-matches
// ══════════════════════════════════════════════════════════════════════════════

[Collection(nameof(MatchingApiCollection))]
public sealed class GetMyJobMatchesTests
{
    private readonly MatchingWebApplicationFactory _factory;
    private static readonly Guid   _userId = Guid.NewGuid();
    private static readonly string _token  =
        MatchingWebApplicationFactory.GenerateToken(_userId);

    public GetMyJobMatchesTests(MatchingWebApplicationFactory factory) => _factory = factory;

    private HttpClient AuthClient()
    {
        var c = _factory.CreateClient();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        return c;
    }

    [Fact]
    public async Task GetMyJobMatches_EmbeddingNotIndexed_ReturnsNotReady()
    {
        await _factory.ResetDatabaseAsync();
        var response = await AuthClient().GetAsync("/api/v1/matching/my-job-matches");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content
            .ReadFromJsonAsync<MatchResultsResponse<JobMatchResponse>>(Json.Opts);
        body!.EmbeddingReady.Should().BeFalse();
        body.Results.Should().BeEmpty();
    }

    [Fact]
    public async Task GetMyJobMatches_AfterIndexing_ReturnsReadyWithResults()
    {
        await _factory.ResetDatabaseAsync();

        // Seed a job
        var jobId = Guid.NewGuid();
        _factory.Jobs.Seed(Seeds.ActiveJob(jobId, Guid.NewGuid()));

        // Seed the candidate so ProcessEmbeddingCommand can fetch their data
        _factory.Profiles.Seed(Seeds.Candidate(_userId));

        // Index profile (triggers EmbeddingRecord creation)
        await AuthClient().PostAsync("/api/v1/matching/index/my-profile", null);

        // Manually process embedding via the background logic
        using var scope    = _factory.Services.CreateScope();
        var mediator       = scope.ServiceProvider
            .GetRequiredService<MediatR.IMediator>();
        var vectorSearch   = scope.ServiceProvider
            .GetRequiredService<IVectorSearchService>() as
            SNHub.Matching.Infrastructure.Services.InMemoryVectorSearchService;

        // Upsert a job directly into in-memory search so it can be matched
        await vectorSearch!.UpsertJobAsync(new SNHub.Matching.Application.DTOs.JobSearchDocument
        {
            Id = jobId.ToString(), Title = "SNow Dev", Description = "x",
            WorkMode = "Remote", ExperienceLevel = "Senior", JobType = "FullTime",
            Skills = ["ITSM"], ServiceNowVersions = [],
            Embedding = new float[1536], CreatedAt = DateTimeOffset.UtcNow
        });

        // Process the candidate embedding
        await mediator.Send(new SNHub.Matching.Application.Commands.ProcessEmbedding
            .ProcessEmbeddingCommand(_userId,
                SNHub.Matching.Domain.Enums.DocumentType.CandidateProfile));

        var response = await AuthClient().GetAsync("/api/v1/matching/my-job-matches");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content
            .ReadFromJsonAsync<MatchResultsResponse<JobMatchResponse>>(Json.Opts);
        body!.EmbeddingReady.Should().BeTrue();
        body.Results.Should().NotBeEmpty();
    }

    [Fact]
    public async Task GetMyJobMatches_NoAuth_Returns401()
    {
        var response = await _factory.CreateClient()
            .GetAsync("/api/v1/matching/my-job-matches");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetMyJobMatches_InvalidPage_Returns400()
    {
        var response = await AuthClient()
            .GetAsync("/api/v1/matching/my-job-matches?page=0");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}

// ══════════════════════════════════════════════════════════════════════════════
// GET /api/v1/matching/jobs/{jobId}/candidates
// ══════════════════════════════════════════════════════════════════════════════

[Collection(nameof(MatchingApiCollection))]
public sealed class GetCandidatesForJobTests
{
    private readonly MatchingWebApplicationFactory _factory;
    private static readonly Guid   _employerId = Guid.NewGuid();
    private static readonly Guid   _otherId    = Guid.NewGuid();
    private static readonly string _employerToken =
        MatchingWebApplicationFactory.GenerateToken(_employerId, "Employer");
    private static readonly string _otherToken =
        MatchingWebApplicationFactory.GenerateToken(_otherId, "Employer");

    public GetCandidatesForJobTests(MatchingWebApplicationFactory factory) => _factory = factory;

    private HttpClient ClientFor(string token)
    {
        var c = _factory.CreateClient();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return c;
    }

    [Fact]
    public async Task GetCandidates_EmbeddingNotIndexed_ReturnsNotReady()
    {
        await _factory.ResetDatabaseAsync();
        var jobId = Guid.NewGuid();
        _factory.Jobs.Seed(Seeds.ActiveJob(jobId, _employerId));

        var response = await ClientFor(_employerToken)
            .GetAsync($"/api/v1/matching/jobs/{jobId}/candidates");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content
            .ReadFromJsonAsync<MatchResultsResponse<CandidateMatchResponse>>(Json.Opts);
        body!.EmbeddingReady.Should().BeFalse();
    }

    [Fact]
    public async Task GetCandidates_JobNotFound_Returns404()
    {
        var response = await ClientFor(_employerToken)
            .GetAsync($"/api/v1/matching/jobs/{Guid.NewGuid()}/candidates");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetCandidates_DifferentEmployer_Returns403()
    {
        await _factory.ResetDatabaseAsync();
        var jobId = Guid.NewGuid();
        _factory.Jobs.Seed(Seeds.ActiveJob(jobId, _employerId));

        var response = await ClientFor(_otherToken)
            .GetAsync($"/api/v1/matching/jobs/{jobId}/candidates");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetCandidates_AdminCanAccessAnyJob()
    {
        await _factory.ResetDatabaseAsync();
        var jobId = Guid.NewGuid();
        _factory.Jobs.Seed(Seeds.ActiveJob(jobId, _employerId));

        var adminToken = MatchingWebApplicationFactory.GenerateToken(
            Guid.NewGuid(), "SuperAdmin");
        var response = await ClientFor(adminToken)
            .GetAsync($"/api/v1/matching/jobs/{jobId}/candidates");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetCandidates_NoAuth_Returns401()
    {
        var response = await _factory.CreateClient()
            .GetAsync($"/api/v1/matching/jobs/{Guid.NewGuid()}/candidates");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetCandidates_AfterIndexing_ReturnsCandidates()
    {
        await _factory.ResetDatabaseAsync();
        var jobId    = Guid.NewGuid();
        var userId   = Guid.NewGuid();
        _factory.Jobs.Seed(Seeds.ActiveJob(jobId, _employerId));
        _factory.Profiles.Seed(Seeds.Candidate(userId));

        // Index the job embedding record
        await ClientFor(_employerToken)
            .PostAsync($"/api/v1/matching/index/jobs/{jobId}", null);

        // Process via mediator
        using var scope  = _factory.Services.CreateScope();
        var mediator     = scope.ServiceProvider.GetRequiredService<MediatR.IMediator>();
        var vectorSearch = scope.ServiceProvider
            .GetRequiredService<IVectorSearchService>() as
            SNHub.Matching.Infrastructure.Services.InMemoryVectorSearchService;

        // Seed a candidate into in-memory search
        await vectorSearch!.UpsertCandidateAsync(
            new SNHub.Matching.Application.DTOs.CandidateSearchDocument
            {
                Id = userId.ToString(), ExperienceLevel = "Senior",
                Availability = "OpenToOpportunities",
                Skills = ["ITSM", "HRSD"], Certifications = [],
                ServiceNowVersions = [], Embedding = new float[1536],
                UpdatedAt = DateTimeOffset.UtcNow
            });

        // Process job embedding
        await mediator.Send(new SNHub.Matching.Application.Commands.ProcessEmbedding
            .ProcessEmbeddingCommand(jobId,
                SNHub.Matching.Domain.Enums.DocumentType.Job));

        var response = await ClientFor(_employerToken)
            .GetAsync($"/api/v1/matching/jobs/{jobId}/candidates");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content
            .ReadFromJsonAsync<MatchResultsResponse<CandidateMatchResponse>>(Json.Opts);
        body!.EmbeddingReady.Should().BeTrue();
        body.Results.Should().NotBeEmpty();
        body.Results[0].MatchedSkills.Should().Contain("ITSM");
    }
}

// ══════════════════════════════════════════════════════════════════════════════
// Health
// ══════════════════════════════════════════════════════════════════════════════

[Collection(nameof(MatchingApiCollection))]
public sealed class MatchingHealthTests
{
    private readonly MatchingWebApplicationFactory _factory;
    public MatchingHealthTests(MatchingWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Health_Returns200()
    {
        var response = await _factory.CreateClient().GetAsync("/health");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
