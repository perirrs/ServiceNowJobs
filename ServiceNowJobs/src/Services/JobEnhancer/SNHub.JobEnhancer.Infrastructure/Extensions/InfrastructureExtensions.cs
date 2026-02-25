using Azure;
using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SNHub.JobEnhancer.Application.Interfaces;
using SNHub.JobEnhancer.Infrastructure.Persistence;
using SNHub.JobEnhancer.Infrastructure.Services;

namespace SNHub.JobEnhancer.Infrastructure.Extensions;

public static class InfrastructureExtensions
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services, IConfiguration config)
    {
        // ── Database ──────────────────────────────────────────────────────────
        var conn = config.GetConnectionString("EnhancerDb")
            ?? throw new InvalidOperationException("EnhancerDb connection string required.");

        services.AddDbContext<JobEnhancerDbContext>(opts =>
            opts.UseNpgsql(conn, npg =>
            {
                npg.EnableRetryOnFailure(3, TimeSpan.FromSeconds(5), null);
                npg.MigrationsHistoryTable("__ef_migrations", "enhancer");
                npg.CommandTimeout(60);
            }));

        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<JobEnhancerDbContext>());
        services.AddScoped<IEnhancementResultRepository, EnhancementResultRepository>();

        // ── Azure OpenAI ──────────────────────────────────────────────────────
        var openAiEndpoint = config["AzureOpenAI:Endpoint"];
        var openAiKey      = config["AzureOpenAI:ApiKey"];

        if (!string.IsNullOrWhiteSpace(openAiEndpoint) && !string.IsNullOrWhiteSpace(openAiKey))
        {
            services.AddSingleton(_ =>
                new AzureOpenAIClient(new Uri(openAiEndpoint),
                    new System.ClientModel.ApiKeyCredential(openAiKey)));
            services.AddScoped<IJobDescriptionEnhancer, AzureOpenAiJobEnhancer>();
        }
        else if (!string.IsNullOrWhiteSpace(openAiEndpoint))
        {
            services.AddSingleton(_ =>
                new AzureOpenAIClient(new Uri(openAiEndpoint), new DefaultAzureCredential()));
            services.AddScoped<IJobDescriptionEnhancer, AzureOpenAiJobEnhancer>();
        }
        else
        {
            services.AddSingleton<IJobDescriptionEnhancer, StubJobDescriptionEnhancer>();
        }

        // ── Jobs service HTTP client ───────────────────────────────────────────
        var jobsUrl = config["Services:JobsServiceUrl"];
        if (!string.IsNullOrWhiteSpace(jobsUrl))
            services.AddHttpClient<IJobsServiceClient, JobsServiceHttpClient>(
                c => c.BaseAddress = new Uri(jobsUrl));
        else
            services.AddSingleton<IJobsServiceClient, StubJobsServiceClient>();

        // ── Auth / HTTP context ───────────────────────────────────────────────
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUserService, CurrentUserService>();

        // ── Health checks ─────────────────────────────────────────────────────
        services.AddHealthChecks()
            .AddNpgSql(conn, name: "enhancer-postgres", tags: ["ready"]);

        return services;
    }
}
