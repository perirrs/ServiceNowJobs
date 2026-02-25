using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SNHub.Jobs.Domain.Enums;
using SNHub.Jobs.Infrastructure.Persistence;
using SNHub.Jobs.IntegrationTests.Brokers;
using SNHub.Jobs.IntegrationTests.Models;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace SNHub.Jobs.IntegrationTests.Apis;

[Collection(nameof(JobsApiCollection))]
public sealed partial class JobsApiTests
{
    private readonly JobsWebApplicationFactory _factory;
    private readonly JobsApiBroker _broker;
    private readonly JobsApiBroker _employerBroker;

    private static readonly Guid _employerId   = Guid.NewGuid();
    private static readonly Guid _employer2Id  = Guid.NewGuid();
    private static readonly string _employerToken  = TestTokenHelper.GenerateToken(_employerId,  "Employer");
    private static readonly string _employer2Token = TestTokenHelper.GenerateToken(_employer2Id, "Employer");
    private static readonly string _candidateToken = TestTokenHelper.GenerateToken(Guid.NewGuid(), "Candidate");

    private static readonly JsonSerializerOptions _json =
        new() { PropertyNameCaseInsensitive = true };

    public JobsApiTests(JobsWebApplicationFactory factory)
    {
        _factory = factory;
        _broker  = new JobsApiBroker(factory.CreateClient());

        var employerClient = factory.CreateClient();
        employerClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", _employerToken);
        _employerBroker = new JobsApiBroker(employerClient);
    }

    // ── Authenticated client helpers ──────────────────────────────────────────

    private JobsApiBroker BrokerFor(string token)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);
        return new JobsApiBroker(client);
    }

    // ── Data factories ────────────────────────────────────────────────────────

    private static CreateJobRequest ValidCreateRequest(bool publish = false) => new(
        Title:                  "ServiceNow ITSM Developer",
        Description:            "Excellent opportunity for an ITSM specialist.",
        Requirements:           "3+ years ServiceNow experience",
        Benefits:               "Remote, flexible hours",
        CompanyName:            "TechCorp",
        JobType:                1,   // FullTime
        WorkMode:               1,   // Remote
        ExperienceLevel:        2,   // MidLevel
        Location:               "London",
        Country:                "GBR",
        SalaryMin:              50_000,
        SalaryMax:              70_000,
        SalaryCurrency:         "GBP",
        IsSalaryVisible:        true,
        SkillsRequired:         ["ITSM", "Flow Designer", "Business Rules"],
        CertificationsRequired: ["CSA"],
        ServiceNowVersions:     ["Xanadu", "Washington"],
        PublishImmediately:     publish,
        ExpiresAt:              DateTimeOffset.UtcNow.AddDays(30));

    private async Task<JobResponse> CreateAndPublishJobAsync(Guid? employerId = null)
    {
        var broker = employerId.HasValue
            ? BrokerFor(TestTokenHelper.GenerateToken(employerId.Value, "Employer"))
            : _employerBroker;

        var response = await broker.CreateJobAsync(ValidCreateRequest(publish: true));
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<JobResponse>(_json);
        return body!;
    }

    private async Task<JobResponse> CreateDraftJobAsync()
    {
        var response = await _employerBroker.CreateJobAsync(ValidCreateRequest(publish: false));
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<JobResponse>(_json);
        return body!;
    }
}

// ════════════════════════════════════════════════════════════════════════════════
// Public Search Tests
// ════════════════════════════════════════════════════════════════════════════════

