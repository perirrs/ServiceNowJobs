using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SNHub.Matching.Domain.Entities;

namespace SNHub.Matching.Infrastructure.Persistence.Configurations;

public sealed class EmbeddingRecordConfiguration : IEntityTypeConfiguration<EmbeddingRecord>
{
    public void Configure(EntityTypeBuilder<EmbeddingRecord> b)
    {
        b.ToTable("embedding_records", "matching");
        b.HasKey(r => r.Id);
        b.Property(r => r.Id).HasColumnName("id");
        b.Property(r => r.DocumentId).HasColumnName("document_id").IsRequired();
        b.Property(r => r.DocumentType).HasColumnName("document_type").IsRequired();
        b.Property(r => r.Status).HasColumnName("status").IsRequired();
        b.Property(r => r.ErrorMessage).HasColumnName("error_message").HasMaxLength(2000);
        b.Property(r => r.RetryCount).HasColumnName("retry_count").HasDefaultValue(0);
        b.Property(r => r.LastIndexedAt).HasColumnName("last_indexed_at");
        b.Property(r => r.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(r => r.UpdatedAt).HasColumnName("updated_at").IsRequired();

        b.HasIndex(r => new { r.DocumentId, r.DocumentType })
            .IsUnique()
            .HasDatabaseName("ix_embedding_records_document");
        b.HasIndex(r => r.Status)
            .HasDatabaseName("ix_embedding_records_status");
        b.HasIndex(r => r.UpdatedAt)
            .HasDatabaseName("ix_embedding_records_updated_at");
    }
}
