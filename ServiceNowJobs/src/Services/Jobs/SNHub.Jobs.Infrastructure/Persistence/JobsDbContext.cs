using Microsoft.EntityFrameworkCore;
using SNHub.Jobs.Application.Interfaces;
using SNHub.Jobs.Domain.Entities;
using System.Reflection;

namespace SNHub.Jobs.Infrastructure.Persistence;

public sealed class JobsDbContext : DbContext, IUnitOfWork
{
    public JobsDbContext(DbContextOptions<JobsDbContext> options) : base(options) { }
    public DbSet<Job> Jobs => Set<Job>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
        foreach (var p in modelBuilder.Model.GetEntityTypes()
            .SelectMany(e => e.GetProperties())
            .Where(p => p.ClrType == typeof(DateTimeOffset) || p.ClrType == typeof(DateTimeOffset?)))
            p.SetColumnType("timestamptz");
    }

    public override async Task<int> SaveChangesAsync(CancellationToken ct = default)
        => await base.SaveChangesAsync(ct);
}
