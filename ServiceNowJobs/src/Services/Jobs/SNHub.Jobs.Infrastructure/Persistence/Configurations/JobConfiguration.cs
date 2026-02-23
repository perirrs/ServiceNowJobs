using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SNHub.Jobs.Domain.Entities;

namespace SNHub.Jobs.Infrastructure.Persistence.Configurations;

public sealed class JobConfiguration : IEntityTypeConfiguration<Job>
{
    public void Configure(EntityTypeBuilder<Job> builder)
    {
        builder.ToTable("jobs", "jobs");
        builder.HasKey(j => j.Id);
        builder.Property(j => j.Id).HasColumnName("id");
        builder.Property(j => j.EmployerId).HasColumnName("employer_id").IsRequired();
        builder.Property(j => j.Title).HasColumnName("title").HasMaxLength(200).IsRequired();
        builder.Property(j => j.Description).HasColumnName("description").HasColumnType("text").IsRequired();
        builder.Property(j => j.Requirements).HasColumnName("requirements").HasColumnType("text");
        builder.Property(j => j.Benefits).HasColumnName("benefits").HasColumnType("text");
        builder.Property(j => j.CompanyName).HasColumnName("company_name").HasMaxLength(200);
        builder.Property(j => j.CompanyLogoUrl).HasColumnName("company_logo_url").HasMaxLength(2048);
        builder.Property(j => j.Location).HasColumnName("location").HasMaxLength(200);
        builder.Property(j => j.Country).HasColumnName("country").HasMaxLength(3);
        builder.Property(j => j.JobType).HasColumnName("job_type");
        builder.Property(j => j.WorkMode).HasColumnName("work_mode");
        builder.Property(j => j.ExperienceLevel).HasColumnName("experience_level");
        builder.Property(j => j.Status).HasColumnName("status");
        builder.Property(j => j.SalaryMin).HasColumnName("salary_min").HasColumnType("decimal(12,2)");
        builder.Property(j => j.SalaryMax).HasColumnName("salary_max").HasColumnType("decimal(12,2)");
        builder.Property(j => j.SalaryCurrency).HasColumnName("salary_currency").HasMaxLength(3).HasDefaultValue("USD");
        builder.Property(j => j.IsSalaryVisible).HasColumnName("is_salary_visible").HasDefaultValue(false);
        builder.Property(j => j.SkillsRequired).HasColumnName("skills_required").HasColumnType("jsonb").HasDefaultValue("[]");
        builder.Property(j => j.ServiceNowVersions).HasColumnName("servicenow_versions").HasColumnType("jsonb");
        builder.Property(j => j.CertificationsRequired).HasColumnName("certifications_required").HasColumnType("jsonb");
        builder.Property(j => j.ApplicationCount).HasColumnName("application_count").HasDefaultValue(0);
        builder.Property(j => j.ViewCount).HasColumnName("view_count").HasDefaultValue(0);
        builder.Property(j => j.ExpiresAt).HasColumnName("expires_at");
        builder.Property(j => j.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(j => j.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.Ignore(j => j.IsActive);
        builder.HasIndex(j => j.EmployerId).HasDatabaseName("ix_jobs_employer");
        builder.HasIndex(j => j.Status).HasDatabaseName("ix_jobs_status");
        builder.HasIndex(j => new { j.Status, j.CreatedAt }).HasDatabaseName("ix_jobs_status_created");
        builder.HasIndex(j => j.Country).HasDatabaseName("ix_jobs_country");
    }
}
