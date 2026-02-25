using Azure.Identity;
using Azure.Storage.Blobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SNHub.Users.Application.Interfaces;
using SNHub.Users.Infrastructure.Persistence;
using SNHub.Users.Infrastructure.Persistence.Repositories;
using SNHub.Users.Infrastructure.Services;

namespace SNHub.Users.Infrastructure.Extensions;

public static class InfrastructureExtensions
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services, IConfiguration config)
    {
        var conn = config.GetConnectionString("UsersDb")
            ?? throw new InvalidOperationException("UsersDb connection string required.");

        services.AddDbContext<UsersDbContext>(opts =>
            opts.UseNpgsql(conn, npg =>
            {
                npg.EnableRetryOnFailure(3, TimeSpan.FromSeconds(5), null);
                npg.MigrationsHistoryTable("__ef_migrations", "users");
                npg.CommandTimeout(60);
            })
            .EnableDetailedErrors(config.GetValue<bool>("DetailedErrors")));

        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<UsersDbContext>());
        services.AddScoped<IUserProfileRepository, UserProfileRepository>();
        services.AddScoped<ICurrentUserService, CurrentUserService>();
        services.AddHttpContextAccessor();

        // Blob storage: Azure in production, local stub otherwise
        var storageConn = config.GetConnectionString("AzureStorage");
        var accountUrl  = config["AzureStorage:AccountUrl"];

        if (!string.IsNullOrWhiteSpace(storageConn))
        {
            services.AddSingleton(_ => new BlobServiceClient(storageConn));
            services.AddScoped<IBlobStorageService, AzureBlobStorageService>();
        }
        else if (!string.IsNullOrWhiteSpace(accountUrl))
        {
            services.AddSingleton(_ =>
                new BlobServiceClient(new Uri(accountUrl), new DefaultAzureCredential()));
            services.AddScoped<IBlobStorageService, AzureBlobStorageService>();
        }
        else
        {
            // Local / test environment
            services.AddSingleton<IBlobStorageService, LocalBlobStorageService>();
        }

        services.AddHealthChecks()
            .AddNpgSql(conn, name: "users-postgres", tags: ["ready"]);

        return services;
    }
}
