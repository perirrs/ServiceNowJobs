using Microsoft.EntityFrameworkCore;
using SNHub.Matching.Application.Interfaces;
using SNHub.Matching.Domain.Entities;
using SNHub.Matching.Domain.Enums;
using System.Reflection;

namespace SNHub.Matching.Infrastructure.Persistence;

public sealed class MatchingDbContext : DbContext, IUnitOfWork
{
    public MatchingDbContext(DbContextOptions<MatchingDbContext> options) : base(options) { }
    public DbSet<EmbeddingRecord> EmbeddingRecords => Set<EmbeddingRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
        foreach (var p in modelBuilder.Model.GetEntityTypes()
            .SelectMany(e => e.GetProperties())
            .Where(p => p.ClrType == typeof(DateTimeOffset) || p.ClrType == typeof(DateTimeOffset?)))
            p.SetColumnType("timestamptz");
    }

    public override Task<int> SaveChangesAsync(CancellationToken ct = default)
        => base.SaveChangesAsync(ct);
}

// ── Repository ────────────────────────────────────────────────────────────────

public sealed class EmbeddingRecordRepository : IEmbeddingRecordRepository
{
    private readonly MatchingDbContext _db;
    public EmbeddingRecordRepository(MatchingDbContext db) => _db = db;

    public Task<EmbeddingRecord?> GetByDocumentIdAsync(
        Guid documentId, DocumentType type, CancellationToken ct = default)
        => _db.EmbeddingRecords
              .FirstOrDefaultAsync(r => r.DocumentId == documentId && r.DocumentType == type, ct);

    public async Task<IEnumerable<EmbeddingRecord>> GetPendingAsync(
        int batchSize, CancellationToken ct = default)
        => await _db.EmbeddingRecords
            .Where(r => r.Status == EmbeddingStatus.Pending && r.RetryCount < 3)
            .OrderBy(r => r.UpdatedAt)
            .Take(batchSize)
            .ToListAsync(ct);

    public async Task AddAsync(EmbeddingRecord record, CancellationToken ct = default)
        => await _db.EmbeddingRecords.AddAsync(record, ct);

    public Task<bool> ExistsAsync(Guid documentId, DocumentType type, CancellationToken ct = default)
        => _db.EmbeddingRecords
              .AnyAsync(r => r.DocumentId == documentId && r.DocumentType == type, ct);
}
