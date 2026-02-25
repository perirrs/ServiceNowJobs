using System.Text;
using System.Text.Json;
using Azure.AI.OpenAI;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OpenAI.Chat;
using SNHub.CvParser.Application.Interfaces;

namespace SNHub.CvParser.Infrastructure.Services;

/// <summary>
/// Uses Azure OpenAI GPT-4o to extract structured data from a CV document.
/// Text is extracted from PDF (iText7) or DOCX (OpenXml) first, then sent
/// as plain text to GPT-4o with a structured JSON extraction prompt.
/// </summary>
public sealed class AzureOpenAiCvParserService : ICvParserService
{
    private readonly ChatClient _chat;
    private readonly ILogger<AzureOpenAiCvParserService> _logger;

    private static readonly JsonSerializerOptions _jsonOpts =
        new() { PropertyNameCaseInsensitive = true };

    private const string SystemPrompt = """
        You are a CV/resume parser specialising in the ServiceNow ecosystem.

        Extract ALL of the following fields from the provided CV text and return ONLY a valid JSON object.
        Do not include any explanation, markdown code fences, or text outside the JSON.

        Return this exact JSON structure:
        {
          "firstName": "string or null",
          "lastName": "string or null",
          "email": "string or null",
          "phone": "string or null",
          "location": "city, country or null",
          "headline": "professional headline (max 200 chars) or null",
          "summary": "professional summary (max 1000 chars) or null",
          "currentRole": "most recent job title or null",
          "yearsOfExperience": number or null,
          "linkedInUrl": "full URL or null",
          "gitHubUrl": "full URL or null",
          "skills": ["array of ServiceNow and technical skills found"],
          "certifications": [
            {
              "type": "CSA|CAD|CIS|CMA|CSM|CSD|Other",
              "name": "full certification name",
              "year": number or null,
              "confidence": 0-100
            }
          ],
          "serviceNowVersions": ["array of ServiceNow versions e.g. Xanadu, Washington, Vancouver"],
          "overallConfidence": 0-100,
          "fieldConfidences": {
            "firstName": 0-100, "lastName": 0-100, "email": 0-100, "phone": 0-100,
            "location": 0-100, "headline": 0-100, "summary": 0-100, "currentRole": 0-100,
            "yearsOfExperience": 0-100, "linkedInUrl": 0-100, "gitHubUrl": 0-100,
            "skills": 0-100, "certifications": 0-100, "serviceNowVersions": 0-100
          }
        }

        ServiceNow skills: ITSM, HRSD, CSM, FSM, SecOps, ITOM, ITBM, GRC, SPM,
        Creator Workflows, Flow Designer, Integration Hub, Service Portal, Now Platform,
        Scripting, Business Rules, Client Scripts, UI Policies, REST API, GraphQL, ATF,
        Performance Analytics, CMDB, Discovery, Service Mapping, MID Server.

        Confidence: 95-100=directly in CV, 70-94=high inference, 50-69=medium, <50=guess, 0=not found.
        """;

    public AzureOpenAiCvParserService(
        AzureOpenAIClient client, IConfiguration config,
        ILogger<AzureOpenAiCvParserService> logger)
    {
        var deployment = config["AzureOpenAI:DeploymentName"] ?? "gpt-4o";
        _chat   = client.GetChatClient(deployment);
        _logger = logger;
    }

    public async Task<ParsedCvData> ParseAsync(
        Stream content, string contentType, CancellationToken ct = default)
    {
        var text = await ExtractTextAsync(content, contentType);
        if (string.IsNullOrWhiteSpace(text))
            throw new InvalidOperationException(
                "Could not extract any text from the uploaded document.");

        _logger.LogInformation(
            "Extracted {Chars} chars from CV, sending to GPT-4o", text.Length);

        var messages = new List<ChatMessage>
        {
            new SystemChatMessage(SystemPrompt),
            new UserChatMessage(
                $"Please parse the following CV text and return structured JSON:\n\n{text}")
        };

        var options = new ChatCompletionOptions
        {
            Temperature         = 0f,
            MaxOutputTokenCount = 2000
        };

        var response = await _chat.CompleteChatAsync(messages, options, ct);
        var json     = response.Value.Content[0].Text.Trim();

        // Strip markdown fences if present
        if (json.StartsWith("```"))
        {
            var lines = json.Split('\n');
            json = string.Join('\n',
                lines.Skip(1).TakeWhile(l => !l.StartsWith("```")));
        }

        var extracted = JsonSerializer.Deserialize<GptExtractionResult>(json, _jsonOpts)
            ?? throw new InvalidOperationException("GPT-4o returned unparseable JSON.");

        return MapToData(extracted);
    }

