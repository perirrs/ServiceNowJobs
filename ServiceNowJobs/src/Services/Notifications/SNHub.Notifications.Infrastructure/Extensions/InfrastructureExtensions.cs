using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SNHub.Notifications.Application.Interfaces;
using SNHub.Notifications.Infrastructure.Persistence;
using SNHub.Notifications.Infrastructure.Persistence.Repositories;
using SNHub.Notifications.Infrastructure.Services;

namespace SNHub.Notifications.Infrastructure.Extensions;

public static class InfrastructureExtensions
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        var conn = config.GetConnectionString("NotificationsDb")
            ?? throw new InvalidOperationException("NotificationsDb connection string required.");

        services.AddDbContext<NotificationsDbContext>(o =>
            o.UseNpgsql(conn, n =>
            {
                n.EnableRetryOnFailure(3, TimeSpan.FromSeconds(5), null);
                n.MigrationsHistoryTable("__ef_migrations", "notifications");
                n.CommandTimeout(60);
            }));

        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<NotificationsDbContext>());
        services.AddScoped<INotificationRepository, NotificationRepository>();
        services.AddScoped<ICurrentUserService, CurrentUserService>();
        services.AddHttpContextAccessor();

        services.AddHealthChecks()
            .AddNpgSql(conn, name: "postgres-notifications", tags: ["ready"]);

        return services;
    }
}
