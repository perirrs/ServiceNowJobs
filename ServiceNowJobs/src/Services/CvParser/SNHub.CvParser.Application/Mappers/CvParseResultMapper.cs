using System.Text.Json;
using SNHub.CvParser.Application.DTOs;
using SNHub.CvParser.Domain.Entities;

namespace SNHub.CvParser.Application.Mappers;

public static class CvParseResultMapper
{
    private static readonly JsonSerializerOptions _opts =
        new() { PropertyNameCaseInsensitive = true };

    public static CvParseResultDto ToDto(CvParseResult r) => new(
        Id:                r.Id,
        UserId:            r.UserId,
        OriginalFileName:  r.OriginalFileName,
        FileSizeBytes:     r.FileSizeBytes,
        Status:            r.Status.ToString(),
        ErrorMessage:      r.ErrorMessage,
        FirstName:         r.FirstName,
        LastName:          r.LastName,
        Email:             r.Email,
        Phone:             r.Phone,
        Location:          r.Location,
        Headline:          r.Headline,
        Summary:           r.Summary,
        CurrentRole:       r.CurrentRole,
        YearsOfExperience: r.YearsOfExperience,
        LinkedInUrl:       r.LinkedInUrl,
        GitHubUrl:         r.GitHubUrl,
        Skills:            DeserializeArray(r.SkillsJson),
        Certifications:    DeserializeCerts(r.CertificationsJson),
        ServiceNowVersions: DeserializeArray(r.ServiceNowVersionsJson),
        OverallConfidence: r.OverallConfidence,
        FieldConfidences:  DeserializeConfidences(r.FieldConfidencesJson),
        IsApplied:         r.IsApplied,
        AppliedAt:         r.AppliedAt,
        CreatedAt:         r.CreatedAt,
        UpdatedAt:         r.UpdatedAt);

    private static string[] DeserializeArray(string json)
    {
        try { return JsonSerializer.Deserialize<string[]>(json, _opts) ?? []; }
        catch { return []; }
    }

    private static CertificationDto[] DeserializeCerts(string json)
    {
        try { return JsonSerializer.Deserialize<CertificationDto[]>(json, _opts) ?? []; }
        catch { return []; }
    }

    private static Dictionary<string, int> DeserializeConfidences(string json)
    {
        try { return JsonSerializer.Deserialize<Dictionary<string, int>>(json, _opts) ?? []; }
        catch { return []; }
    }
}
