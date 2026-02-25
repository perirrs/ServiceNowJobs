using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SNHub.Jobs.Application.Interfaces;
using SNHub.Jobs.Infrastructure.Persistence;
using Testcontainers.PostgreSql;
using Xunit;

namespace SNHub.Jobs.IntegrationTests.Brokers;

/// <summary>
/// Spins up a real PostgreSQL container for Jobs service integration tests.
///
/// IMPORTANT — Why EnsureCreatedAsync() instead of MigrateAsync():
/// ─────────────────────────────────────────────────────────────────
/// EnableRetryOnFailure() in the DI-registered DbContext conflicts with EF Core
/// migration transactions (Npgsql requires no ambient transaction for retry logic).
/// EnsureCreatedAsync() creates the schema directly from the current model — it is
/// reliable, fast, and correct for integration tests where migration history does
/// not matter. The trigram extension/index from the migration is skipped (tests
/// use ILike which works without it).
/// </summary>
public sealed class JobsWebApplicationFactory
    : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("snhub_jobs_test")
        .WithUsername("snhub_test")
        .WithPassword("snhub_test_pw")
        .WithCleanUp(true)
        .Build();

    public const string TestJwtSecret   = "INTEGRATION_TESTS_JOBS_SECRET_KEY_32CHARS!!";
    public const string TestJwtIssuer   = "snhub-jobs-test";
    public const string TestJwtAudience = "snhub-jobs-test";

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        // Trigger host build so Services becomes available
        _ = CreateClient();

        // Create schema directly from the model — avoids retry/transaction conflicts
        // that occur with MigrateAsync() + EnableRetryOnFailure in the same context.
        var opts = new DbContextOptionsBuilder<JobsDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;

        await using var db = new JobsDbContext(opts);
        await db.Database.EnsureCreatedAsync();
    }

    public async Task ResetDatabaseAsync()
    {
        var opts = new DbContextOptionsBuilder<JobsDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;

        await using var db = new JobsDbContext(opts);
        // Use DELETE instead of TRUNCATE — safer with EnsureCreated (no sequences to reset)
        await db.Database.ExecuteSqlRawAsync("DELETE FROM jobs.jobs;");
    }

    public new async Task DisposeAsync()
    {
        await _postgres.DisposeAsync();
        await base.DisposeAsync();
    }

    // ── WebApplicationFactory override ────────────────────────────────────────

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["JwtSettings:SecretKey"]            = TestJwtSecret,
                ["JwtSettings:Issuer"]               = TestJwtIssuer,
                ["JwtSettings:Audience"]             = TestJwtAudience,
                ["ConnectionStrings:JobsDb"]         = _postgres.GetConnectionString(),
                ["RunMigrationsOnStartup"]           = "false",
                ["DetailedErrors"]                   = "true",
                ["Cors:AllowedOrigins:0"]            = "http://localhost:3000",
                ["ApplicationInsights:ConnectionString"] = "",
                ["Serilog:MinimumLevel:Default"]     = "Warning",
            });
        });

        builder.ConfigureTestServices(services =>
        {
            // Replace DbContext — use plain connection (no retry) for test reliability
            services.RemoveAll<JobsDbContext>();
            services.RemoveAll<DbContextOptions<JobsDbContext>>();

            services.AddDbContext<JobsDbContext>(options =>
                options.UseNpgsql(_postgres.GetConnectionString()), ServiceLifetime.Scoped);

            services.RemoveAll<IUnitOfWork>();
            services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<JobsDbContext>());

            // Strip health checks that bake-in wrong connection strings
            var healthChecks = services
                .Where(d => d.ServiceType.FullName?.Contains("HealthCheck") == true
                         || d.ServiceType.FullName?.Contains("IHealthCheck") == true)
                .ToList();
            foreach (var hc in healthChecks) services.Remove(hc);
            services.AddHealthChecks();
        });
    }
}
