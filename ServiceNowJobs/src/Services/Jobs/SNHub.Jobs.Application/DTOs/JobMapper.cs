using SNHub.Jobs.Domain.Entities;

namespace SNHub.Jobs.Application.DTOs;

public static class JobMapper
{
    public static JobDto Map(Job j) => new(
        j.Id, j.EmployerId, j.Title, j.Description, j.Requirements, j.Benefits,
        j.CompanyName, j.CompanyLogoUrl, j.Location, j.Country,
        j.JobType, j.WorkMode, j.ExperienceLevel, j.Status,
        j.SalaryMin, j.SalaryMax, j.SalaryCurrency, j.IsSalaryVisible,
        j.SkillsRequired, j.ApplicationCount, j.ViewCount,
        j.ExpiresAt, j.CreatedAt, j.UpdatedAt);
}