public sealed partial class JobsApiTests
{
    [Fact]
    public async Task Search_NoAuth_Returns200()
    {
        var response = await _broker.SearchJobsAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Search_ActiveJob_AppearsInResults()
    {
        await _factory.ResetDatabaseAsync();
        await CreateAndPublishJobAsync();

        var response = await _broker.SearchJobsAsync();
        var body = await response.Content.ReadFromJsonAsync<JobSearchResponse>(_json);

        body!.Total.Should().BeGreaterThanOrEqualTo(1);
        body.Items.Should().Contain(j => j.Title == "ServiceNow ITSM Developer");
    }

    [Fact]
    public async Task Search_DraftJob_DoesNotAppearInResults()
    {
        await _factory.ResetDatabaseAsync();
        await CreateDraftJobAsync();

        var response = await _broker.SearchJobsAsync();
        var body = await response.Content.ReadFromJsonAsync<JobSearchResponse>(_json);

        body!.Items.Should().NotContain(j => j.Status == "Draft",
            "draft jobs must not appear in public search");
    }

    [Fact]
    public async Task Search_KeywordFilter_ReturnsMatchingJobs()
    {
        await _factory.ResetDatabaseAsync();
        await CreateAndPublishJobAsync();

        var response = await _broker.SearchJobsAsync(keyword: "ITSM");
        response.StatusCode.Should().Be(HttpStatusCode.OK, "search should succeed — if 500 check repository query");

        var body = await response.Content.ReadFromJsonAsync<JobSearchResponse>(_json);
        body!.Items.Should().NotBeNullOrEmpty();
        body.Items.Should().AllSatisfy(j =>
            j.Title.Contains("ITSM", StringComparison.OrdinalIgnoreCase).Should().BeTrue());
    }

    [Fact]
    public async Task Search_Pagination_RespectsPageSize()
    {
        await _factory.ResetDatabaseAsync();
        // Create 3 published jobs
        for (var i = 0; i < 3; i++)
            await CreateAndPublishJobAsync();

        var response = await _broker.SearchJobsAsync(pageSize: 2);
        var body = await response.Content.ReadFromJsonAsync<JobSearchResponse>(_json);

        body!.Items.Should().HaveCount(2);
        body.HasNextPage.Should().BeTrue();
    }
}

// ════════════════════════════════════════════════════════════════════════════════
// Get Job Tests
// ════════════════════════════════════════════════════════════════════════════════

public sealed partial class JobsApiTests
{
    [Fact]
    public async Task GetJob_ExistingJob_Returns200WithDto()
    {
        await _factory.ResetDatabaseAsync();
        var job = await CreateAndPublishJobAsync();

        var response = await _broker.GetJobAsync(job.Id);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JobResponse>(_json);
        body!.Id.Should().Be(job.Id);
        body.Title.Should().Be("ServiceNow ITSM Developer");
    }

    [Fact]
    public async Task GetJob_ExistingJob_IncrementsViewCount()
    {
        await _factory.ResetDatabaseAsync();
        var job = await CreateAndPublishJobAsync();

        await _broker.GetJobAsync(job.Id);
        await _broker.GetJobAsync(job.Id);

        var response = await _broker.GetJobAsync(job.Id);
        var body = await response.Content.ReadFromJsonAsync<JobResponse>(_json);
        body!.ViewCount.Should().Be(3);
    }

