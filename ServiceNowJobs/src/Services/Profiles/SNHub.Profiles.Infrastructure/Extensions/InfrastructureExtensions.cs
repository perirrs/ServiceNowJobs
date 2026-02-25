using Azure.Storage.Blobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SNHub.Profiles.Application.Interfaces;
using SNHub.Profiles.Infrastructure.Persistence;
using SNHub.Profiles.Infrastructure.Persistence.Repositories;
using SNHub.Profiles.Infrastructure.Services;

namespace SNHub.Profiles.Infrastructure.Extensions;

public static class InfrastructureExtensions
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        var conn = config.GetConnectionString("ProfilesDb")
            ?? throw new InvalidOperationException("ProfilesDb connection string required.");

        services.AddDbContext<ProfilesDbContext>(o =>
            o.UseNpgsql(conn, n =>
            {
                n.EnableRetryOnFailure(3, TimeSpan.FromSeconds(5), null);
                n.MigrationsHistoryTable("__ef_migrations", "profiles");
                n.CommandTimeout(60);
            }));

        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<ProfilesDbContext>());
        services.AddScoped<ICandidateProfileRepository, CandidateProfileRepository>();
        services.AddScoped<IEmployerProfileRepository, EmployerProfileRepository>();
        services.AddScoped<ICurrentUserService, CurrentUserService>();
        services.AddHttpContextAccessor();

        // File storage — Azure Blob in prod, local stub if not configured
        var azureConn = config["Azure:BlobStorage:ConnectionString"];
        if (!string.IsNullOrWhiteSpace(azureConn) && azureConn != "stub")
        {
            services.AddSingleton(_ => new BlobServiceClient(azureConn));
            services.AddScoped<IFileStorageService, AzureBlobFileStorageService>();
        }
        else
        {
            // Singleton stub — all tests/local dev share the same in-memory "blob store"
            services.AddSingleton<IFileStorageService, LocalFileStorageService>();
        }

        services.AddHealthChecks()
            .AddNpgSql(conn, name: "postgres-profiles", tags: ["ready"]);

        return services;
    }
}
