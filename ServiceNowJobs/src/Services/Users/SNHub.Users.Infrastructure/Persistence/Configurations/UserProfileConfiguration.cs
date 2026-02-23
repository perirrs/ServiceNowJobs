using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SNHub.Users.Domain.Entities;

namespace SNHub.Users.Infrastructure.Persistence.Configurations;

public sealed class UserProfileConfiguration : IEntityTypeConfiguration<UserProfile>
{
    public void Configure(EntityTypeBuilder<UserProfile> builder)
    {
        builder.ToTable("user_profiles", "users");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).HasColumnName("id");
        builder.Property(p => p.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(p => p.Headline).HasColumnName("headline").HasMaxLength(200);
        builder.Property(p => p.Bio).HasColumnName("bio").HasMaxLength(2000);
        builder.Property(p => p.Location).HasColumnName("location").HasMaxLength(200);
        builder.Property(p => p.ProfilePictureUrl).HasColumnName("profile_picture_url").HasMaxLength(2048);
        builder.Property(p => p.CvUrl).HasColumnName("cv_url").HasMaxLength(2048);
        builder.Property(p => p.LinkedInUrl).HasColumnName("linkedin_url").HasMaxLength(500);
        builder.Property(p => p.GitHubUrl).HasColumnName("github_url").HasMaxLength(500);
        builder.Property(p => p.WebsiteUrl).HasColumnName("website_url").HasMaxLength(500);
        builder.Property(p => p.IsPublic).HasColumnName("is_public").HasDefaultValue(true);
        builder.Property(p => p.YearsOfExperience).HasColumnName("years_of_experience").HasDefaultValue(0);
        builder.Property(p => p.Country).HasColumnName("country").HasMaxLength(3);
        builder.Property(p => p.TimeZone).HasColumnName("time_zone").HasMaxLength(100);
        builder.Property(p => p.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(p => p.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.HasIndex(p => p.UserId).IsUnique().HasDatabaseName("ix_user_profiles_user_id");
    }
}
