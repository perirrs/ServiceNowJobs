using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SNHub.Auth.Domain.Entities;

namespace SNHub.Auth.Infrastructure.Persistence.Configurations;

public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users", "auth");
        builder.HasKey(u => u.Id);

        builder.Property(u => u.Id).HasColumnName("id");
        builder.Property(u => u.Email).HasColumnName("email").HasMaxLength(256).IsRequired();
        builder.Property(u => u.NormalizedEmail).HasColumnName("normalized_email").HasMaxLength(256).IsRequired();
        builder.Property(u => u.PasswordHash).HasColumnName("password_hash").HasMaxLength(512);
        builder.Property(u => u.FirstName).HasColumnName("first_name").HasMaxLength(100).IsRequired();
        builder.Property(u => u.LastName).HasColumnName("last_name").HasMaxLength(100).IsRequired();
        builder.Property(u => u.PhoneNumber).HasColumnName("phone_number").HasMaxLength(20);
        builder.Property(u => u.ProfilePictureUrl).HasColumnName("profile_picture_url").HasMaxLength(2048);
        builder.Property(u => u.IsEmailVerified).HasColumnName("is_email_verified").HasDefaultValue(false);
        builder.Property(u => u.IsActive).HasColumnName("is_active").HasDefaultValue(true);
        builder.Property(u => u.IsSuspended).HasColumnName("is_suspended").HasDefaultValue(false);
        builder.Property(u => u.SuspensionReason).HasColumnName("suspension_reason").HasMaxLength(1000);
        builder.Property(u => u.SuspendedAt).HasColumnName("suspended_at");
        builder.Property(u => u.EmailVerifiedAt).HasColumnName("email_verified_at");
        builder.Property(u => u.LastLoginAt).HasColumnName("last_login_at");
        builder.Property(u => u.LastLoginIp).HasColumnName("last_login_ip").HasMaxLength(45);
        builder.Property(u => u.FailedLoginAttempts).HasColumnName("failed_login_attempts").HasDefaultValue(0);
        builder.Property(u => u.LockedOutUntil).HasColumnName("locked_out_until");
        builder.Property(u => u.EmailVerificationToken).HasColumnName("email_verification_token").HasMaxLength(256);
        builder.Property(u => u.EmailVerificationTokenExpiry).HasColumnName("email_verification_token_expiry");
        builder.Property(u => u.PasswordResetToken).HasColumnName("password_reset_token").HasMaxLength(256);
        builder.Property(u => u.PasswordResetTokenExpiry).HasColumnName("password_reset_token_expiry");
        builder.Property(u => u.LinkedInId).HasColumnName("linkedin_id").HasMaxLength(100);
        builder.Property(u => u.GoogleId).HasColumnName("google_id").HasMaxLength(100);
        builder.Property(u => u.AzureAdObjectId).HasColumnName("azure_ad_object_id").HasMaxLength(100);
        builder.Property(u => u.TimeZone).HasColumnName("time_zone").HasMaxLength(100).HasDefaultValue("UTC");
        builder.Property(u => u.Country).HasColumnName("country").HasMaxLength(3);
        builder.Property(u => u.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(u => u.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.Property(u => u.CreatedBy).HasColumnName("created_by").HasMaxLength(256).HasDefaultValue("system");
        builder.Property(u => u.UpdatedBy).HasColumnName("updated_by").HasMaxLength(256).HasDefaultValue("system");

        builder.PrimitiveCollection(u => u.Roles)
            .HasColumnName("roles")
            .HasColumnType("int[]")
            .IsRequired();

        // Unique indexes
        builder.HasIndex(u => u.NormalizedEmail).IsUnique().HasDatabaseName("ix_users_email");
        builder.HasIndex(u => u.LinkedInId).IsUnique().HasFilter("linkedin_id IS NOT NULL").HasDatabaseName("ix_users_linkedin");
        builder.HasIndex(u => u.AzureAdObjectId).IsUnique().HasFilter("azure_ad_object_id IS NOT NULL").HasDatabaseName("ix_users_azure_ad");
        builder.HasIndex(u => new { u.IsActive, u.CreatedAt }).HasDatabaseName("ix_users_active_created");

        builder.HasMany(u => u.RefreshTokens)
            .WithOne()
            .HasForeignKey(rt => rt.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Ignore(u => u.DomainEvents);
    }
}

public sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("refresh_tokens", "auth");
        builder.HasKey(rt => rt.Id);

        builder.Property(rt => rt.Id).HasColumnName("id");
        builder.Property(rt => rt.UserId).HasColumnName("user_id");
        builder.Property(rt => rt.Token).HasColumnName("token").HasMaxLength(512).IsRequired();
        builder.Property(rt => rt.ExpiresAt).HasColumnName("expires_at");
        builder.Property(rt => rt.CreatedAt).HasColumnName("created_at");
        builder.Property(rt => rt.CreatedByIp).HasColumnName("created_by_ip").HasMaxLength(45);
        builder.Property(rt => rt.UserAgent).HasColumnName("user_agent").HasMaxLength(512);
        builder.Property(rt => rt.RevokedAt).HasColumnName("revoked_at");
        builder.Property(rt => rt.RevokedByIp).HasColumnName("revoked_by_ip").HasMaxLength(45);
        builder.Property(rt => rt.RevokeReason).HasColumnName("revoke_reason").HasMaxLength(256);
        builder.Property(rt => rt.ReplacedByToken).HasColumnName("replaced_by_token").HasMaxLength(512);

        builder.HasIndex(rt => rt.Token).IsUnique().HasDatabaseName("ix_rt_token");
        builder.HasIndex(rt => new { rt.UserId, rt.ExpiresAt }).HasDatabaseName("ix_rt_user_expiry");
        builder.HasIndex(rt => rt.Token).HasFilter("revoked_at IS NULL").HasDatabaseName("ix_rt_active");
    }
}