    [Fact]
    public async Task GetJob_NonExistentId_Returns404()
    {
        var response = await _broker.GetJobAsync(Guid.NewGuid());
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetJob_SkillsDeserialisedCorrectly()
    {
        await _factory.ResetDatabaseAsync();
        var job = await CreateAndPublishJobAsync();

        var response = await _broker.GetJobAsync(job.Id);
        var body = await response.Content.ReadFromJsonAsync<JobResponse>(_json);

        body!.SkillsRequired.Should().Contain("ITSM").And.Contain("Flow Designer");
        body.CertificationsRequired.Should().Contain("CSA");
        body.ServiceNowVersions.Should().Contain("Xanadu");
    }
}

// ════════════════════════════════════════════════════════════════════════════════
// Create Job Tests
// ════════════════════════════════════════════════════════════════════════════════

public sealed partial class JobsApiTests
{
    [Fact]
    public async Task CreateJob_ValidRequest_Returns201()
    {
        await _factory.ResetDatabaseAsync();
        var response = await _employerBroker.CreateJobAsync(ValidCreateRequest());
        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task CreateJob_DefaultsToDraftStatus()
    {
        await _factory.ResetDatabaseAsync();
        var response = await _employerBroker.CreateJobAsync(ValidCreateRequest(publish: false));
        var body = await response.Content.ReadFromJsonAsync<JobResponse>(_json);
        body!.Status.Should().Be("Draft");
    }

    [Fact]
    public async Task CreateJob_PublishImmediately_ReturnsActiveStatus()
    {
        await _factory.ResetDatabaseAsync();
        var response = await _employerBroker.CreateJobAsync(ValidCreateRequest(publish: true));
        var body = await response.Content.ReadFromJsonAsync<JobResponse>(_json);
        body!.Status.Should().Be("Active");
        body.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task CreateJob_NoAuth_Returns401()
    {
        var response = await _broker.CreateJobAsync(ValidCreateRequest());
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CreateJob_CandidateRole_Returns403()
    {
        var response = await BrokerFor(_candidateToken).CreateJobAsync(ValidCreateRequest());
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task CreateJob_EmptyTitle_Returns400()
    {
        var response = await _employerBroker.CreateJobAsync(ValidCreateRequest() with { Title = "" });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateJob_SalaryMaxLessThanMin_Returns400()
    {
        var response = await _employerBroker.CreateJobAsync(
            ValidCreateRequest() with { SalaryMin = 80_000, SalaryMax = 50_000 });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateJob_Sets_EmployerIdFromToken()
    {
        await _factory.ResetDatabaseAsync();
        var response = await _employerBroker.CreateJobAsync(ValidCreateRequest());
        var body = await response.Content.ReadFromJsonAsync<JobResponse>(_json);
        body!.EmployerId.Should().Be(_employerId);
    }
}

// ════════════════════════════════════════════════════════════════════════════════
// Update Job Tests
// ════════════════════════════════════════════════════════════════════════════════

public sealed partial class JobsApiTests
{
    private static UpdateJobRequest ValidUpdateRequest() => new(
        Title:                  "Updated: ServiceNow Developer",
        Description:            "Updated description with more details.",
        Requirements:           "5+ years experience",
        Benefits:               "Pension, stock options",
        JobType:                3, // Contract
        WorkMode:               2, // Hybrid
        ExperienceLevel:        3, // Senior
        Location:               "Manchester",
        Country:                "GBR",
        SalaryMin:              60_000,
        SalaryMax:              85_000,
        SalaryCurrency:         "GBP",
        IsSalaryVisible:        true,
        SkillsRequired:         ["ITSM", "HRSD", "SecOps"],
        CertificationsRequired: ["CSA", "CIS-ITSM"],
        ServiceNowVersions:     ["Xanadu"],
        ExpiresAt:              DateTimeOffset.UtcNow.AddDays(45));

    [Fact]
    public async Task UpdateJob_Owner_Returns200WithUpdatedFields()
    {
        await _factory.ResetDatabaseAsync();
        var job = await CreateAndPublishJobAsync();

        var response = await _employerBroker.UpdateJobAsync(job.Id, ValidUpdateRequest());
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JobResponse>(_json);
        body!.Title.Should().Be("Updated: ServiceNow Developer");
        body.JobType.Should().Be("Contract");
        body.WorkMode.Should().Be("Hybrid");
    }

    [Fact]
    public async Task UpdateJob_DifferentEmployer_Returns403()
    {
        await _factory.ResetDatabaseAsync();
        var job = await CreateAndPublishJobAsync(_employerId);

        var response = await BrokerFor(_employer2Token).UpdateJobAsync(job.Id, ValidUpdateRequest());
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task UpdateJob_NonExistentJob_Returns404()
    {
        var response = await _employerBroker.UpdateJobAsync(Guid.NewGuid(), ValidUpdateRequest());
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task UpdateJob_NoAuth_Returns401()
    {
        var job = await CreateAndPublishJobAsync();
        var response = await _broker.UpdateJobAsync(job.Id, ValidUpdateRequest());
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}

// ════════════════════════════════════════════════════════════════════════════════
// Publish / Pause Job Tests
// ════════════════════════════════════════════════════════════════════════════════

public sealed partial class JobsApiTests
{
    [Fact]
    public async Task PublishJob_DraftJob_Returns200WithActiveStatus()
    {
        await _factory.ResetDatabaseAsync();
        var job = await CreateDraftJobAsync();

        var response = await _employerBroker.PublishJobAsync(job.Id);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JobResponse>(_json);
        body!.Status.Should().Be("Active");
    }

    [Fact]
    public async Task PublishJob_AlreadyActive_Returns400()
    {
        await _factory.ResetDatabaseAsync();
        var job = await CreateAndPublishJobAsync();

        var response = await _employerBroker.PublishJobAsync(job.Id);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task PublishJob_DifferentEmployer_Returns403()
    {
        await _factory.ResetDatabaseAsync();
        var job = await CreateDraftJobAsync();

        var response = await BrokerFor(_employer2Token).PublishJobAsync(job.Id);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task PauseJob_ActiveJob_Returns200WithPausedStatus()
    {
        await _factory.ResetDatabaseAsync();
        var job = await CreateAndPublishJobAsync();

        var response = await _employerBroker.PauseJobAsync(job.Id);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JobResponse>(_json);
        body!.Status.Should().Be("Paused");
    }

    [Fact]
    public async Task PauseJob_DraftJob_Returns400()
    {
        await _factory.ResetDatabaseAsync();
        var job = await CreateDraftJobAsync();

        var response = await _employerBroker.PauseJobAsync(job.Id);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task PauseJob_PausedJob_ThenPublish_ReturnsActiveStatus()
    {
        await _factory.ResetDatabaseAsync();
        var job = await CreateAndPublishJobAsync();
        await _employerBroker.PauseJobAsync(job.Id);

        var response = await _employerBroker.PublishJobAsync(job.Id);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JobResponse>(_json);
        body!.Status.Should().Be("Active");
    }
}

// ════════════════════════════════════════════════════════════════════════════════
// Close Job Tests
// ════════════════════════════════════════════════════════════════════════════════

public sealed partial class JobsApiTests
{
    [Fact]
    public async Task CloseJob_Owner_Returns204()
    {
        await _factory.ResetDatabaseAsync();
        var job = await CreateAndPublishJobAsync();

        var response = await _employerBroker.CloseJobAsync(job.Id);
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task CloseJob_ClosedJobDisappearsFromSearch()
    {
        await _factory.ResetDatabaseAsync();
        var job = await CreateAndPublishJobAsync();
        await _employerBroker.CloseJobAsync(job.Id);

        var searchResponse = await _broker.SearchJobsAsync();
        var body = await searchResponse.Content.ReadFromJsonAsync<JobSearchResponse>(_json);
        body!.Items.Should().NotContain(j => j.Id == job.Id);
    }

    [Fact]
    public async Task CloseJob_DifferentEmployer_Returns403()
    {
        await _factory.ResetDatabaseAsync();
        var job = await CreateAndPublishJobAsync(_employerId);

        var response = await BrokerFor(_employer2Token).CloseJobAsync(job.Id);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task CloseJob_NonExistentJob_Returns404()
    {
        var response = await _employerBroker.CloseJobAsync(Guid.NewGuid());
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task CloseJob_NoAuth_Returns401()
    {
        var job = await CreateAndPublishJobAsync();
        var response = await _broker.CloseJobAsync(job.Id);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}

// ════════════════════════════════════════════════════════════════════════════════
// GetMyJobs Tests
// ════════════════════════════════════════════════════════════════════════════════

public sealed partial class JobsApiTests
{
    [Fact]
    public async Task GetMyJobs_ReturnsBothDraftAndPublished()
    {
        await _factory.ResetDatabaseAsync();
        await CreateDraftJobAsync();
        await CreateAndPublishJobAsync();

        var response = await _employerBroker.GetMyJobsAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JobSearchResponse>(_json);
        body!.Total.Should().Be(2);
    }

    [Fact]
    public async Task GetMyJobs_StatusFilter_ReturnsDraftOnly()
    {
        await _factory.ResetDatabaseAsync();
        await CreateDraftJobAsync();
        await CreateAndPublishJobAsync();

        var response = await _employerBroker.GetMyJobsAsync(status: "1"); // Draft=1
        var body = await response.Content.ReadFromJsonAsync<JobSearchResponse>(_json);

        body!.Items.Should().AllSatisfy(j => j.Status.Should().Be("Draft"));
    }

    [Fact]
    public async Task GetMyJobs_DoesNotReturnOtherEmployersJobs()
    {
        await _factory.ResetDatabaseAsync();
        await CreateAndPublishJobAsync(_employerId);
        await CreateAndPublishJobAsync(_employer2Id);

        var response = await _employerBroker.GetMyJobsAsync();
        var body = await response.Content.ReadFromJsonAsync<JobSearchResponse>(_json);

        body!.Items.Should().AllSatisfy(j => j.EmployerId.Should().Be(_employerId));
    }

    [Fact]
    public async Task GetMyJobs_NoAuth_Returns401()
    {
        var response = await _broker.GetMyJobsAsync();
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetMyJobs_CandidateRole_Returns403()
    {
        var response = await BrokerFor(_candidateToken).GetMyJobsAsync();
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}

// ── Health ────────────────────────────────────────────────────────────────────

public sealed partial class JobsApiTests
{
    [Fact]
    public async Task Health_Returns200()
    {
        var response = await _factory.CreateClient().GetAsync("/health");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}

// ── Extension helpers ─────────────────────────────────────────────────────────

// (no extension helpers needed)
