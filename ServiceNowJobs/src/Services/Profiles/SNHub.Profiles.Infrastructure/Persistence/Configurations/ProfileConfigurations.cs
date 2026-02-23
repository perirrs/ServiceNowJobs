using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SNHub.Profiles.Domain.Entities;
namespace SNHub.Profiles.Infrastructure.Persistence.Configurations;
public sealed class CandidateProfileConfiguration : IEntityTypeConfiguration<CandidateProfile>
{
    public void Configure(EntityTypeBuilder<CandidateProfile> b)
    {
        b.ToTable("candidate_profiles", "profiles");
        b.HasKey(p => p.Id); b.Property(p => p.Id).HasColumnName("id");
        b.Property(p => p.UserId).HasColumnName("user_id");
        b.Property(p => p.Headline).HasColumnName("headline").HasMaxLength(200);
        b.Property(p => p.Bio).HasColumnName("bio").HasMaxLength(3000);
        b.Property(p => p.ExperienceLevel).HasColumnName("experience_level");
        b.Property(p => p.YearsOfExperience).HasColumnName("years_of_experience").HasDefaultValue(0);
        b.Property(p => p.Availability).HasColumnName("availability");
        b.Property(p => p.CurrentRole).HasColumnName("current_role").HasMaxLength(200);
        b.Property(p => p.DesiredRole).HasColumnName("desired_role").HasMaxLength(200);
        b.Property(p => p.Location).HasColumnName("location").HasMaxLength(200);
        b.Property(p => p.Country).HasColumnName("country").HasMaxLength(3);
        b.Property(p => p.TimeZone).HasColumnName("time_zone").HasMaxLength(100);
        b.Property(p => p.ProfilePictureUrl).HasColumnName("profile_picture_url").HasMaxLength(2048);
        b.Property(p => p.CvUrl).HasColumnName("cv_url").HasMaxLength(2048);
        b.Property(p => p.LinkedInUrl).HasColumnName("linkedin_url").HasMaxLength(500);
        b.Property(p => p.GitHubUrl).HasColumnName("github_url").HasMaxLength(500);
        b.Property(p => p.WebsiteUrl).HasColumnName("website_url").HasMaxLength(500);
        b.Property(p => p.IsPublic).HasColumnName("is_public").HasDefaultValue(true);
        b.Property(p => p.DesiredSalaryMin).HasColumnName("desired_salary_min").HasColumnType("decimal(12,2)");
        b.Property(p => p.DesiredSalaryMax).HasColumnName("desired_salary_max").HasColumnType("decimal(12,2)");
        b.Property(p => p.SalaryCurrency).HasColumnName("salary_currency").HasMaxLength(3).HasDefaultValue("USD");
        b.Property(p => p.OpenToRemote).HasColumnName("open_to_remote").HasDefaultValue(false);
        b.Property(p => p.OpenToRelocation).HasColumnName("open_to_relocation").HasDefaultValue(false);
        b.Property(p => p.SkillsJson).HasColumnName("skills").HasColumnType("jsonb").HasDefaultValue("[]");
        b.Property(p => p.CertificationsJson).HasColumnName("certifications").HasColumnType("jsonb").HasDefaultValue("[]");
        b.Property(p => p.ServiceNowVersionsJson).HasColumnName("servicenow_versions").HasColumnType("jsonb").HasDefaultValue("[]");
        b.Property(p => p.ProfileCompleteness).HasColumnName("profile_completeness").HasDefaultValue(0);
        b.Property(p => p.CreatedAt).HasColumnName("created_at");
        b.Property(p => p.UpdatedAt).HasColumnName("updated_at");
        b.HasIndex(p => p.UserId).IsUnique().HasDatabaseName("ix_candidate_profiles_user");
    }
}
public sealed class EmployerProfileConfiguration : IEntityTypeConfiguration<EmployerProfile>
{
    public void Configure(EntityTypeBuilder<EmployerProfile> b)
    {
        b.ToTable("employer_profiles", "profiles");
        b.HasKey(p => p.Id); b.Property(p => p.Id).HasColumnName("id");
        b.Property(p => p.UserId).HasColumnName("user_id");
        b.Property(p => p.CompanyName).HasColumnName("company_name").HasMaxLength(200);
        b.Property(p => p.CompanyDescription).HasColumnName("company_description").HasMaxLength(5000);
        b.Property(p => p.Industry).HasColumnName("industry").HasMaxLength(100);
        b.Property(p => p.CompanySize).HasColumnName("company_size").HasMaxLength(20);
        b.Property(p => p.HeadquartersCity).HasColumnName("headquarters_city").HasMaxLength(100);
        b.Property(p => p.HeadquartersCountry).HasColumnName("headquarters_country").HasMaxLength(3);
        b.Property(p => p.WebsiteUrl).HasColumnName("website_url").HasMaxLength(500);
        b.Property(p => p.LinkedInUrl).HasColumnName("linkedin_url").HasMaxLength(500);
        b.Property(p => p.LogoUrl).HasColumnName("logo_url").HasMaxLength(2048);
        b.Property(p => p.IsVerified).HasColumnName("is_verified").HasDefaultValue(false);
        b.Property(p => p.CreatedAt).HasColumnName("created_at");
        b.Property(p => p.UpdatedAt).HasColumnName("updated_at");
        b.HasIndex(p => p.UserId).IsUnique().HasDatabaseName("ix_employer_profiles_user");
    }
}
