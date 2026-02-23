using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SNHub.Auth.Infrastructure.Persistence;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;
using Xunit;

namespace SNHub.Auth.IntegrationTests.Brokers;

/// <summary>
/// Spins up real PostgreSQL and Redis containers via Testcontainers.
/// Shared across all tests in <see cref="AuthApiCollection"/> — containers
/// start once per test session, not once per test class.
/// </summary>
public sealed class AuthWebApplicationFactory
    : WebApplicationFactory<Program>, IAsyncLifetime
{
    // ── Containers ────────────────────────────────────────────────────────────

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
        .WithDatabase("snhub_auth_test")
        .WithUsername("snhub_test")
        .WithPassword("snhub_test_pw")
        .Build();

    private readonly RedisContainer _redis = new RedisBuilder()
        .WithImage("redis:7.4-alpine")
        .Build();

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    /// <summary>Start containers, then force host build and apply migrations.</summary>
    public async Task InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _redis.StartAsync());

        // Force WebApplicationFactory to build the host now, using
        // the container connection strings set via ConfigureAppConfiguration.
        // This ensures Program.cs can resolve all services including AuthDbContext.
        _ = CreateClient();

        // Apply EF Core migrations against the live test database
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        await db.Database.MigrateAsync();
    }

    /// <summary>
    /// Truncate all rows between tests (call from tests that need clean state).
    /// Uses CASCADE so FK constraints don't block truncation.
    /// </summary>
    public async Task ResetDatabaseAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        await db.Database.ExecuteSqlRawAsync(
            "TRUNCATE auth.refresh_tokens, auth.users RESTART IDENTITY CASCADE;");
    }

    public new async Task DisposeAsync()
    {
        await _postgres.DisposeAsync();
        await _redis.DisposeAsync();
        await base.DisposeAsync();
    }

    // ── WebApplicationFactory overrides ───────────────────────────────────────

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // ConfigureAppConfiguration runs before the DI container is built,
        // so these in-memory values override every other config source.
        builder.ConfigureAppConfiguration((_, cfg) =>
        {
            cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                // ── Database ───────────────────────────────────────────────────
                ["ConnectionStrings:AuthDb"]    = _postgres.GetConnectionString(),

                // ── Redis ──────────────────────────────────────────────────────
                ["ConnectionStrings:Redis"]     = _redis.GetConnectionString(),

                // ── Azure Blob Storage (fake Azurite endpoint — client is lazy)
                ["ConnectionStrings:AzureStorage"] =
                    "DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;" +
                    "AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IforxEum" +
                    "JLLjo2SpxMs5lBXo2A==;" +
                    "BlobEndpoint=http://127.0.0.1:10000/devstoreaccount1;",

                // ── Azure Blob container ───────────────────────────────────────
                ["AzureStorage:ContainerName"]  = "snhub-test-profiles",
                ["AzureStorage:AccountUrl"]     = "",  // use ConnectionString path above

                // ── Azure Communication Services (email fails silently in tests)
                ["AzureEmail:ConnectionString"] =
                    "endpoint=https://snhub-test.communication.azure.com/;" +
                    "accesskey=AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA==",
                ["AzureEmail:SenderAddress"]    = "noreply@snhub-test.io",
                ["AzureEmail:AppBaseUrl"]       = "http://localhost:3000",

                // ── JWT ────────────────────────────────────────────────────────
                ["JwtSettings:SecretKey"]                = "INTEGRATION_TEST_SECRET_KEY_MIN_32_CHARS!!",
                ["JwtSettings:Issuer"]                   = "snhub-test",
                ["JwtSettings:Audience"]                 = "snhub-test",
                ["JwtSettings:AccessTokenExpiryMinutes"] = "15",
                ["JwtSettings:RefreshTokenExpiryDays"]   = "30",

                // ── Feature flags ──────────────────────────────────────────────
                // Disable startup migration — we run it ourselves in InitializeAsync
                ["RunMigrationsOnStartup"]               = "false",

                // ── Azure Key Vault (disabled in tests) ────────────────────────
                ["Azure:KeyVaultUrl"]                    = "",

                // ── Application Insights (disabled in tests) ───────────────────
                ["ApplicationInsights:ConnectionString"] = "",

                // ── CORS ───────────────────────────────────────────────────────
                ["Cors:AllowedOrigins:0"]                = "http://localhost:3000",

                // ── Logging (quieter output in test runner) ────────────────────
                ["Serilog:MinimumLevel:Default"]         = "Warning",
                ["Serilog:MinimumLevel:Override:Microsoft.EntityFrameworkCore"] = "Warning",
            });
        });
    }
}
