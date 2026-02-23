using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SNHub.Applications.Domain.Entities;

namespace SNHub.Applications.Infrastructure.Persistence.Configurations;

public sealed class JobApplicationConfiguration : IEntityTypeConfiguration<JobApplication>
{
    public void Configure(EntityTypeBuilder<JobApplication> builder)
    {
        builder.ToTable("applications", "applications");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).HasColumnName("id");
        builder.Property(a => a.JobId).HasColumnName("job_id");
        builder.Property(a => a.CandidateId).HasColumnName("candidate_id");
        builder.Property(a => a.Status).HasColumnName("status");
        builder.Property(a => a.CoverLetter).HasColumnName("cover_letter").HasMaxLength(5000);
        builder.Property(a => a.CvUrl).HasColumnName("cv_url").HasMaxLength(2048);
        builder.Property(a => a.EmployerNotes).HasColumnName("employer_notes").HasMaxLength(2000);
        builder.Property(a => a.RejectionReason).HasColumnName("rejection_reason").HasMaxLength(1000);
        builder.Property(a => a.AppliedAt).HasColumnName("applied_at");
        builder.Property(a => a.UpdatedAt).HasColumnName("updated_at");
        builder.Property(a => a.StatusChangedAt).HasColumnName("status_changed_at");
        builder.Ignore(a => a.IsActive);
        builder.HasIndex(a => new { a.JobId, a.CandidateId }).IsUnique().HasDatabaseName("ix_applications_job_candidate");
        builder.HasIndex(a => a.CandidateId).HasDatabaseName("ix_applications_candidate");
        builder.HasIndex(a => a.JobId).HasDatabaseName("ix_applications_job");
    }
}