    // ── Text extraction ───────────────────────────────────────────────────────

    private static async Task<string> ExtractTextAsync(Stream content, string contentType)
    {
        using var ms = new MemoryStream();
        await content.CopyToAsync(ms);
        ms.Position = 0;
        return contentType switch
        {
            "application/pdf" => ExtractPdfText(ms),
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document"
                              => ExtractDocxText(ms),
            _                 => throw new InvalidOperationException(
                                     $"Unsupported content type: {contentType}")
        };
    }

    private static string ExtractPdfText(MemoryStream stream)
    {
        var sb = new StringBuilder();
        using var reader = new PdfReader(stream);
        using var pdf    = new PdfDocument(reader);
        for (int i = 1; i <= pdf.GetNumberOfPages(); i++)
            sb.AppendLine(PdfTextExtractor.GetTextFromPage(pdf.GetPage(i)));
        return sb.ToString();
    }

    private static string ExtractDocxText(MemoryStream stream)
    {
        var sb = new StringBuilder();
        using var doc  = WordprocessingDocument.Open(stream, false);
        var body = doc.MainDocumentPart?.Document?.Body;
        if (body is null) return string.Empty;
        foreach (var para in body.Descendants<Paragraph>())
            sb.AppendLine(para.InnerText);
        return sb.ToString();
    }

    // ── Mapping ───────────────────────────────────────────────────────────────

    private static ParsedCvData MapToData(GptExtractionResult r) => new()
    {
        FirstName         = r.FirstName,
        LastName          = r.LastName,
        Email             = r.Email,
        Phone             = r.Phone,
        Location          = r.Location,
        Headline          = r.Headline,
        Summary           = r.Summary,
        CurrentRole       = r.CurrentRole,
        YearsOfExperience = r.YearsOfExperience,
        LinkedInUrl       = r.LinkedInUrl,
        GitHubUrl         = r.GitHubUrl,
        Skills            = r.Skills ?? [],
        Certifications    = (r.Certifications ?? []).Select(c => new ExtractedCertification
        {
            Type = c.Type ?? "Other", Name = c.Name ?? string.Empty,
            Year = c.Year, Confidence = c.Confidence
        }).ToList(),
        ServiceNowVersions = r.ServiceNowVersions ?? [],
        OverallConfidence  = r.OverallConfidence,
        FieldConfidences   = r.FieldConfidences ?? []
    };

    private sealed class GptExtractionResult
    {
        public string? FirstName         { get; set; }
        public string? LastName          { get; set; }
        public string? Email             { get; set; }
        public string? Phone             { get; set; }
        public string? Location          { get; set; }
        public string? Headline          { get; set; }
        public string? Summary           { get; set; }
        public string? CurrentRole       { get; set; }
        public int?    YearsOfExperience { get; set; }
        public string? LinkedInUrl       { get; set; }
        public string? GitHubUrl         { get; set; }
        public List<string>? Skills      { get; set; }
        public List<GptCert>? Certifications { get; set; }
        public List<string>? ServiceNowVersions { get; set; }
        public int     OverallConfidence { get; set; }
        public Dictionary<string, int>? FieldConfidences { get; set; }
    }

    private sealed class GptCert
    {
        public string? Type { get; set; }
        public string? Name { get; set; }
        public int?    Year { get; set; }
        public int     Confidence { get; set; }
    }
}
