using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SNHub.JobEnhancer.Application.Interfaces;
using SNHub.JobEnhancer.Domain.Entities;
using SNHub.JobEnhancer.Domain.Enums;
using System.Reflection;

namespace SNHub.JobEnhancer.Infrastructure.Persistence;

// ── DbContext ─────────────────────────────────────────────────────────────────

public sealed class JobEnhancerDbContext : DbContext, IUnitOfWork
{
    public JobEnhancerDbContext(DbContextOptions<JobEnhancerDbContext> options) : base(options) { }
    public DbSet<EnhancementResult> EnhancementResults => Set<EnhancementResult>();

    protected override void OnModelCreating(ModelBuilder mb)
    {
        mb.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
        foreach (var p in mb.Model.GetEntityTypes()
            .SelectMany(e => e.GetProperties())
            .Where(p => p.ClrType == typeof(DateTimeOffset) || p.ClrType == typeof(DateTimeOffset?)))
            p.SetColumnType("timestamptz");
    }

    public override Task<int> SaveChangesAsync(CancellationToken ct = default)
        => base.SaveChangesAsync(ct);
}

// ── EF Configuration ──────────────────────────────────────────────────────────

public sealed class EnhancementResultConfiguration
    : IEntityTypeConfiguration<EnhancementResult>
{
    public void Configure(EntityTypeBuilder<EnhancementResult> b)
    {
        b.ToTable("enhancement_results", "enhancer");
        b.HasKey(r => r.Id);

        b.Property(r => r.Id).HasColumnName("id");
        b.Property(r => r.JobId).HasColumnName("job_id").IsRequired();
        b.Property(r => r.RequestedBy).HasColumnName("requested_by").IsRequired();
        b.Property(r => r.Status).HasColumnName("status").IsRequired();
        b.Property(r => r.ErrorMessage).HasColumnName("error_message").HasMaxLength(2000);

        b.Property(r => r.OriginalTitle).HasColumnName("original_title").HasMaxLength(200).IsRequired();
        b.Property(r => r.OriginalDescription).HasColumnName("original_description").IsRequired();
        b.Property(r => r.OriginalRequirements).HasColumnName("original_requirements");

        b.Property(r => r.EnhancedTitle).HasColumnName("enhanced_title").HasMaxLength(200);
        b.Property(r => r.EnhancedDescription).HasColumnName("enhanced_description");
        b.Property(r => r.EnhancedRequirements).HasColumnName("enhanced_requirements");

        b.Property(r => r.ScoreBefore).HasColumnName("score_before").HasDefaultValue(0);
        b.Property(r => r.ScoreAfter).HasColumnName("score_after").HasDefaultValue(0);

        b.Property(r => r.BiasIssuesJson).HasColumnName("bias_issues_json")
            .HasColumnType("jsonb").HasDefaultValue("[]");
        b.Property(r => r.MissingFieldsJson).HasColumnName("missing_fields_json")
            .HasColumnType("jsonb").HasDefaultValue("[]");
        b.Property(r => r.ImprovementsJson).HasColumnName("improvements_json")
            .HasColumnType("jsonb").HasDefaultValue("[]");
        b.Property(r => r.SuggestedSkillsJson).HasColumnName("suggested_skills_json")
            .HasColumnType("jsonb").HasDefaultValue("[]");

        b.Property(r => r.IsAccepted).HasColumnName("is_accepted").HasDefaultValue(false);
        b.Property(r => r.AcceptedAt).HasColumnName("accepted_at");
        b.Property(r => r.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(r => r.UpdatedAt).HasColumnName("updated_at").IsRequired();

        b.HasIndex(r => r.JobId).HasDatabaseName("ix_enhancement_results_job_id");
        b.HasIndex(r => r.RequestedBy).HasDatabaseName("ix_enhancement_results_requested_by");
        b.HasIndex(r => r.CreatedAt).HasDatabaseName("ix_enhancement_results_created_at");
    }
}

// ── Repository ────────────────────────────────────────────────────────────────

public sealed class EnhancementResultRepository : IEnhancementResultRepository
{
    private readonly JobEnhancerDbContext _db;
    public EnhancementResultRepository(JobEnhancerDbContext db) => _db = db;

    public Task<EnhancementResult?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => _db.EnhancementResults.FirstOrDefaultAsync(r => r.Id == id, ct);

    public async Task<IEnumerable<EnhancementResult>> GetByJobIdAsync(
        Guid jobId, CancellationToken ct = default)
        => await _db.EnhancementResults
            .AsNoTracking()
            .Where(r => r.JobId == jobId)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync(ct);

    public async Task<IEnumerable<EnhancementResult>> GetByRequesterAsync(
        Guid userId, CancellationToken ct = default)
        => await _db.EnhancementResults
            .AsNoTracking()
            .Where(r => r.RequestedBy == userId)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync(ct);

    public async Task AddAsync(EnhancementResult result, CancellationToken ct = default)
        => await _db.EnhancementResults.AddAsync(result, ct);
}
