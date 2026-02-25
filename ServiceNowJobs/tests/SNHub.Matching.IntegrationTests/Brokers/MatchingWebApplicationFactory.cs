using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using SNHub.Matching.Application.Interfaces;
using SNHub.Matching.Infrastructure.Persistence;
using SNHub.Matching.Infrastructure.Services;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Testcontainers.PostgreSql;
using Xunit;

namespace SNHub.Matching.IntegrationTests.Brokers;

public sealed class MatchingWebApplicationFactory
    : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("snhub_matching_test")
        .WithUsername("snhub_test")
        .WithPassword("snhub_test_pw")
        .WithCleanUp(true)
        .Build();

    public const string JwtSecret   = "INTEGRATION_TESTS_MATCHING_SECRET_32!!";
    public const string JwtIssuer   = "snhub-matching-test";
    public const string JwtAudience = "snhub-matching-test";

    // Publicly accessible stubs so integration tests can seed data
    public StubJobsServiceClient     Jobs     { get; } = new();
    public StubProfilesServiceClient Profiles { get; } = new();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _ = CreateClient();
        var opts = new DbContextOptionsBuilder<MatchingDbContext>()
            .UseNpgsql(_postgres.GetConnectionString()).Options;
        await using var db = new MatchingDbContext(opts);
        await db.Database.EnsureCreatedAsync();
    }

    public async Task ResetDatabaseAsync()
    {
        var opts = new DbContextOptionsBuilder<MatchingDbContext>()
            .UseNpgsql(_postgres.GetConnectionString()).Options;
        await using var db = new MatchingDbContext(opts);
        await db.Database.ExecuteSqlRawAsync("DELETE FROM matching.embedding_records;");
    }

    public new async Task DisposeAsync()
    {
        await _postgres.DisposeAsync();
        await base.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["JwtSettings:SecretKey"]          = JwtSecret,
                ["JwtSettings:Issuer"]             = JwtIssuer,
                ["JwtSettings:Audience"]           = JwtAudience,
                ["ConnectionStrings:MatchingDb"]   = _postgres.GetConnectionString(),
                ["RunMigrationsOnStartup"]         = "false",
                ["EmbeddingWorker:Enabled"]        = "false",
                ["AzureOpenAI:Endpoint"]           = "",
                ["AzureAISearch:Endpoint"]         = "",
                ["Services:JobsServiceUrl"]        = "",
                ["Services:ProfilesServiceUrl"]    = "",
                ["ApplicationInsights:ConnectionString"] = "",
                ["Cors:AllowedOrigins:0"]          = "http://localhost:3000",
                ["Serilog:MinimumLevel:Default"]   = "Warning",
            }));

        builder.ConfigureTestServices(services =>
        {
            // Database
            services.RemoveAll<MatchingDbContext>();
            services.RemoveAll<DbContextOptions<MatchingDbContext>>();
            services.AddDbContext<MatchingDbContext>(o =>
                o.UseNpgsql(_postgres.GetConnectionString()));

            // Replace with stubs
            services.RemoveAll<IEmbeddingService>();
            services.AddSingleton<IEmbeddingService, StubEmbeddingService>();

            services.RemoveAll<IVectorSearchService>();
            services.AddSingleton<IVectorSearchService, InMemoryVectorSearchService>();

            services.RemoveAll<IJobsServiceClient>();
            services.AddSingleton<IJobsServiceClient>(Jobs);

            services.RemoveAll<IProfilesServiceClient>();
            services.AddSingleton<IProfilesServiceClient>(Profiles);

            // Remove Azure clients
            services.RemoveAll<Azure.AI.OpenAI.AzureOpenAIClient>();
            services.RemoveAll<Azure.Search.Documents.Indexes.SearchIndexClient>();

            // Remove health checks (NpgSql requires real connection)
            var hcs = services.Where(d =>
                d.ServiceType.FullName?.Contains("HealthCheck") == true).ToList();
            foreach (var hc in hcs) services.Remove(hc);
            services.AddHealthChecks();
        });
    }

    public static string GenerateToken(Guid userId, string role = "Candidate")
    {
        var key    = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JwtSecret));
        var creds  = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(ClaimTypes.Role, role),
            new Claim("sub", userId.ToString()),
        };
        return new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
            JwtIssuer, JwtAudience, claims,
            expires: DateTime.UtcNow.AddHours(1), signingCredentials: creds));
    }
}

[CollectionDefinition(nameof(MatchingApiCollection))]
public sealed class MatchingApiCollection
    : ICollectionFixture<MatchingWebApplicationFactory> { }
