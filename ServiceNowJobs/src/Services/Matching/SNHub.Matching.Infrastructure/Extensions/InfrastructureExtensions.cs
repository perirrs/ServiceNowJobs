using Azure;
using Azure.AI.OpenAI;
using Azure.Identity;
using Azure.Search.Documents.Indexes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SNHub.Matching.Application.Interfaces;
using SNHub.Matching.Infrastructure.Persistence;
using SNHub.Matching.Infrastructure.Services;
using SNHub.Matching.Infrastructure.Workers;

namespace SNHub.Matching.Infrastructure.Extensions;

public static class InfrastructureExtensions
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services, IConfiguration config)
    {
        // ── Database ──────────────────────────────────────────────────────────
        var conn = config.GetConnectionString("MatchingDb")
            ?? throw new InvalidOperationException("MatchingDb connection string required.");

        services.AddDbContext<MatchingDbContext>(opts =>
            opts.UseNpgsql(conn, npg =>
            {
                npg.EnableRetryOnFailure(3, TimeSpan.FromSeconds(5), null);
                npg.MigrationsHistoryTable("__ef_migrations", "matching");
                npg.CommandTimeout(60);
            }));

        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<MatchingDbContext>());
        services.AddScoped<IEmbeddingRecordRepository, EmbeddingRecordRepository>();

        // ── Azure OpenAI ──────────────────────────────────────────────────────
        var openAiEndpoint = config["AzureOpenAI:Endpoint"];
        var openAiKey      = config["AzureOpenAI:ApiKey"];

        if (!string.IsNullOrWhiteSpace(openAiEndpoint) && !string.IsNullOrWhiteSpace(openAiKey))
        {
            services.AddSingleton(_ =>
                new AzureOpenAIClient(new Uri(openAiEndpoint),
                    new System.ClientModel.ApiKeyCredential(openAiKey)));
            services.AddScoped<IEmbeddingService, AzureOpenAiEmbeddingService>();
        }
        else if (!string.IsNullOrWhiteSpace(openAiEndpoint))
        {
            services.AddSingleton(_ =>
                new AzureOpenAIClient(new Uri(openAiEndpoint), new DefaultAzureCredential()));
            services.AddScoped<IEmbeddingService, AzureOpenAiEmbeddingService>();
        }
        else
        {
            services.AddSingleton<IEmbeddingService, StubEmbeddingService>();
        }

        // ── Azure AI Search ───────────────────────────────────────────────────
        var searchEndpoint = config["AzureAISearch:Endpoint"];
        var searchKey      = config["AzureAISearch:ApiKey"];

        if (!string.IsNullOrWhiteSpace(searchEndpoint) && !string.IsNullOrWhiteSpace(searchKey))
        {
            services.AddSingleton(_ =>
                new SearchIndexClient(new Uri(searchEndpoint),
                    new AzureKeyCredential(searchKey)));
            services.AddScoped<IVectorSearchService>(sp =>
                new AzureAiSearchVectorService(
                    sp.GetRequiredService<SearchIndexClient>(),
                    sp.GetRequiredService<IEmbeddingRecordRepository>(),
                    config,
                    sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<AzureAiSearchVectorService>>()));
        }
        else
        {
            // In-memory for local dev / tests — singleton so state persists across requests
            services.AddSingleton<IVectorSearchService, InMemoryVectorSearchService>();
        }

        // ── Cross-service HTTP clients ─────────────────────────────────────────
        var jobsUrl     = config["Services:JobsServiceUrl"];
        var profilesUrl = config["Services:ProfilesServiceUrl"];

        if (!string.IsNullOrWhiteSpace(jobsUrl))
            services.AddHttpClient<IJobsServiceClient, JobsServiceHttpClient>(
                c => c.BaseAddress = new Uri(jobsUrl));
        else
            services.AddSingleton<IJobsServiceClient, StubJobsServiceClient>();

        if (!string.IsNullOrWhiteSpace(profilesUrl))
            services.AddHttpClient<IProfilesServiceClient, ProfilesServiceHttpClient>(
                c => c.BaseAddress = new Uri(profilesUrl));
        else
            services.AddSingleton<IProfilesServiceClient, StubProfilesServiceClient>();

        // ── Auth / HTTP context ───────────────────────────────────────────────
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUserService, CurrentUserService>();

        // ── Background worker ─────────────────────────────────────────────────
        if (config.GetValue<bool>("EmbeddingWorker:Enabled", true))
            services.AddHostedService<EmbeddingWorker>();

        // ── Health checks ─────────────────────────────────────────────────────
        services.AddHealthChecks()
            .AddNpgSql(conn, name: "matching-postgres", tags: ["ready"]);

        return services;
    }
}
