using Microsoft.EntityFrameworkCore;
using SNHub.Notifications.Application.Interfaces;
using SNHub.Notifications.Domain.Entities;
using System.Reflection;
namespace SNHub.Notifications.Infrastructure.Persistence;
public sealed class NotificationsDbContext : DbContext, IUnitOfWork
{
    public NotificationsDbContext(DbContextOptions<NotificationsDbContext> opts) : base(opts) { }
    public DbSet<Notification> Notifications => Set<Notification>();
    protected override void OnModelCreating(ModelBuilder mb)
    {
        mb.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
        foreach (var p in mb.Model.GetEntityTypes().SelectMany(e => e.GetProperties())
            .Where(p => p.ClrType == typeof(DateTimeOffset) || p.ClrType == typeof(DateTimeOffset?)))
            p.SetColumnType("timestamptz");
    }
    public override async Task<int> SaveChangesAsync(CancellationToken ct = default) => await base.SaveChangesAsync(ct);
}
