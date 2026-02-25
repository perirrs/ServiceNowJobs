using FluentAssertions;
using SNHub.Profiles.IntegrationTests.Brokers;
using SNHub.Profiles.IntegrationTests.Models;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace SNHub.Profiles.IntegrationTests.Apis;

[Collection(nameof(ProfilesApiCollection))]
public sealed partial class ProfilesApiTests
{
    private readonly ProfilesWebApplicationFactory _factory;
    private static readonly JsonSerializerOptions _json = new() { PropertyNameCaseInsensitive = true };

    private static readonly Guid _candidateId  = Guid.NewGuid();
    private static readonly Guid _candidate2Id = Guid.NewGuid();
    private static readonly Guid _employerId   = Guid.NewGuid();

    private static readonly string _candidateToken  = ProfilesWebApplicationFactory.GenerateToken(_candidateId,  "Candidate");
    private static readonly string _candidate2Token = ProfilesWebApplicationFactory.GenerateToken(_candidate2Id, "Candidate");
    private static readonly string _employerToken   = ProfilesWebApplicationFactory.GenerateToken(_employerId,   "Employer");

    public ProfilesApiTests(ProfilesWebApplicationFactory factory) => _factory = factory;

    private ProfilesApiBroker BrokerFor(string token)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return new ProfilesApiBroker(client);
    }

    private ProfilesApiBroker AnonBroker() => new(_factory.CreateClient());

    private static UpsertCandidateRequest ValidCandidateRequest(string headline = "ServiceNow Developer") =>
        new(headline, "Experienced ServiceNow professional with deep platform knowledge.",
            ExperienceLevel: 3, YearsOfExperience: 5,
            Availability: 2, // OpenToOpportunities
            "Consultant", "Senior Developer",
            "London", "GBR", "Europe/London",
            "https://linkedin.com/in/test", null, null,
            IsPublic: true, 60_000m, 90_000m, "GBP",
            OpenToRemote: true, OpenToRelocation: false,
            Skills: ["ITSM", "HRSD", "CSM"],
            CertificationsJson: null,
            ServiceNowVersions: ["Xanadu", "Washington"]);

    private static UpsertEmployerRequest ValidEmployerRequest(string name = "Acme Corp") =>
        new(name, "ServiceNow consulting firm.", "Technology",
            "51-200", "London", "GBR", "https://acme.com", null);
}

// ════════════════════════════════════════════════════════════════════════════════
// Candidate Profile — Upsert
// ════════════════════════════════════════════════════════════════════════════════

