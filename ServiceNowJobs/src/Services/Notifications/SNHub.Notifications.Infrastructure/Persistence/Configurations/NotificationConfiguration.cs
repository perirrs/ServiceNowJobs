using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SNHub.Notifications.Domain.Entities;
namespace SNHub.Notifications.Infrastructure.Persistence.Configurations;
public sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> b)
    {
        b.ToTable("notifications", "notifications");
        b.HasKey(n => n.Id); b.Property(n => n.Id).HasColumnName("id");
        b.Property(n => n.UserId).HasColumnName("user_id");
        b.Property(n => n.Type).HasColumnName("type");
        b.Property(n => n.Title).HasColumnName("title").HasMaxLength(200).IsRequired();
        b.Property(n => n.Message).HasColumnName("message").HasMaxLength(1000).IsRequired();
        b.Property(n => n.ActionUrl).HasColumnName("action_url").HasMaxLength(2048);
        b.Property(n => n.MetadataJson).HasColumnName("metadata").HasColumnType("jsonb");
        b.Property(n => n.IsRead).HasColumnName("is_read").HasDefaultValue(false);
        b.Property(n => n.CreatedAt).HasColumnName("created_at");
        b.Property(n => n.ReadAt).HasColumnName("read_at");
        b.HasIndex(n => new { n.UserId, n.IsRead }).HasDatabaseName("ix_notifications_user_read");
        b.HasIndex(n => n.CreatedAt).HasDatabaseName("ix_notifications_created");
    }
}
