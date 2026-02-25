using SNHub.CvParser.Application.Interfaces;

namespace SNHub.CvParser.Infrastructure.Services;

/// <summary>
/// Deterministic CV parser for integration tests and local development.
/// Returns a realistic ServiceNow candidate profile without calling Azure OpenAI.
/// </summary>
public sealed class StubCvParserService : ICvParserService
{
    public Task<ParsedCvData> ParseAsync(
        Stream content, string contentType, CancellationToken ct = default)
    {
        var data = new ParsedCvData
        {
            FirstName         = "Jane",
            LastName          = "Smith",
            Email             = "jane.smith@example.com",
            Phone             = "+44 7700 900123",
            Location          = "London, UK",
            Headline          = "Senior ServiceNow Developer | ITSM & HRSD Specialist",
            Summary           = "8+ years building ServiceNow solutions across ITSM, HRSD, and CSM. Certified System Administrator and CIS-ITSM.",
            CurrentRole       = "Senior ServiceNow Developer",
            YearsOfExperience = 8,
            LinkedInUrl       = "https://linkedin.com/in/jane-smith",
            GitHubUrl         = null,
            Skills            = ["ITSM", "HRSD", "CSM", "Flow Designer", "Integration Hub",
                                  "Service Portal", "Scripting", "REST API", "CMDB", "ATF"],
            Certifications    =
            [
                new ExtractedCertification { Type = "CSA", Name = "Certified System Administrator", Year = 2020, Confidence = 95 },
                new ExtractedCertification { Type = "CIS", Name = "CIS-ITSM", Year = 2021, Confidence = 92 },
            ],
            ServiceNowVersions = ["Xanadu", "Washington", "Vancouver"],
            OverallConfidence  = 88,
            FieldConfidences   = new Dictionary<string, int>
            {
                ["firstName"]         = 95, ["lastName"]          = 95,
                ["email"]             = 98, ["phone"]             = 90,
                ["location"]          = 85, ["headline"]          = 80,
                ["summary"]           = 82, ["currentRole"]       = 92,
                ["yearsOfExperience"] = 75, ["linkedInUrl"]       = 95,
                ["gitHubUrl"]         = 0,  ["skills"]            = 90,
                ["certifications"]    = 93, ["serviceNowVersions"]= 85
            }
        };

        return Task.FromResult(data);
    }
}
