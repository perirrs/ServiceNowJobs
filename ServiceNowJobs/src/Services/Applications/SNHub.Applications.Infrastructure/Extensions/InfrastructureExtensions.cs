using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SNHub.Applications.Application.Interfaces;
using SNHub.Applications.Infrastructure.Persistence;
using SNHub.Applications.Infrastructure.Persistence.Repositories;
using SNHub.Applications.Infrastructure.Services;

namespace SNHub.Applications.Infrastructure.Extensions;

public static class InfrastructureExtensions
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        var conn = config.GetConnectionString("ApplicationsDb")
            ?? throw new InvalidOperationException("ApplicationsDb connection string required.");

        services.AddDbContext<ApplicationsDbContext>(o =>
            o.UseNpgsql(conn, n =>
            {
                n.EnableRetryOnFailure(3, TimeSpan.FromSeconds(5), null);
                n.MigrationsHistoryTable("__ef_migrations", "applications");
                n.CommandTimeout(60);
            }));

        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<ApplicationsDbContext>());
        services.AddScoped<IApplicationRepository, ApplicationRepository>();
        services.AddScoped<ICurrentUserService, CurrentUserService>();
        services.AddHttpContextAccessor();

        // ── Subscription service stub — replace with HttpSubscriptionService in Step 17 ──
        services.AddScoped<ISubscriptionService, StubSubscriptionService>();

        services.AddHealthChecks()
            .AddNpgSql(conn, name: "postgres-applications", tags: ["ready"]);

        return services;
    }
}
