using Microsoft.EntityFrameworkCore;
using SNHub.Profiles.Application.Interfaces;
using SNHub.Profiles.Domain.Entities;
using System.Reflection;
namespace SNHub.Profiles.Infrastructure.Persistence;
public sealed class ProfilesDbContext : DbContext, IUnitOfWork
{
    public ProfilesDbContext(DbContextOptions<ProfilesDbContext> opts) : base(opts) { }
    public DbSet<CandidateProfile> CandidateProfiles => Set<CandidateProfile>();
    public DbSet<EmployerProfile> EmployerProfiles => Set<EmployerProfile>();
    protected override void OnModelCreating(ModelBuilder mb)
    {
        mb.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
        foreach (var p in mb.Model.GetEntityTypes().SelectMany(e => e.GetProperties())
            .Where(p => p.ClrType == typeof(DateTimeOffset) || p.ClrType == typeof(DateTimeOffset?)))
            p.SetColumnType("timestamptz");
    }
    public async Task<int> SaveChangesAsync(CancellationToken ct = default) => await base.SaveChangesAsync(ct);
}
