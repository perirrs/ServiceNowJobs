using SNHub.CvParser.Domain.Entities;

namespace SNHub.CvParser.Application.Interfaces;

public interface ICvParseResultRepository
{
    Task<CvParseResult?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IEnumerable<CvParseResult>> GetByUserIdAsync(Guid userId, CancellationToken ct = default);
    Task AddAsync(CvParseResult result, CancellationToken ct = default);
    Task<int> CountByUserIdAsync(Guid userId, CancellationToken ct = default);
}

public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}

public interface IBlobStorageService
{
    Task<string> UploadAsync(Stream content, string path, string contentType, CancellationToken ct = default);
    Task<Stream> DownloadAsync(string path, CancellationToken ct = default);
    Task DeleteAsync(string path, CancellationToken ct = default);
}

public interface ICvParserService
{
    /// <summary>
    /// Send a CV document to Azure OpenAI GPT-4o for structured extraction.
    /// Returns a ParsedCvData object with all extracted fields and confidence scores.
    /// </summary>
    Task<ParsedCvData> ParseAsync(Stream content, string contentType, CancellationToken ct = default);
}

public interface ICurrentUserService
{
    Guid? UserId { get; }
    bool IsAuthenticated { get; }
    bool IsInRole(string role);
}

/// <summary>Structured result from the AI extraction.</summary>
public sealed class ParsedCvData
{
    public string? FirstName             { get; set; }
    public string? LastName              { get; set; }
    public string? Email                 { get; set; }
    public string? Phone                 { get; set; }
    public string? Location              { get; set; }
    public string? Headline              { get; set; }
    public string? Summary               { get; set; }
    public string? CurrentRole           { get; set; }
    public int?    YearsOfExperience     { get; set; }
    public string? LinkedInUrl           { get; set; }
    public string? GitHubUrl             { get; set; }
    public List<string> Skills           { get; set; } = [];
    public List<ExtractedCertification> Certifications { get; set; } = [];
    public List<string> ServiceNowVersions { get; set; } = [];
    public int OverallConfidence         { get; set; }
    public Dictionary<string, int> FieldConfidences { get; set; } = [];
}

public sealed class ExtractedCertification
{
    public string Type       { get; set; } = "Other";
    public string Name       { get; set; } = string.Empty;
    public int?   Year       { get; set; }
    public int    Confidence { get; set; }
}
