using FluentAssertions;
using SNHub.Applications.Domain.Enums;
using SNHub.Applications.IntegrationTests.Brokers;
using SNHub.Applications.IntegrationTests.Models;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace SNHub.Applications.IntegrationTests.Apis;

[Collection(nameof(ApplicationsApiCollection))]
public sealed partial class ApplicationsApiTests
{
    private readonly ApplicationsWebApplicationFactory _factory;
    private static readonly JsonSerializerOptions _json = new() { PropertyNameCaseInsensitive = true };

    private static readonly Guid _candidateId  = Guid.NewGuid();
    private static readonly Guid _candidate2Id = Guid.NewGuid();
    private static readonly Guid _employerId   = Guid.NewGuid();

    private static readonly string _candidateToken  = ApplicationsWebApplicationFactory.GenerateToken(_candidateId,  "Candidate");
    private static readonly string _candidate2Token = ApplicationsWebApplicationFactory.GenerateToken(_candidate2Id, "Candidate");
    private static readonly string _employerToken   = ApplicationsWebApplicationFactory.GenerateToken(_employerId,   "Employer");

    public ApplicationsApiTests(ApplicationsWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.OverridePlan = CandidatePlan.Pro; // Unlimited by default for most tests
    }

    private ApplicationsApiBroker BrokerFor(string token)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return new ApplicationsApiBroker(client);
    }

    private ApplicationsApiBroker AnonBroker() => new(_factory.CreateClient());

    private static ApplyRequest ValidApplyRequest(string? letter = "I am very interested in this role.") =>
        new(letter, "https://cv.example.com/cv.pdf");

    private async Task<ApplicationResponse> ApplyAndGetAsync(Guid? jobId = null, Guid? candidateToken = null)
    {
        var broker = BrokerFor(candidateToken is null ? _candidateToken
            : ApplicationsWebApplicationFactory.GenerateToken(candidateToken.Value, "Candidate"));
        var response = await broker.ApplyAsync(jobId ?? Guid.NewGuid(), ValidApplyRequest());
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<ApplicationResponse>(_json))!;
    }
}

// ════════════════════════════════════════════════════════════════════════════════
// Apply to Job
// ════════════════════════════════════════════════════════════════════════════════

