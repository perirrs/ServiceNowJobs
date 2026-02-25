using FluentAssertions;
using SNHub.CvParser.IntegrationTests.Brokers;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace SNHub.CvParser.IntegrationTests.Apis;

// ── Response models ────────────────────────────────────────────────────────────
file sealed class CvParseResultResponse
{
    public Guid    Id                { get; set; }
    public Guid    UserId            { get; set; }
    public string  OriginalFileName  { get; set; } = string.Empty;
    public long    FileSizeBytes     { get; set; }
    public string  Status            { get; set; } = string.Empty;
    public string? ErrorMessage      { get; set; }
    public string? FirstName         { get; set; }
    public string? LastName          { get; set; }
    public string? Email             { get; set; }
    public string? Headline          { get; set; }
    public string? CurrentRole       { get; set; }
    public int?    YearsOfExperience { get; set; }
    public string[] Skills           { get; set; } = [];
    public int     OverallConfidence { get; set; }
    public bool    IsApplied         { get; set; }
}

file sealed class ApplyResponse
{
    public Guid   ParseResultId { get; set; }
    public bool   Applied       { get; set; }
    public string Message       { get; set; } = string.Empty;
}

// ── Helpers ────────────────────────────────────────────────────────────────────
file static class Helpers
{
    private static readonly byte[] FakePdf = "%PDF-1.4 fake content for testing"u8.ToArray();
    private static readonly JsonSerializerOptions Json =
        new() { PropertyNameCaseInsensitive = true };

    public static MultipartFormDataContent MakePdfForm(string filename = "cv.pdf")
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(FakePdf);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(file, "file", filename);
        return form;
    }

    public static MultipartFormDataContent MakeDocxForm()
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(FakePdf); // content doesn't matter for stub
        file.Headers.ContentType = new MediaTypeHeaderValue(
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document");
        form.Add(file, "file", "cv.docx");
        return form;
    }

    public static async Task<T> ReadAs<T>(HttpContent content) =>
        (await content.ReadFromJsonAsync<T>(Json))!;
}

// ══════════════════════════════════════════════════════════════════════════════
// POST /api/v1/cv/parse
// ══════════════════════════════════════════════════════════════════════════════

[Collection(nameof(CvParserApiCollection))]
public sealed class ParseCvTests
{
    private readonly CvParserWebApplicationFactory _factory;
    private static readonly Guid   _userId = Guid.NewGuid();
    private static readonly string _token  =
        CvParserWebApplicationFactory.GenerateToken(_userId);

    public ParseCvTests(CvParserWebApplicationFactory factory) => _factory = factory;

    private HttpClient AuthClient()
    {
        var c = _factory.CreateClient();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        return c;
    }

    [Fact]
    public async Task ParseCv_ValidPdf_Returns200WithExtractedData()
    {
        await _factory.ResetDatabaseAsync();
        var response = await AuthClient().PostAsync("/api/v1/cv/parse", Helpers.MakePdfForm());
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await Helpers.ReadAs<CvParseResultResponse>(response.Content);
        body.Status.Should().Be("Completed");
        body.UserId.Should().Be(_userId);
        body.FirstName.Should().Be("Jane"); // from StubCvParserService
        body.LastName.Should().Be("Smith");
        body.YearsOfExperience.Should().Be(8);
        body.Skills.Should().Contain("ITSM");
        body.OverallConfidence.Should().Be(88);
        body.OriginalFileName.Should().Be("cv.pdf");
        body.IsApplied.Should().BeFalse();
    }

    [Fact]
    public async Task ParseCv_ValidDocx_Returns200()
    {
        await _factory.ResetDatabaseAsync();
        var response = await AuthClient().PostAsync("/api/v1/cv/parse", Helpers.MakeDocxForm());
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await Helpers.ReadAs<CvParseResultResponse>(response.Content);
        body.Status.Should().Be("Completed");
    }