public sealed partial class ProfilesApiTests
{
    [Fact]
    public async Task UpsertCandidateProfile_ValidRequest_Returns200()
    {
        await _factory.ResetDatabaseAsync();
        var response = await BrokerFor(_candidateToken).UpsertCandidateProfileAsync(ValidCandidateRequest());
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task UpsertCandidateProfile_PersistsHeadlineAndBio()
    {
        await _factory.ResetDatabaseAsync();
        await BrokerFor(_candidateToken).UpsertCandidateProfileAsync(ValidCandidateRequest("SNow Architect"));

        var get = await BrokerFor(_candidateToken).GetMyCandidateProfileAsync();
        var body = await get.Content.ReadFromJsonAsync<CandidateProfileResponse>(_json);
        body!.Headline.Should().Be("SNow Architect");
        body.UserId.Should().Be(_candidateId);
    }

    [Fact]
    public async Task UpsertCandidateProfile_PersistsSkills()
    {
        await _factory.ResetDatabaseAsync();
        await BrokerFor(_candidateToken).UpsertCandidateProfileAsync(ValidCandidateRequest());

        var get  = await BrokerFor(_candidateToken).GetMyCandidateProfileAsync();
        var body = await get.Content.ReadFromJsonAsync<CandidateProfileResponse>(_json);
        body!.Skills.Should().Contain("ITSM").And.Contain("HRSD").And.Contain("CSM");
    }

    [Fact]
    public async Task UpsertCandidateProfile_SecondCall_UpdatesProfile()
    {
        await _factory.ResetDatabaseAsync();
        await BrokerFor(_candidateToken).UpsertCandidateProfileAsync(ValidCandidateRequest("First"));
        await BrokerFor(_candidateToken).UpsertCandidateProfileAsync(ValidCandidateRequest("Second"));

        var get  = await BrokerFor(_candidateToken).GetMyCandidateProfileAsync();
        var body = await get.Content.ReadFromJsonAsync<CandidateProfileResponse>(_json);
        body!.Headline.Should().Be("Second"); // only one profile per user
    }

    [Fact]
    public async Task UpsertCandidateProfile_ComputesCompleteness()
    {
        await _factory.ResetDatabaseAsync();
        await BrokerFor(_candidateToken).UpsertCandidateProfileAsync(ValidCandidateRequest());

        var get  = await BrokerFor(_candidateToken).GetMyCandidateProfileAsync();
        var body = await get.Content.ReadFromJsonAsync<CandidateProfileResponse>(_json);
        body!.ProfileCompleteness.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task UpsertCandidateProfile_NoAuth_Returns401()
    {
        var response = await AnonBroker().UpsertCandidateProfileAsync(ValidCandidateRequest());
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task UpsertCandidateProfile_HeadlineTooLong_Returns400()
    {
        var bad = ValidCandidateRequest(new string('x', 201));
        var response = await BrokerFor(_candidateToken).UpsertCandidateProfileAsync(bad);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UpsertCandidateProfile_InvalidSalaryRange_Returns400()
    {
        var bad = ValidCandidateRequest() with { DesiredSalaryMin = 100_000m, DesiredSalaryMax = 50_000m };
        var response = await BrokerFor(_candidateToken).UpsertCandidateProfileAsync(bad);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}

// ════════════════════════════════════════════════════════════════════════════════
// Candidate Profile — Get
// ════════════════════════════════════════════════════════════════════════════════

public sealed partial class ProfilesApiTests
{
    [Fact]
    public async Task GetMyCandidateProfile_NotCreatedYet_Returns404()
    {
        await _factory.ResetDatabaseAsync();
        var response = await BrokerFor(_candidateToken).GetMyCandidateProfileAsync();
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetCandidateProfile_ByUserId_PublicProfile_Returns200()
    {
        await _factory.ResetDatabaseAsync();
        await BrokerFor(_candidateToken).UpsertCandidateProfileAsync(ValidCandidateRequest());

        var response = await AnonBroker().GetCandidateProfileAsync(_candidateId);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<CandidateProfileResponse>(_json);
        body!.UserId.Should().Be(_candidateId);
    }

    [Fact]
    public async Task GetCandidateProfile_NonExistent_Returns404()
    {
        var response = await AnonBroker().GetCandidateProfileAsync(Guid.NewGuid());
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}

// ════════════════════════════════════════════════════════════════════════════════
// Candidate Search
// ════════════════════════════════════════════════════════════════════════════════

public sealed partial class ProfilesApiTests
{
    [Fact]
    public async Task SearchCandidates_NoFilters_ReturnsAllPublic()
    {
        await _factory.ResetDatabaseAsync();
        await BrokerFor(_candidateToken).UpsertCandidateProfileAsync(ValidCandidateRequest());
        await BrokerFor(_candidate2Token).UpsertCandidateProfileAsync(ValidCandidateRequest("Another Dev"));

        var response = await AnonBroker().SearchCandidatesAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<PagedCandidateResponse>(_json);
        body!.Total.Should().Be(2);
    }

    [Fact]
    public async Task SearchCandidates_KeywordFilter_ReturnsMatchingProfiles()
    {
        await _factory.ResetDatabaseAsync();
        await BrokerFor(_candidateToken).UpsertCandidateProfileAsync(ValidCandidateRequest("ITSM Expert"));
        await BrokerFor(_candidate2Token).UpsertCandidateProfileAsync(ValidCandidateRequest("HRSD Specialist"));

        var response = await AnonBroker().SearchCandidatesAsync(keyword: "ITSM");
        var body     = await response.Content.ReadFromJsonAsync<PagedCandidateResponse>(_json);
        body!.Total.Should().Be(1);
        body.Items[0].Headline.Should().Contain("ITSM");
    }

    [Fact]
    public async Task SearchCandidates_CountryFilter_ReturnsMatchingProfiles()
    {
        await _factory.ResetDatabaseAsync();
        await BrokerFor(_candidateToken).UpsertCandidateProfileAsync(ValidCandidateRequest());  // GBR
        await BrokerFor(_candidate2Token).UpsertCandidateProfileAsync(
            ValidCandidateRequest() with { Country = "USA" });

        var response = await AnonBroker().SearchCandidatesAsync(country: "GBR");
        var body     = await response.Content.ReadFromJsonAsync<PagedCandidateResponse>(_json);
        body!.Total.Should().Be(1);
        body.Items[0].Country.Should().Be("GBR");
    }

    [Fact]
    public async Task SearchCandidates_RemoteFilter_ReturnsOnlyRemote()
    {
        await _factory.ResetDatabaseAsync();
        await BrokerFor(_candidateToken).UpsertCandidateProfileAsync(ValidCandidateRequest()); // OpenToRemote=true
        await BrokerFor(_candidate2Token).UpsertCandidateProfileAsync(
            ValidCandidateRequest() with { OpenToRemote = false });

        var response = await AnonBroker().SearchCandidatesAsync(remote: true);
        var body     = await response.Content.ReadFromJsonAsync<PagedCandidateResponse>(_json);
        body!.Total.Should().Be(1);
        body.Items[0].OpenToRemote.Should().BeTrue();
    }

    [Fact]
    public async Task SearchCandidates_AnonymousUser_Returns200()
    {
        var response = await AnonBroker().SearchCandidatesAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}

// ════════════════════════════════════════════════════════════════════════════════
// Employer Profile
// ════════════════════════════════════════════════════════════════════════════════

public sealed partial class ProfilesApiTests
{
    [Fact]
    public async Task UpsertEmployerProfile_ValidRequest_Returns200()
    {
        await _factory.ResetDatabaseAsync();
        var response = await BrokerFor(_employerToken).UpsertEmployerProfileAsync(ValidEmployerRequest());
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task UpsertEmployerProfile_PersistsCompanyName()
    {
        await _factory.ResetDatabaseAsync();
        await BrokerFor(_employerToken).UpsertEmployerProfileAsync(ValidEmployerRequest("ACME SNow Ltd"));

        var get  = await BrokerFor(_employerToken).GetMyEmployerProfileAsync();
        var body = await get.Content.ReadFromJsonAsync<EmployerProfileResponse>(_json);
        body!.CompanyName.Should().Be("ACME SNow Ltd");
    }

    [Fact]
    public async Task UpsertEmployerProfile_CandidateRole_Returns403()
    {
        var response = await BrokerFor(_candidateToken).UpsertEmployerProfileAsync(ValidEmployerRequest());
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task UpsertEmployerProfile_InvalidCompanySize_Returns400()
    {
        var bad = ValidEmployerRequest() with { CompanySize = "999-huge" };
        var response = await BrokerFor(_employerToken).UpsertEmployerProfileAsync(bad);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetMyEmployerProfile_NotCreatedYet_Returns404()
    {
        await _factory.ResetDatabaseAsync();
        var response = await BrokerFor(_employerToken).GetMyEmployerProfileAsync();
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetEmployerProfile_PublicAccess_Returns200()
    {
        await _factory.ResetDatabaseAsync();
        await BrokerFor(_employerToken).UpsertEmployerProfileAsync(ValidEmployerRequest());

        var response = await AnonBroker().GetEmployerProfileAsync(_employerId);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task UpsertEmployerProfile_SecondCall_UpdatesWithoutDuplicate()
    {
        await _factory.ResetDatabaseAsync();
        await BrokerFor(_employerToken).UpsertEmployerProfileAsync(ValidEmployerRequest("First Corp"));
        await BrokerFor(_employerToken).UpsertEmployerProfileAsync(ValidEmployerRequest("Second Corp"));

        var get  = await BrokerFor(_employerToken).GetMyEmployerProfileAsync();
        var body = await get.Content.ReadFromJsonAsync<EmployerProfileResponse>(_json);
        body!.CompanyName.Should().Be("Second Corp");
    }
}

// ════════════════════════════════════════════════════════════════════════════════
// File Uploads — Profile Picture
// ════════════════════════════════════════════════════════════════════════════════

public sealed partial class ProfilesApiTests
{
    // 1x1 white JPEG — minimal valid image bytes
    private static readonly byte[] _validJpegBytes = Convert.FromBase64String(
        "/9j/4AAQSkZJRgABAQEASABIAAD/2wBDAAgGBgcGBQgHBwcJCQgKDBQNDAsLDBkSEw8U" +
        "HRofHh0aHBwgJC4nICIsIxwcKDcpLDAxNDQ0Hyc5PTgyPC4zNDL/2wBDAQkJCQwLDBgN" +
        "DRgyIRwhMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIy" +
        "MjL/wAARCAABAAEDASIAAhEBAxEB/8QAFAABAAAAAAAAAAAAAAAAAAAACf/EABQQAQAAAAAA" +
        "AAAAAAAAAAAAAP/EABQBAQAAAAAAAAAAAAAAAAAAAAD/xAAUEQEAAAAAAAAAAAAAAAAAAAAA" +
        "/9oADAMBAAIRAxEAPwCwABmX/9k=");

    private static readonly byte[] _validPdfBytes =
        System.Text.Encoding.ASCII.GetBytes(
            "%PDF-1.4\n1 0 obj<</Type/Catalog/Pages 2 0 R>>endobj\n" +
            "2 0 obj<</Type/Pages/Kids[3 0 R]/Count 1>>endobj\n" +
            "3 0 obj<</Type/Page/MediaBox[0 0 612 792]/Parent 2 0 R>>endobj\n" +
            "xref\n0 4\n0000000000 65535 f\n0000000009 00000 n\n" +
            "0000000058 00000 n\n0000000115 00000 n\n" +
            "trailer<</Size 4/Root 1 0 R>>\nstartxref\n190\n%%EOF");

    [Fact]
    public async Task UploadProfilePicture_ValidJpeg_Returns200WithUrl()
    {
        await _factory.ResetDatabaseAsync();
        var response = await BrokerFor(_candidateToken)
            .UploadProfilePictureAsync(_validJpegBytes, "photo.jpg", "image/jpeg");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<UploadedFileResponse>(_json);
        body!.Url.Should().StartWith("https://");
        body.Url.Should().Contain("profile-pictures");
    }

    [Fact]
    public async Task UploadProfilePicture_ValidPng_Returns200()
    {
        await _factory.ResetDatabaseAsync();
        // PNG magic bytes: 89 50 4E 47
        var pngBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        var response = await BrokerFor(_candidateToken)
            .UploadProfilePictureAsync(pngBytes, "photo.png", "image/png");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task UploadProfilePicture_WrongContentType_Returns415()
    {
        var response = await BrokerFor(_candidateToken)
            .UploadProfilePictureAsync(_validPdfBytes, "cv.pdf", "application/pdf");

        response.StatusCode.Should().Be(HttpStatusCode.UnsupportedMediaType);
    }

    [Fact]
    public async Task UploadProfilePicture_FileTooLarge_Returns413()
    {
        // 6MB — over the 5MB limit
        var bigFile  = new byte[6 * 1024 * 1024];
        var response = await BrokerFor(_candidateToken)
            .UploadProfilePictureAsync(bigFile, "huge.jpg", "image/jpeg");

        response.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
    }

    [Fact]
    public async Task UploadProfilePicture_NoAuth_Returns401()
    {
        var response = await AnonBroker()
            .UploadProfilePictureAsync(_validJpegBytes, "photo.jpg", "image/jpeg");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task UploadProfilePicture_UrlPersistedOnProfile()
    {
        await _factory.ResetDatabaseAsync();
        await BrokerFor(_candidateToken).UpsertCandidateProfileAsync(ValidCandidateRequest());
        await BrokerFor(_candidateToken)
            .UploadProfilePictureAsync(_validJpegBytes, "photo.jpg", "image/jpeg");

        var profile = await BrokerFor(_candidateToken).GetMyCandidateProfileAsync();
        var body    = await profile.Content.ReadFromJsonAsync<CandidateProfileResponse>(_json);
        body!.ProfilePictureUrl.Should().StartWith("https://");
    }
}

// ════════════════════════════════════════════════════════════════════════════════
// File Uploads — CV
// ════════════════════════════════════════════════════════════════════════════════

public sealed partial class ProfilesApiTests
{
    [Fact]
    public async Task UploadCv_ValidPdf_Returns200WithUrl()
    {
        await _factory.ResetDatabaseAsync();
        var response = await BrokerFor(_candidateToken)
            .UploadCvAsync(_validPdfBytes, "cv.pdf", "application/pdf");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<UploadedFileResponse>(_json);
        body!.Url.Should().StartWith("https://");
        body.Url.Should().Contain("cvs");
    }

    [Fact]
    public async Task UploadCv_NonPdf_Returns415()
    {
        var docxBytes = new byte[] { 0x50, 0x4B, 0x03, 0x04 }; // ZIP/DOCX magic bytes
        var response  = await BrokerFor(_candidateToken)
            .UploadCvAsync(docxBytes, "cv.docx",
                "application/vnd.openxmlformats-officedocument.wordprocessingml.document");

        response.StatusCode.Should().Be(HttpStatusCode.UnsupportedMediaType);
    }

    [Fact]
    public async Task UploadCv_FileTooLarge_Returns413()
    {
        // 11MB — over the 10MB limit
        var bigFile  = new byte[11 * 1024 * 1024];
        var response = await BrokerFor(_candidateToken)
            .UploadCvAsync(bigFile, "cv.pdf", "application/pdf");

        response.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
    }

    [Fact]
    public async Task UploadCv_NoAuth_Returns401()
    {
        var response = await AnonBroker()
            .UploadCvAsync(_validPdfBytes, "cv.pdf", "application/pdf");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task UploadCv_UrlPersistedOnProfile()
    {
        await _factory.ResetDatabaseAsync();
        await BrokerFor(_candidateToken).UpsertCandidateProfileAsync(ValidCandidateRequest());
        await BrokerFor(_candidateToken)
            .UploadCvAsync(_validPdfBytes, "cv.pdf", "application/pdf");

        var profile = await BrokerFor(_candidateToken).GetMyCandidateProfileAsync();
        var body    = await profile.Content.ReadFromJsonAsync<CandidateProfileResponse>(_json);
        body!.CvUrl.Should().StartWith("https://");
    }
}

// ════════════════════════════════════════════════════════════════════════════════

public sealed partial class ProfilesApiTests
{
    [Fact]
    public async Task Health_Returns200()
    {
        var response = await _factory.CreateClient().GetAsync("/health");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
