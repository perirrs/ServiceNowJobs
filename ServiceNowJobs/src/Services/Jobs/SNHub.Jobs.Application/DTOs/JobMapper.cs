using SNHub.Jobs.Domain.Entities;
using System.Text.Json;

namespace SNHub.Jobs.Application.DTOs;

public static class JobMapper
{
    private static readonly JsonSerializerOptions _jsonOpts = new() { PropertyNameCaseInsensitive = true };

    public static JobDto ToDto(Job j) => new(
        Id:                    j.Id,
        EmployerId:            j.EmployerId,
        Title:                 j.Title,
        Description:           j.Description,
        Requirements:          j.Requirements,
        Benefits:              j.Benefits,
        CompanyName:           j.CompanyName,
        CompanyLogoUrl:        j.CompanyLogoUrl,
        Location:              j.Location,
        Country:               j.Country,
        JobType:               j.JobType.ToString(),
        WorkMode:              j.WorkMode.ToString(),
        ExperienceLevel:       j.ExperienceLevel.ToString(),
        Status:                j.Status.ToString(),
        SalaryMin:             j.SalaryMin,
        SalaryMax:             j.SalaryMax,
        SalaryCurrency:        j.SalaryCurrency,
        IsSalaryVisible:       j.IsSalaryVisible,
        SkillsRequired:        DeserializeStringArray(j.SkillsRequired),
        CertificationsRequired: DeserializeStringArray(j.CertificationsRequired),
        ServiceNowVersions:    DeserializeStringArray(j.ServiceNowVersions),
        ApplicationCount:      j.ApplicationCount,
        ViewCount:             j.ViewCount,
        IsActive:              j.IsActive,
        ExpiresAt:             j.ExpiresAt,
        CreatedAt:             j.CreatedAt,
        UpdatedAt:             j.UpdatedAt);

    private static IEnumerable<string> DeserializeStringArray(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "[]") return [];
        try { return JsonSerializer.Deserialize<List<string>>(json, _jsonOpts) ?? []; }
        catch { return []; }
    }
}
