using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SNHub.Users.Domain.Entities;

namespace SNHub.Users.Infrastructure.Persistence.Configurations;

public sealed class UserProfileConfiguration : IEntityTypeConfiguration<UserProfile>
{
    public void Configure(EntityTypeBuilder<UserProfile> b)
    {
        b.ToTable("user_profiles", "users");
        b.HasKey(p => p.Id);

        b.Property(p => p.Id).HasColumnName("id");
        b.Property(p => p.UserId).HasColumnName("user_id").IsRequired();
        b.Property(p => p.FirstName).HasColumnName("first_name").HasMaxLength(100);
        b.Property(p => p.LastName).HasColumnName("last_name").HasMaxLength(100);
        b.Property(p => p.Email).HasColumnName("email").HasMaxLength(320);
        b.Property(p => p.PhoneNumber).HasColumnName("phone_number").HasMaxLength(30);
        b.Property(p => p.Headline).HasColumnName("headline").HasMaxLength(200);
        b.Property(p => p.Bio).HasColumnName("bio").HasMaxLength(2000);
        b.Property(p => p.Location).HasColumnName("location").HasMaxLength(200);
        b.Property(p => p.ProfilePictureUrl).HasColumnName("profile_picture_url").HasMaxLength(2048);
        b.Property(p => p.LinkedInUrl).HasColumnName("linkedin_url").HasMaxLength(500);
        b.Property(p => p.GitHubUrl).HasColumnName("github_url").HasMaxLength(500);
        b.Property(p => p.WebsiteUrl).HasColumnName("website_url").HasMaxLength(500);
        b.Property(p => p.IsPublic).HasColumnName("is_public").HasDefaultValue(true);
        b.Property(p => p.YearsOfExperience).HasColumnName("years_of_experience").HasDefaultValue(0);
        b.Property(p => p.Country).HasColumnName("country").HasMaxLength(3);
        b.Property(p => p.TimeZone).HasColumnName("time_zone").HasMaxLength(100);
        b.Property(p => p.IsDeleted).HasColumnName("is_deleted").HasDefaultValue(false);
        b.Property(p => p.DeletedAt).HasColumnName("deleted_at");
        b.Property(p => p.DeletedBy).HasColumnName("deleted_by");
        b.Property(p => p.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(p => p.UpdatedAt).HasColumnName("updated_at").IsRequired();

        b.HasIndex(p => p.UserId).IsUnique().HasDatabaseName("ix_user_profiles_user_id");
        b.HasIndex(p => p.Email).HasDatabaseName("ix_user_profiles_email");
        b.HasIndex(p => p.IsDeleted).HasDatabaseName("ix_user_profiles_is_deleted");
    }
}