public sealed partial class ApplicationsApiTests
{
    [Fact]
    public async Task Apply_ValidRequest_Returns201()
    {
        await _factory.ResetDatabaseAsync();
        var response = await BrokerFor(_candidateToken).ApplyAsync(Guid.NewGuid(), ValidApplyRequest());
        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Apply_Returns_AppliedStatus()
    {
        await _factory.ResetDatabaseAsync();
        var app = await ApplyAndGetAsync();
        app.Status.Should().Be("Applied");
        app.CandidateId.Should().Be(_candidateId);
    }

    [Fact]
    public async Task Apply_WithCoverLetter_PersistsCoverLetter()
    {
        await _factory.ResetDatabaseAsync();
        var app = await ApplyAndGetAsync();
        app.CoverLetter.Should().Be("I am very interested in this role.");
    }

    [Fact]
    public async Task Apply_NoAuth_Returns401()
    {
        var response = await AnonBroker().ApplyAsync(Guid.NewGuid(), ValidApplyRequest());
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Apply_EmployerRole_Returns403()
    {
        var response = await BrokerFor(_employerToken).ApplyAsync(Guid.NewGuid(), ValidApplyRequest());
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Apply_Duplicate_Returns409()
    {
        await _factory.ResetDatabaseAsync();
        var jobId = Guid.NewGuid();
        await BrokerFor(_candidateToken).ApplyAsync(jobId, ValidApplyRequest());

        var response = await BrokerFor(_candidateToken).ApplyAsync(jobId, ValidApplyRequest());
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Apply_FreePlan_AtLimit_Returns402()
    {
        await _factory.ResetDatabaseAsync();
        _factory.OverridePlan = CandidatePlan.Free; // 5 app limit

        // Apply 5 times to different jobs
        for (var i = 0; i < 5; i++)
            await BrokerFor(_candidateToken).ApplyAsync(Guid.NewGuid(), ValidApplyRequest());

        // 6th should hit limit
        var response = await BrokerFor(_candidateToken).ApplyAsync(Guid.NewGuid(), ValidApplyRequest());
        response.StatusCode.Should().Be(HttpStatusCode.PaymentRequired);
        _factory.OverridePlan = CandidatePlan.Pro; // Reset
    }

    [Fact]
    public async Task Apply_ProPlan_NoLimit()
    {
        await _factory.ResetDatabaseAsync();
        _factory.OverridePlan = CandidatePlan.Pro;

        // Apply 10 times — should all succeed
        for (var i = 0; i < 10; i++)
        {
            var r = await BrokerFor(_candidateToken).ApplyAsync(Guid.NewGuid(), ValidApplyRequest());
            r.StatusCode.Should().Be(HttpStatusCode.Created, $"application {i + 1} should succeed on Pro plan");
        }
    }

    [Fact]
    public async Task Apply_InvalidCvUrl_Returns400()
    {
        var response = await BrokerFor(_candidateToken).ApplyAsync(
            Guid.NewGuid(), new ApplyRequest("Cover", "not-a-valid-url"));
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}

// ════════════════════════════════════════════════════════════════════════════════
// Get Application
// ════════════════════════════════════════════════════════════════════════════════

public sealed partial class ApplicationsApiTests
{
    [Fact]
    public async Task GetById_CandidateViewsOwn_Returns200()
    {
        await _factory.ResetDatabaseAsync();
        var app = await ApplyAndGetAsync();

        var response = await BrokerFor(_candidateToken).GetByIdAsync(app.Id);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<ApplicationResponse>(_json);
        body!.Id.Should().Be(app.Id);
    }

    [Fact]
    public async Task GetById_CandidateViewsOther_Returns403()
    {
        await _factory.ResetDatabaseAsync();
        var app = await ApplyAndGetAsync(_candidateId);

        var response = await BrokerFor(_candidate2Token).GetByIdAsync(app.Id);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetById_EmployerViewsAny_Returns200()
    {
        await _factory.ResetDatabaseAsync();
        var app = await ApplyAndGetAsync();

        var response = await BrokerFor(_employerToken).GetByIdAsync(app.Id);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetById_NotFound_Returns404()
    {
        var response = await BrokerFor(_candidateToken).GetByIdAsync(Guid.NewGuid());
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetMine_Returns_OnlyOwnApplications()
    {
        await _factory.ResetDatabaseAsync();
        await ApplyAndGetAsync(Guid.NewGuid(), _candidateId);
        await ApplyAndGetAsync(Guid.NewGuid(), _candidateId);
        // Different candidate applies too
        await BrokerFor(_candidate2Token).ApplyAsync(Guid.NewGuid(), ValidApplyRequest());

        var response = await BrokerFor(_candidateToken).GetMineAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<PagedApplicationResponse>(_json);
        body!.Total.Should().Be(2);
        body.Items.Should().AllSatisfy(a => a.CandidateId.Should().Be(_candidateId));
    }

    [Fact]
    public async Task GetMine_NoAuth_Returns401()
    {
        var response = await AnonBroker().GetMineAsync();
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}

// ════════════════════════════════════════════════════════════════════════════════
// Get For Job (Employer)
// ════════════════════════════════════════════════════════════════════════════════

public sealed partial class ApplicationsApiTests
{
    [Fact]
    public async Task GetForJob_Employer_Returns200()
    {
        await _factory.ResetDatabaseAsync();
        var jobId = Guid.NewGuid();
        await BrokerFor(_candidateToken).ApplyAsync(jobId, ValidApplyRequest());
        await BrokerFor(_candidate2Token).ApplyAsync(jobId, ValidApplyRequest());

        var response = await BrokerFor(_employerToken).GetForJobAsync(jobId);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<PagedApplicationResponse>(_json);
        body!.Total.Should().Be(2);
    }

    [Fact]
    public async Task GetForJob_CandidateRole_Returns403()
    {
        var response = await BrokerFor(_candidateToken).GetForJobAsync(Guid.NewGuid());
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetForJob_StatusFilter_ReturnsFilteredResults()
    {
        await _factory.ResetDatabaseAsync();
        var jobId = Guid.NewGuid();
        var app = await (await BrokerFor(_candidateToken).ApplyAsync(jobId, ValidApplyRequest()))
            .Content.ReadFromJsonAsync<ApplicationResponse>(_json);

        // Move to Screening
        await BrokerFor(_employerToken).UpdateStatusAsync(app!.Id,
            new UpdateStatusRequest(2, "Good candidate", null)); // 2=Screening

        await BrokerFor(_candidate2Token).ApplyAsync(jobId, ValidApplyRequest()); // stays Applied

        var response = await BrokerFor(_employerToken).GetForJobAsync(jobId, "Screening");
        var body = await response.Content.ReadFromJsonAsync<PagedApplicationResponse>(_json);
        body!.Total.Should().Be(1);
        body.Items.Should().AllSatisfy(a => a.Status.Should().Be("Screening"));
    }
}

// ════════════════════════════════════════════════════════════════════════════════
// Update Status
// ════════════════════════════════════════════════════════════════════════════════

public sealed partial class ApplicationsApiTests
{
    [Theory]
    [InlineData(2, "Screening")]
    [InlineData(3, "Interview")]
    [InlineData(4, "Offer")]
    [InlineData(5, "Hired")]
    public async Task UpdateStatus_ValidTransitions_Return200(int status, string expectedLabel)
    {
        await _factory.ResetDatabaseAsync();
        var app = await ApplyAndGetAsync();

        var response = await BrokerFor(_employerToken)
            .UpdateStatusAsync(app.Id, new UpdateStatusRequest(status, "Moving forward", null));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ApplicationResponse>(_json);
        body!.Status.Should().Be(expectedLabel);
    }

    [Fact]
    public async Task UpdateStatus_Rejected_RequiresReason()
    {
        await _factory.ResetDatabaseAsync();
        var app = await ApplyAndGetAsync();

        var response = await BrokerFor(_employerToken)
            .UpdateStatusAsync(app.Id, new UpdateStatusRequest(6, null, null)); // 6=Rejected, no reason

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UpdateStatus_Rejected_WithReason_Returns200()
    {
        await _factory.ResetDatabaseAsync();
        var app = await ApplyAndGetAsync();

        var response = await BrokerFor(_employerToken)
            .UpdateStatusAsync(app.Id, new UpdateStatusRequest(6, null, "Not enough experience."));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ApplicationResponse>(_json);
        body!.Status.Should().Be("Rejected");
        body.RejectionReason.Should().Be("Not enough experience.");
    }

    [Fact]
    public async Task UpdateStatus_CandidateRole_Returns403()
    {
        await _factory.ResetDatabaseAsync();
        var app = await ApplyAndGetAsync();

        var response = await BrokerFor(_candidateToken)
            .UpdateStatusAsync(app.Id, new UpdateStatusRequest(2, null, null));
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task UpdateStatus_NotFound_Returns404()
    {
        var response = await BrokerFor(_employerToken)
            .UpdateStatusAsync(Guid.NewGuid(), new UpdateStatusRequest(2, null, null));
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}

// ════════════════════════════════════════════════════════════════════════════════
// Withdraw
// ════════════════════════════════════════════════════════════════════════════════

public sealed partial class ApplicationsApiTests
{
    [Fact]
    public async Task Withdraw_OwnApplication_Returns204()
    {
        await _factory.ResetDatabaseAsync();
        var app = await ApplyAndGetAsync();

        var response = await BrokerFor(_candidateToken).WithdrawAsync(app.Id);
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Withdraw_WithdrawnApplication_IsNoLongerActive()
    {
        await _factory.ResetDatabaseAsync();
        var app = await ApplyAndGetAsync();
        await BrokerFor(_candidateToken).WithdrawAsync(app.Id);

        var response = await BrokerFor(_candidateToken).GetByIdAsync(app.Id);
        var body = await response.Content.ReadFromJsonAsync<ApplicationResponse>(_json);
        body!.Status.Should().Be("Withdrawn");
    }

    [Fact]
    public async Task Withdraw_OtherCandidateApplication_Returns403()
    {
        await _factory.ResetDatabaseAsync();
        var app = await ApplyAndGetAsync(_candidateId);

        var response = await BrokerFor(_candidate2Token).WithdrawAsync(app.Id);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Withdraw_NotFound_Returns404()
    {
        var response = await BrokerFor(_candidateToken).WithdrawAsync(Guid.NewGuid());
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Withdraw_AlreadyWithdrawn_Returns400()
    {
        await _factory.ResetDatabaseAsync();
        var app = await ApplyAndGetAsync();
        await BrokerFor(_candidateToken).WithdrawAsync(app.Id);

        var response = await BrokerFor(_candidateToken).WithdrawAsync(app.Id);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Withdraw_NoAuth_Returns401()
    {
        var response = await AnonBroker().WithdrawAsync(Guid.NewGuid());
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}

// ════════════════════════════════════════════════════════════════════════════════
// Health
// ════════════════════════════════════════════════════════════════════════════════

public sealed partial class ApplicationsApiTests
{
    [Fact]
    public async Task Health_Returns200()
    {
        var response = await _factory.CreateClient().GetAsync("/health");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
