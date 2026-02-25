using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using SNHub.JobEnhancer.Application.Interfaces;
using SNHub.JobEnhancer.Infrastructure.Persistence;
using SNHub.JobEnhancer.Infrastructure.Services;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Testcontainers.PostgreSql;
using Xunit;

namespace SNHub.JobEnhancer.IntegrationTests.Brokers;

public sealed class JobEnhancerWebApplicationFactory
    : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("snhub_enhancer_test")
        .WithUsername("snhub_test")
        .WithPassword("snhub_test_pw")
        .WithCleanUp(true)
        .Build();

    public const string JwtSecret   = "INTEGRATION_TESTS_ENHANCER_SECRET_32!!";
    public const string JwtIssuer   = "snhub-enhancer-test";
    public const string JwtAudience = "snhub-enhancer-test";

    public StubJobsServiceClient JobsClient { get; } = new();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _ = CreateClient();
        var opts = new DbContextOptionsBuilder<JobEnhancerDbContext>()
            .UseNpgsql(_postgres.GetConnectionString()).Options;
        await using var db = new JobEnhancerDbContext(opts);
        await db.Database.EnsureCreatedAsync();
    }

    public async Task ResetDatabaseAsync()
    {
        var opts = new DbContextOptionsBuilder<JobEnhancerDbContext>()
            .UseNpgsql(_postgres.GetConnectionString()).Options;
        await using var db = new JobEnhancerDbContext(opts);
        await db.Database.ExecuteSqlRawAsync("DELETE FROM enhancer.enhancement_results;");
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
                ["JwtSettings:SecretKey"]         = JwtSecret,
                ["JwtSettings:Issuer"]            = JwtIssuer,
                ["JwtSettings:Audience"]          = JwtAudience,
                ["ConnectionStrings:EnhancerDb"]  = _postgres.GetConnectionString(),
                ["RunMigrationsOnStartup"]        = "false",
                ["AzureOpenAI:Endpoint"]          = "",
                ["Services:JobsServiceUrl"]       = "",
                ["ApplicationInsights:ConnectionString"] = "",
                ["Cors:AllowedOrigins:0"]         = "http://localhost:3000",
                ["Serilog:MinimumLevel:Default"]  = "Warning",
            }));

        builder.ConfigureTestServices(services =>
        {
            // Database
            services.RemoveAll<JobEnhancerDbContext>();
            services.RemoveAll<DbContextOptions<JobEnhancerDbContext>>();
            services.AddDbContext<JobEnhancerDbContext>(o =>
                o.UseNpgsql(_postgres.GetConnectionString()));

            // Use stub enhancer (no OpenAI calls)
            services.RemoveAll<IJobDescriptionEnhancer>();
            services.AddSingleton<IJobDescriptionEnhancer, StubJobDescriptionEnhancer>();

            // Use stub jobs client (records calls for verification)
            services.RemoveAll<IJobsServiceClient>();
            services.AddSingleton<IJobsServiceClient>(JobsClient);

            // Remove Azure clients
            services.RemoveAll<Azure.AI.OpenAI.AzureOpenAIClient>();

            // Remove health checks
            var hcs = services.Where(d =>
                d.ServiceType.FullName?.Contains("HealthCheck") == true).ToList();
            foreach (var hc in hcs) services.Remove(hc);
            services.AddHealthChecks();
        });
    }

    public static string GenerateToken(Guid userId, string role = "Employer")
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

[CollectionDefinition(nameof(JobEnhancerApiCollection))]
public sealed class JobEnhancerApiCollection
    : ICollectionFixture<JobEnhancerWebApplicationFactory> { }