    [Fact]
    public async Task ParseCv_NoFile_Returns400()
    {
        var response = await AuthClient().PostAsync("/api/v1/cv/parse",
            new MultipartFormDataContent());
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ParseCv_WrongContentType_Returns400()
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent([1, 2, 3]);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(file, "file", "photo.png");

        var response = await AuthClient().PostAsync("/api/v1/cv/parse", form);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ParseCv_NoAuth_Returns401()
    {
        var response = await _factory.CreateClient()
            .PostAsync("/api/v1/cv/parse", Helpers.MakePdfForm());
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ParseCv_ResultPersisted_CanBeRetrieved()
    {
        await _factory.ResetDatabaseAsync();
        var parseResp = await AuthClient().PostAsync("/api/v1/cv/parse", Helpers.MakePdfForm());
        var parsed    = await Helpers.ReadAs<CvParseResultResponse>(parseResp.Content);

        var getResp = await AuthClient().GetAsync($"/api/v1/cv/{parsed.Id}");
        getResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var retrieved = await Helpers.ReadAs<CvParseResultResponse>(getResp.Content);
        retrieved.Id.Should().Be(parsed.Id);
        retrieved.FirstName.Should().Be("Jane");
    }
}

// ══════════════════════════════════════════════════════════════════════════════
// GET /api/v1/cv/{id}
// ══════════════════════════════════════════════════════════════════════════════

[Collection(nameof(CvParserApiCollection))]
public sealed class GetParseResultTests
{
    private readonly CvParserWebApplicationFactory _factory;
    private static readonly Guid   _user1Id = Guid.NewGuid();
    private static readonly Guid   _user2Id = Guid.NewGuid();
    private static readonly string _user1Token =
        CvParserWebApplicationFactory.GenerateToken(_user1Id);
    private static readonly string _user2Token =
        CvParserWebApplicationFactory.GenerateToken(_user2Id);

    public GetParseResultTests(CvParserWebApplicationFactory factory) => _factory = factory;

    private HttpClient ClientFor(string token)
    {
        var c = _factory.CreateClient();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return c;
    }

    [Fact]
    public async Task GetParseResult_OwnResult_Returns200()
    {
        await _factory.ResetDatabaseAsync();
        var parse  = await ClientFor(_user1Token).PostAsync("/api/v1/cv/parse", Helpers.MakePdfForm());
        var parsed = await Helpers.ReadAs<CvParseResultResponse>(parse.Content);

        var response = await ClientFor(_user1Token).GetAsync($"/api/v1/cv/{parsed.Id}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetParseResult_OtherUserResult_Returns403()
    {
        await _factory.ResetDatabaseAsync();
        var parse  = await ClientFor(_user1Token).PostAsync("/api/v1/cv/parse", Helpers.MakePdfForm());
        var parsed = await Helpers.ReadAs<CvParseResultResponse>(parse.Content);

        var response = await ClientFor(_user2Token).GetAsync($"/api/v1/cv/{parsed.Id}");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetParseResult_NonExistent_Returns404()
    {
        var response = await ClientFor(_user1Token).GetAsync($"/api/v1/cv/{Guid.NewGuid()}");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetParseResult_NoAuth_Returns401()
    {
        var response = await _factory.CreateClient().GetAsync($"/api/v1/cv/{Guid.NewGuid()}");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}

// ══════════════════════════════════════════════════════════════════════════════
// GET /api/v1/cv/my-results
// ══════════════════════════════════════════════════════════════════════════════

[Collection(nameof(CvParserApiCollection))]
public sealed class GetMyParseResultsTests
{
    private readonly CvParserWebApplicationFactory _factory;
    private static readonly Guid   _userId = Guid.NewGuid();
    private static readonly string _token  =
        CvParserWebApplicationFactory.GenerateToken(_userId);

    public GetMyParseResultsTests(CvParserWebApplicationFactory factory) => _factory = factory;

    private HttpClient AuthClient()
    {
        var c = _factory.CreateClient();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        return c;
    }

    [Fact]
    public async Task GetMyResults_NoResults_ReturnsEmptyArray()
    {
        await _factory.ResetDatabaseAsync();
        var response = await AuthClient().GetAsync("/api/v1/cv/my-results");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<CvParseResultResponse[]>(
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        body!.Should().BeEmpty();
    }

    [Fact]
    public async Task GetMyResults_AfterTwoParsed_ReturnsBothNewestFirst()
    {
        await _factory.ResetDatabaseAsync();
        await AuthClient().PostAsync("/api/v1/cv/parse", Helpers.MakePdfForm("cv1.pdf"));
        await Task.Delay(10); // ensure ordering
        await AuthClient().PostAsync("/api/v1/cv/parse", Helpers.MakePdfForm("cv2.pdf"));

        var response = await AuthClient().GetAsync("/api/v1/cv/my-results");
        var body     = await response.Content.ReadFromJsonAsync<CvParseResultResponse[]>(
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        body!.Should().HaveCount(2);
        body![0].OriginalFileName.Should().Be("cv2.pdf"); // newest first
    }

    [Fact]
    public async Task GetMyResults_OnlyReturnsOwnResults()
    {
        await _factory.ResetDatabaseAsync();
        var otherToken = CvParserWebApplicationFactory.GenerateToken(Guid.NewGuid());
        var otherClient = _factory.CreateClient();
        otherClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", otherToken);

        await otherClient.PostAsync("/api/v1/cv/parse", Helpers.MakePdfForm());
        await AuthClient().PostAsync("/api/v1/cv/parse", Helpers.MakePdfForm());

        var response = await AuthClient().GetAsync("/api/v1/cv/my-results");
        var body     = await response.Content.ReadFromJsonAsync<CvParseResultResponse[]>(
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        body!.Should().HaveCount(1);
        body![0].UserId.Should().Be(_userId);
    }
}

// ══════════════════════════════════════════════════════════════════════════════
// POST /api/v1/cv/{id}/apply
// ══════════════════════════════════════════════════════════════════════════════

[Collection(nameof(CvParserApiCollection))]
public sealed class ApplyParsedCvTests
{
    private readonly CvParserWebApplicationFactory _factory;
    private static readonly Guid   _userId = Guid.NewGuid();
    private static readonly string _token  =
        CvParserWebApplicationFactory.GenerateToken(_userId);

    public ApplyParsedCvTests(CvParserWebApplicationFactory factory) => _factory = factory;

    private HttpClient AuthClient()
    {
        var c = _factory.CreateClient();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        return c;
    }

    [Fact]
    public async Task Apply_CompletedResult_Returns200AndMarksApplied()
    {
        await _factory.ResetDatabaseAsync();
        var parse  = await AuthClient().PostAsync("/api/v1/cv/parse", Helpers.MakePdfForm());
        var parsed = await Helpers.ReadAs<CvParseResultResponse>(parse.Content);

        var apply = await AuthClient().PostAsync($"/api/v1/cv/{parsed.Id}/apply", null);
        apply.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await Helpers.ReadAs<ApplyResponse>(apply.Content);
        body.Applied.Should().BeTrue();
        body.ParseResultId.Should().Be(parsed.Id);
    }

    [Fact]
    public async Task Apply_IsAppliedFlagSetOnResult()
    {
        await _factory.ResetDatabaseAsync();
        var parse  = await AuthClient().PostAsync("/api/v1/cv/parse", Helpers.MakePdfForm());
        var parsed = await Helpers.ReadAs<CvParseResultResponse>(parse.Content);
        await AuthClient().PostAsync($"/api/v1/cv/{parsed.Id}/apply", null);

        var get  = await AuthClient().GetAsync($"/api/v1/cv/{parsed.Id}");
        var body = await Helpers.ReadAs<CvParseResultResponse>(get.Content);
        body.IsApplied.Should().BeTrue();
    }

    [Fact]
    public async Task Apply_AlreadyApplied_Returns409()
    {
        await _factory.ResetDatabaseAsync();
        var parse  = await AuthClient().PostAsync("/api/v1/cv/parse", Helpers.MakePdfForm());
        var parsed = await Helpers.ReadAs<CvParseResultResponse>(parse.Content);

        await AuthClient().PostAsync($"/api/v1/cv/{parsed.Id}/apply", null);
        var second = await AuthClient().PostAsync($"/api/v1/cv/{parsed.Id}/apply", null);
        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Apply_OtherUserResult_Returns403()
    {
        await _factory.ResetDatabaseAsync();
        var parse  = await AuthClient().PostAsync("/api/v1/cv/parse", Helpers.MakePdfForm());
        var parsed = await Helpers.ReadAs<CvParseResultResponse>(parse.Content);

        var otherToken  = CvParserWebApplicationFactory.GenerateToken(Guid.NewGuid());
        var otherClient = _factory.CreateClient();
        otherClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", otherToken);

        var response = await otherClient.PostAsync($"/api/v1/cv/{parsed.Id}/apply", null);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Apply_NonExistent_Returns404()
    {
        var response = await AuthClient().PostAsync($"/api/v1/cv/{Guid.NewGuid()}/apply", null);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Apply_NoAuth_Returns401()
    {
        var response = await _factory.CreateClient()
            .PostAsync($"/api/v1/cv/{Guid.NewGuid()}/apply", null);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}

// ══════════════════════════════════════════════════════════════════════════════
// Health
// ══════════════════════════════════════════════════════════════════════════════

[Collection(nameof(CvParserApiCollection))]
public sealed class CvParserHealthTests
{
    private readonly CvParserWebApplicationFactory _factory;
    public CvParserHealthTests(CvParserWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Health_Returns200()
    {
        var response = await _factory.CreateClient().GetAsync("/health");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
