using SNHub.Profiles.Application.DTOs;
using SNHub.Profiles.Domain.Entities;
using System.Text.Json;

namespace SNHub.Profiles.Application.DTOs;

public static class ProfileMapper
{
    private static readonly JsonSerializerOptions _json = new() { PropertyNameCaseInsensitive = true };

    public static CandidateProfileDto ToDto(CandidateProfile p) => new(
        Id:                p.Id,
        UserId:            p.UserId,
        Headline:          p.Headline,
        Bio:               p.Bio,
        ExperienceLevel:   p.ExperienceLevel.ToString(),
        YearsOfExperience: p.YearsOfExperience,
        Availability:      p.Availability.ToString(),
        CurrentRole:       p.CurrentRole,
        DesiredRole:       p.DesiredRole,
        Location:          p.Location,
        Country:           p.Country,
        TimeZone:          p.TimeZone,
        ProfilePictureUrl: p.ProfilePictureUrl,
        CvUrl:             p.CvUrl,
        LinkedInUrl:       p.LinkedInUrl,
        GitHubUrl:         p.GitHubUrl,
        WebsiteUrl:        p.WebsiteUrl,
        IsPublic:          p.IsPublic,
        DesiredSalaryMin:  p.DesiredSalaryMin,
        DesiredSalaryMax:  p.DesiredSalaryMax,
        SalaryCurrency:    p.SalaryCurrency,
        OpenToRemote:      p.OpenToRemote,
        OpenToRelocation:  p.OpenToRelocation,
        Skills:            DeserializeStrings(p.SkillsJson),
        Certifications:    DeserializeObjects(p.CertificationsJson),
        ServiceNowVersions: DeserializeStrings(p.ServiceNowVersionsJson),
        ProfileCompleteness: p.ProfileCompleteness,
        CreatedAt:         p.CreatedAt,
        UpdatedAt:         p.UpdatedAt);

    public static EmployerProfileDto ToDto(EmployerProfile p) => new(
        Id:                   p.Id,
        UserId:               p.UserId,
        CompanyName:          p.CompanyName,
        CompanyDescription:   p.CompanyDescription,
        Industry:             p.Industry,
        CompanySize:          p.CompanySize,
        HeadquartersCity:     p.HeadquartersCity,
        HeadquartersCountry:  p.HeadquartersCountry,
        WebsiteUrl:           p.WebsiteUrl,
        LinkedInUrl:          p.LinkedInUrl,
        LogoUrl:              p.LogoUrl,
        IsVerified:           p.IsVerified,
        CreatedAt:            p.CreatedAt,
        UpdatedAt:            p.UpdatedAt);

    private static IEnumerable<string> DeserializeStrings(string json)
    {
        try { return JsonSerializer.Deserialize<List<string>>(json, _json) ?? []; }
        catch { return []; }
    }

    private static IEnumerable<object> DeserializeObjects(string json)
    {
        try { return JsonSerializer.Deserialize<List<object>>(json, _json) ?? []; }
        catch { return []; }
    }
}
