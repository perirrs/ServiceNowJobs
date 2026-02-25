using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SNHub.CvParser.Domain.Entities;

namespace SNHub.CvParser.Infrastructure.Persistence.Configurations;

public sealed class CvParseResultConfiguration : IEntityTypeConfiguration<CvParseResult>
{
    public void Configure(EntityTypeBuilder<CvParseResult> b)
    {
        b.ToTable("cv_parse_results", "cvparser");
        b.HasKey(r => r.Id);

        b.Property(r => r.Id).HasColumnName("id");
        b.Property(r => r.UserId).HasColumnName("user_id").IsRequired();
        b.Property(r => r.BlobPath).HasColumnName("blob_path").HasMaxLength(1024).IsRequired();
        b.Property(r => r.OriginalFileName).HasColumnName("original_file_name").HasMaxLength(255).IsRequired();
        b.Property(r => r.ContentType).HasColumnName("content_type").HasMaxLength(100).IsRequired();
        b.Property(r => r.FileSizeBytes).HasColumnName("file_size_bytes").IsRequired();
        b.Property(r => r.Status).HasColumnName("status").IsRequired();
        b.Property(r => r.ErrorMessage).HasColumnName("error_message").HasMaxLength(2000);

        b.Property(r => r.FirstName).HasColumnName("first_name").HasMaxLength(100);
        b.Property(r => r.LastName).HasColumnName("last_name").HasMaxLength(100);
        b.Property(r => r.Email).HasColumnName("email").HasMaxLength(320);
        b.Property(r => r.Phone).HasColumnName("phone").HasMaxLength(50);
        b.Property(r => r.Location).HasColumnName("location").HasMaxLength(200);
        b.Property(r => r.Headline).HasColumnName("headline").HasMaxLength(200);
        b.Property(r => r.Summary).HasColumnName("summary").HasMaxLength(2000);
        b.Property(r => r.CurrentRole).HasColumnName("current_role").HasMaxLength(200);
        b.Property(r => r.YearsOfExperience).HasColumnName("years_of_experience");
        b.Property(r => r.LinkedInUrl).HasColumnName("linkedin_url").HasMaxLength(500);
        b.Property(r => r.GitHubUrl).HasColumnName("github_url").HasMaxLength(500);

        b.Property(r => r.SkillsJson).HasColumnName("skills_json")
            .HasColumnType("jsonb").IsRequired().HasDefaultValue("[]");
        b.Property(r => r.CertificationsJson).HasColumnName("certifications_json")
            .HasColumnType("jsonb").IsRequired().HasDefaultValue("[]");
        b.Property(r => r.ServiceNowVersionsJson).HasColumnName("servicenow_versions_json")
            .HasColumnType("jsonb").IsRequired().HasDefaultValue("[]");
        b.Property(r => r.FieldConfidencesJson).HasColumnName("field_confidences_json")
            .HasColumnType("jsonb").IsRequired().HasDefaultValue("{}");

        b.Property(r => r.OverallConfidence).HasColumnName("overall_confidence").HasDefaultValue(0);
        b.Property(r => r.IsApplied).HasColumnName("is_applied").HasDefaultValue(false);
        b.Property(r => r.AppliedAt).HasColumnName("applied_at");
        b.Property(r => r.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(r => r.UpdatedAt).HasColumnName("updated_at").IsRequired();

        b.HasIndex(r => r.UserId).HasDatabaseName("ix_cv_parse_results_user_id");
        b.HasIndex(r => r.Status).HasDatabaseName("ix_cv_parse_results_status");
        b.HasIndex(r => r.CreatedAt).HasDatabaseName("ix_cv_parse_results_created_at");
    }
}
