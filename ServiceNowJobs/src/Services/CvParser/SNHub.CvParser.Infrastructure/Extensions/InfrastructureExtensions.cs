using Azure.AI.OpenAI;
using Azure.Identity;
using Azure.Storage.Blobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SNHub.CvParser.Application.Commands.ApplyParsedCv;
using SNHub.CvParser.Application.Interfaces;
using SNHub.CvParser.Infrastructure.Persistence;
using SNHub.CvParser.Infrastructure.Persistence.Repositories;
using SNHub.CvParser.Infrastructure.Services;

namespace SNHub.CvParser.Infrastructure.Extensions;

public static class InfrastructureExtensions
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services, IConfiguration config)
    {
        // ── Database ─────────────────────────────────────────────────────────
        var conn = config.GetConnectionString("CvParserDb")
            ?? throw new InvalidOperationException("CvParserDb connection string required.");

        services.AddDbContext<CvParserDbContext>(opts =>
            opts.UseNpgsql(conn, npg =>
            {
                npg.EnableRetryOnFailure(3, TimeSpan.FromSeconds(5), null);
                npg.MigrationsHistoryTable("__ef_migrations", "cvparser");
                npg.CommandTimeout(60);
            })
            .EnableDetailedErrors(config.GetValue<bool>("DetailedErrors")));

        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<CvParserDbContext>());
        services.AddScoped<ICvParseResultRepository, CvParseResultRepository>();

        // ── Blob Storage ─────────────────────────────────────────────────────
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
            services.AddSingleton<IBlobStorageService, LocalBlobStorageService>();
        }

        // ── Azure OpenAI CV Parser ────────────────────────────────────────────
        var openAiEndpoint = config["AzureOpenAI:Endpoint"];
        var openAiKey      = config["AzureOpenAI:ApiKey"];

        if (!string.IsNullOrWhiteSpace(openAiEndpoint) && !string.IsNullOrWhiteSpace(openAiKey))
        {
            services.AddSingleton(_ =>
                new AzureOpenAIClient(new Uri(openAiEndpoint),
                    new System.ClientModel.ApiKeyCredential(openAiKey)));
            services.AddScoped<ICvParserService, AzureOpenAiCvParserService>();
        }
        else if (!string.IsNullOrWhiteSpace(openAiEndpoint))
        {
            // Managed Identity
            services.AddSingleton(_ =>
                new AzureOpenAIClient(new Uri(openAiEndpoint), new DefaultAzureCredential()));
            services.AddScoped<ICvParserService, AzureOpenAiCvParserService>();
        }
        else
        {
            // Local / test — use deterministic stub
            services.AddSingleton<ICvParserService, StubCvParserService>();
        }

        // ── Profiles service HTTP client ──────────────────────────────────────
        var profilesUrl = config["Services:ProfilesServiceUrl"];
        if (!string.IsNullOrWhiteSpace(profilesUrl))
        {
            services.AddHttpClient<IProfilesServiceClient, ProfilesServiceHttpClient>(
                client => client.BaseAddress = new Uri(profilesUrl));
        }
        else
        {
            services.AddSingleton<IProfilesServiceClient, StubProfilesServiceClient>();
        }

        // ── Auth / HTTP context ───────────────────────────────────────────────
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUserService, CurrentUserService>();

        // ── Health checks ─────────────────────────────────────────────────────
        services.AddHealthChecks()
            .AddNpgSql(conn, name: "cvparser-postgres", tags: ["ready"]);

        return services;
    }
}
