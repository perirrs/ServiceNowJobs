using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SNHub.Jobs.Application.Interfaces;
using SNHub.Jobs.Infrastructure.Persistence;
using SNHub.Jobs.Infrastructure.Persistence.Repositories;
using SNHub.Jobs.Infrastructure.Services;

namespace SNHub.Jobs.Infrastructure.Extensions;

public static class InfrastructureExtensions
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        var conn = config.GetConnectionString("JobsDb")
            ?? throw new InvalidOperationException("JobsDb connection string is required.");

        services.AddDbContext<JobsDbContext>(o =>
            o.UseNpgsql(conn, n =>
            {
                n.EnableRetryOnFailure(3);
                n.MigrationsHistoryTable("__ef_migrations", "jobs");
            }));

        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<JobsDbContext>());
        services.AddScoped<IJobRepository, JobRepository>();
        services.AddScoped<ICurrentUserService, CurrentUserService>();
        services.AddHttpContextAccessor();

        services.AddHealthChecks()
            .AddNpgSql(conn, name: "jobs-postgres", tags: ["ready"]);

        return services;
    }
}
