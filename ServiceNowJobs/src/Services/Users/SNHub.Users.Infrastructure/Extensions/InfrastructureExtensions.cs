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
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        var conn = config.GetConnectionString("UsersDb")
            ?? throw new InvalidOperationException("UsersDb connection string required.");

        services.AddDbContext<UsersDbContext>(opts =>
            opts.UseNpgsql(conn, npg => { npg.EnableRetryOnFailure(3); npg.MigrationsHistoryTable("__ef_migrations", "users"); })
                .EnableDetailedErrors(config.GetValue<bool>("DetailedErrors")));

        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<UsersDbContext>());
        services.AddScoped<IUserProfileRepository, UserProfileRepository>();

        var storageCon = config.GetConnectionString("AzureStorage");
        if (!string.IsNullOrWhiteSpace(storageCon))
            services.AddSingleton(_ => new BlobServiceClient(storageCon));
        else
            services.AddSingleton(_ => new BlobServiceClient(new Uri(config["AzureStorage:AccountUrl"]!), new DefaultAzureCredential()));

        services.AddScoped<IBlobStorageService, AzureBlobStorageService>();

        services.AddHealthChecks()
            .AddNpgSql(conn, name: "users-postgres", tags: ["ready"]);

        return services;
    }
}
