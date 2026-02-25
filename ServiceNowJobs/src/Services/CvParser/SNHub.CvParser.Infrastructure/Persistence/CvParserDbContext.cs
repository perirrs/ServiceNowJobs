using Microsoft.EntityFrameworkCore;
using SNHub.CvParser.Application.Interfaces;
using SNHub.CvParser.Domain.Entities;
using System.Reflection;

namespace SNHub.CvParser.Infrastructure.Persistence;

public sealed class CvParserDbContext : DbContext, IUnitOfWork
{
    public CvParserDbContext(DbContextOptions<CvParserDbContext> options) : base(options) { }

    public DbSet<CvParseResult> ParseResults => Set<CvParseResult>();

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
