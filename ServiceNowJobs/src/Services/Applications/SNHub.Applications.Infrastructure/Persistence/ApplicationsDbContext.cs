using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using SNHub.Applications.Application.Interfaces;
using SNHub.Applications.Domain.Entities;
using System.Reflection;

namespace SNHub.Applications.Infrastructure.Persistence;

public sealed class ApplicationsDbContext : DbContext, IUnitOfWork
{
    public ApplicationsDbContext(DbContextOptions<ApplicationsDbContext> opts) : base(opts) { }

    public DbSet<JobApplication> Applications => Set<JobApplication>();

    protected override void OnModelCreating(ModelBuilder mb)
    {
        mb.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
        foreach (var p in mb.Model.GetEntityTypes().SelectMany(e => e.GetProperties())
            .Where(p => p.ClrType == typeof(DateTimeOffset) || p.ClrType == typeof(DateTimeOffset?)))
            p.SetColumnType("timestamptz");
    }

    public async Task<int> SaveChangesAsync(CancellationToken ct = default)
        => await base.SaveChangesAsync(ct);
}
