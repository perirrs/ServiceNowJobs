using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SNHub.Profiles.Application.Interfaces;
using SNHub.Profiles.Infrastructure.Persistence;
using SNHub.Profiles.Infrastructure.Persistence.Repositories;
namespace SNHub.Profiles.Infrastructure.Extensions;
public static class InfrastructureExtensions
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        var conn = config.GetConnectionString("ProfilesDb") ?? throw new InvalidOperationException("ProfilesDb connection string required.");
        services.AddDbContext<ProfilesDbContext>(o => o.UseNpgsql(conn, n => { n.EnableRetryOnFailure(3, TimeSpan.FromSeconds(5), null); n.MigrationsHistoryTable("__ef_migrations", "profiles"); }));
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<ProfilesDbContext>());
        services.AddScoped<ICandidateProfileRepository, CandidateProfileRepository>();
        services.AddScoped<IEmployerProfileRepository, EmployerProfileRepository>();
        services.AddHealthChecks().AddNpgSql(conn, name: "postgres-profiles", tags: ["ready"]);
        return services;
    }
}
