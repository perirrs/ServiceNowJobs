using SNHub.CvParser.Domain.Enums;

namespace SNHub.CvParser.Application.DTOs;

public sealed record CvParseResultDto(
    Guid         Id,
    Guid         UserId,
    string       OriginalFileName,
    long         FileSizeBytes,
    string       Status,
    string?      ErrorMessage,
    // Extracted fields
    string?      FirstName,
    string?      LastName,
    string?      Email,
    string?      Phone,
    string?      Location,
    string?      Headline,
    string?      Summary,
    string?      CurrentRole,
    int?         YearsOfExperience,
    string?      LinkedInUrl,
    string?      GitHubUrl,
    string[]     Skills,
    CertificationDto[] Certifications,
    string[]     ServiceNowVersions,
    // Confidence
    int          OverallConfidence,
    Dictionary<string, int> FieldConfidences,
    // State
    bool         IsApplied,
    DateTimeOffset?  AppliedAt,
    DateTimeOffset   CreatedAt,
    DateTimeOffset   UpdatedAt);

public sealed record CertificationDto(
    string Type,
    string Name,
    int?   Year,
    int    Confidence);

public sealed record ParseCvResponse(
    Guid   ParseResultId,
    string Status,
    string Message);

public sealed record ApplyParsedCvResponse(
    Guid   ParseResultId,
    bool   Applied,
    string Message);
