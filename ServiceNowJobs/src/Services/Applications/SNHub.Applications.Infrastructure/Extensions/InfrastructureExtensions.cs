using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SNHub.Applications.Application.Interfaces;
using SNHub.Applications.Infrastructure.Persistence;
using SNHub.Applications.Infrastructure.Persistence.Repositories;

namespace SNHub.Applications.Infrastructure.Extensions;

public static class InfrastructureExtensions
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        var conn = config.GetConnectionString("ApplicationsDb")
            ?? throw new InvalidOperationException("ApplicationsDb connection string required.");

        services.AddDbContext<ApplicationsDbContext>(o =>
            o.UseNpgsql(conn, npgsql => {
                npgsql.EnableRetryOnFailure(3, TimeSpan.FromSeconds(5), null);
                npgsql.MigrationsHistoryTable("__ef_migrations", "applications");
            }));

        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<ApplicationsDbContext>());
        services.AddScoped<IApplicationRepository, ApplicationRepository>();
        services.AddHealthChecks().AddNpgSql(conn, name: "postgres-applications", tags: ["ready"]);
        return services;
    }
}
