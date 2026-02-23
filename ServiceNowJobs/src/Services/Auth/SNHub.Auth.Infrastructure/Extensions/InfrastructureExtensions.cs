using Azure.Identity;
using Azure.Storage.Blobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SNHub.Auth.Application.Interfaces;
using SNHub.Auth.Infrastructure.Persistence;
using SNHub.Auth.Infrastructure.Persistence.Repositories;
using SNHub.Auth.Infrastructure.Services;
using StackExchange.Redis;

namespace SNHub.Auth.Infrastructure.Extensions;

public static class InfrastructureExtensions
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration config)
    {
        services
            .AddDatabase(config)
            .AddAzureRedis(config)
            .AddAzureStorage(config)
            .AddRepositories()
            .AddAppServices(config);

        return services;
    }

    // ─── PostgreSQL via Azure Database for PostgreSQL Flexible Server ─────────

    private static IServiceCollection AddDatabase(
        this IServiceCollection services, IConfiguration config)
    {
        var conn = config.GetConnectionString("AuthDb")
            ?? throw new InvalidOperationException("AuthDb connection string required.");

        services.AddDbContext<AuthDbContext>(options =>
        {
            options.UseNpgsql(conn, npgsql =>
            {
                npgsql.EnableRetryOnFailure(3, TimeSpan.FromSeconds(5), null);
                npgsql.CommandTimeout(30);
                npgsql.MigrationsHistoryTable("__ef_migrations", "auth");
            });

            if (config.GetValue<bool>("DetailedErrors"))
                options.EnableDetailedErrors().EnableSensitiveDataLogging();
        });

        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<AuthDbContext>());
        services.AddScoped<AuthDbSeeder>();

        services.AddHealthChecks()
            .AddNpgSql(conn, name: "postgres", tags: ["ready"]);

        return services;
    }

    // ─── Azure Cache for Redis ────────────────────────────────────────────────

    private static IServiceCollection AddAzureRedis(
        this IServiceCollection services, IConfiguration config)
    {
        var conn = config.GetConnectionString("Redis")
            ?? throw new InvalidOperationException("Redis connection string required.");

        services.AddSingleton<IConnectionMultiplexer>(_ =>
            ConnectionMultiplexer.Connect(conn));

        services.AddStackExchangeRedisCache(opts =>
        {
            opts.Configuration = conn;
            opts.InstanceName = "snhub_auth_";
        });

        services.AddHealthChecks()
            .AddRedis(conn, name: "redis", tags: ["ready"]);

        return services;
    }

    // ─── Azure Blob Storage ───────────────────────────────────────────────────

    private static IServiceCollection AddAzureStorage(
        this IServiceCollection services, IConfiguration config)
    {
        var conn = config.GetConnectionString("AzureStorage");

        if (!string.IsNullOrWhiteSpace(conn))
        {
            // Local dev — use connection string
            services.AddSingleton(_ => new BlobServiceClient(conn));
        }
        else
        {
            // Production — use Managed Identity (no secrets needed)
            var accountUrl = config["AzureStorage:AccountUrl"]
                ?? throw new InvalidOperationException("AzureStorage:AccountUrl required in production.");
            services.AddSingleton(_ => new BlobServiceClient(
                new Uri(accountUrl), new DefaultAzureCredential()));
        }

        services.Configure<AzureStorageSettings>(
            config.GetSection(AzureStorageSettings.SectionName));
        services.AddScoped<IBlobStorageService, AzureBlobStorageService>();

        return services;
    }

    // ─── Repositories ─────────────────────────────────────────────────────────

    private static IServiceCollection AddRepositories(this IServiceCollection services)
    {
        services.AddScoped<IUserRepository, UserRepository>();
        return services;
    }

    // ─── Application Services ─────────────────────────────────────────────────

    private static IServiceCollection AddAppServices(
        this IServiceCollection services, IConfiguration config)
    {
        services.Configure<JwtSettings>(config.GetSection(JwtSettings.SectionName));
        services.Configure<AzureEmailSettings>(config.GetSection(AzureEmailSettings.SectionName));

        services.AddScoped<ITokenService, TokenService>();
        services.AddScoped<IPasswordHasher, PasswordHasher>();
        services.AddScoped<IEmailService, EmailService>();

        return services;
    }
}
