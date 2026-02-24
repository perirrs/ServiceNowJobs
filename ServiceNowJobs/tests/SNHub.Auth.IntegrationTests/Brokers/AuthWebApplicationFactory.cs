using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SNHub.Auth.Application.Interfaces;
using SNHub.Auth.Infrastructure.Persistence;
using StackExchange.Redis;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;
using Xunit;

namespace SNHub.Auth.IntegrationTests.Brokers;

/// <summary>
/// Spins up real PostgreSQL and Redis containers via Testcontainers.
///
/// DESIGN NOTES
/// ─────────────
/// 1. ConfigureAppConfiguration (called before the host is built) injects ALL
///    static test settings via AddInMemoryCollection added LAST — so it wins
///    over appsettings.json regardless of file-loading order.
///    This guarantees JwtSettings:SecretKey, Issuer, Audience, etc. are correct
///    before Program.cs reads them at registration time.
///
/// 2. The two connection strings (Postgres + Redis) cannot be static because
///    Testcontainers assigns random host ports at runtime. They are injected via
///    AddInMemoryCollection in the same ConfigureAppConfiguration callback so they
///    override the placeholder values in appsettings.Testing.json.
///
/// 3. ConfigureTestServices (called AFTER app services register) replaces the
///    EF Core DbContext and Redis multiplexer registrations with ones pointing at
///    the live container ports — so the actual DI objects use the right addresses.
///
/// 4. IEmailService is replaced with a NoOp stub — no real Azure calls in tests.
///
/// 5. Health check registrations bake-in the appsettings connection strings at
///    registration time; we strip them so /health returns 200 without probing DB.
///
/// 6. Rate limiting middleware is skipped in the "Testing" environment (see Program.cs).
/// </summary>
public sealed class AuthWebApplicationFactory
    : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("snhub_auth_test")
        .WithUsername("snhub_test")
        .WithPassword("snhub_test_pw")
        .WithCleanUp(true)
        .Build();

    private readonly RedisContainer _redis = new RedisBuilder()
        .WithImage("redis:7-alpine")
        .WithCleanUp(true)
        .Build();

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await _redis.StartAsync();

        // Build the host — triggers ConfigureWebHost
        _ = CreateClient();

        // Run EF Core migrations against the live container
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        await db.Database.MigrateAsync();
    }

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

    // ── WebApplicationFactory override ────────────────────────────────────────

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Use "Testing" environment — loads appsettings.Testing.json if present,
        // and skips rate-limiter middleware (see Program.cs).
        builder.UseEnvironment("Testing");

        // ── Step 1: Inject ALL test settings via AddInMemoryCollection ─────────
        //
        // AddInMemoryCollection is appended LAST in the config pipeline so it has
        // the highest priority and overrides anything in appsettings.*.json.
        //
        // This is the only reliable way to guarantee settings are correct at the
        // moment Program.cs reads them to configure JWT bearer, rate limiter, etc.
        // File-based approaches are fragile because WebApplicationFactory sets the
        // content root to the test binary directory, and file discovery is not
        // guaranteed to complete before the builder reads configuration.
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                // ── JWT — must match between bearer validator and TokenService ─
                ["JwtSettings:SecretKey"]                = "INTEGRATION_TESTS_SECRET_KEY_32_CHARS_MIN!!",
                ["JwtSettings:Issuer"]                   = "snhub-test",
                ["JwtSettings:Audience"]                 = "snhub-test",
                ["JwtSettings:AccessTokenExpiryMinutes"] = "15",
                ["JwtSettings:RefreshTokenExpiryDays"]   = "30",

                // ── Connection strings (dynamic — overrides placeholder values) ─
                ["ConnectionStrings:AuthDb"]       = _postgres.GetConnectionString(),
                ["ConnectionStrings:Redis"]        = _redis.GetConnectionString(),
                ["ConnectionStrings:AzureStorage"] =
                    "DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;" +
                    "AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IforxEumJLLjo2SpxMs5lBXo2A==;" +
                    "BlobEndpoint=http://127.0.0.1:10000/devstoreaccount1;",

                // ── App behaviour ─────────────────────────────────────────────
                ["RunMigrationsOnStartup"] = "false",
                ["DetailedErrors"]         = "true",

                // ── Azure / external services (disabled/stubbed in tests) ─────
                ["Azure:KeyVaultUrl"]                    = "",
                ["ApplicationInsights:ConnectionString"] = "",
                ["AzureEmail:ConnectionString"]          =
                    "endpoint=https://test.communication.azure.com/;" +
                    "accesskey=AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA==",
                ["AzureEmail:SenderAddress"]             = "noreply@snhub-test.io",
                ["AzureEmail:AppBaseUrl"]                = "http://localhost:3000",
                ["AzureStorage:AccountUrl"]              = "",
                ["AzureStorage:ProfileImagesContainer"]  = "test-profiles",
                ["AzureStorage:CvContainer"]             = "test-cvs",

                // ── CORS ─────────────────────────────────────────────────────
                ["Cors:AllowedOrigins:0"] = "http://localhost:3000",

                // ── Seed (RunMigrationsOnStartup=false so this is never used) ─
                ["Seed:SuperAdmin:Email"]     = "admin@snhub-test.io",
                ["Seed:SuperAdmin:Password"]  = "Admin@Test2025!",
                ["Seed:SuperAdmin:FirstName"] = "SNHub",
                ["Seed:SuperAdmin:LastName"]  = "Admin",

                // ── Serilog — suppress noise in test output ───────────────────
                ["Serilog:MinimumLevel:Default"] = "Warning",
            });
        });

        // ── Step 2: Replace DI registrations after app services are registered ─
        builder.ConfigureTestServices(services =>
        {
            // ── Replace DbContext ──────────────────────────────────────────────
            services.RemoveAll<AuthDbContext>();
            services.RemoveAll<DbContextOptions<AuthDbContext>>();

            var dbContextFactory = services
                .FirstOrDefault(d => d.ServiceType == typeof(IDbContextFactory<AuthDbContext>));
            if (dbContextFactory is not null) services.Remove(dbContextFactory);

            services.AddDbContext<AuthDbContext>(options =>
                options.UseNpgsql(_postgres.GetConnectionString(), npgsql =>
                {
                    npgsql.MigrationsHistoryTable("__ef_migrations", "auth");
                    npgsql.CommandTimeout(60);
                    npgsql.EnableRetryOnFailure(3, TimeSpan.FromSeconds(5), null);
                }), ServiceLifetime.Scoped);

            services.RemoveAll<IUnitOfWork>();
            services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<AuthDbContext>());

            // ── Replace Redis ──────────────────────────────────────────────────
            services.RemoveAll<IConnectionMultiplexer>();

            var redisConn = _redis.GetConnectionString();
            services.AddSingleton<IConnectionMultiplexer>(_ =>
                ConnectionMultiplexer.Connect(redisConn));

            var oldCache = services
                .FirstOrDefault(d => d.ServiceType.FullName?.Contains("IDistributedCache") == true);
            if (oldCache is not null) services.Remove(oldCache);

            services.AddStackExchangeRedisCache(opts =>
            {
                opts.Configuration = redisConn;
                opts.InstanceName   = "snhub_auth_test_";
            });

            // ── Stub email — no real Azure calls in tests ──────────────────────
            services.RemoveAll<IEmailService>();
            services.AddScoped<IEmailService, NoOpEmailService>();

            // ── Remove health checks that bake-in wrong connection strings ─────
            var healthChecks = services
                .Where(d => d.ServiceType.FullName?.Contains("HealthCheck") == true
                         || d.ServiceType.FullName?.Contains("IHealthCheck") == true)
                .ToList();
            foreach (var hc in healthChecks) services.Remove(hc);
            services.AddHealthChecks();
        });
    }
}

/// <summary>No-op email service — silently discards all emails in integration tests.</summary>
internal sealed class NoOpEmailService : IEmailService
{
    public Task SendEmailVerificationAsync(string to, string name, string token, CancellationToken ct = default) => Task.CompletedTask;
    public Task SendPasswordResetAsync(string to, string name, string token, CancellationToken ct = default)     => Task.CompletedTask;
    public Task SendWelcomeEmailAsync(string to, string name, CancellationToken ct = default)                    => Task.CompletedTask;
    public Task SendAccountSuspendedAsync(string to, string name, string reason, CancellationToken ct = default) => Task.CompletedTask;
}
