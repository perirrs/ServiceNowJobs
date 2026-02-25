using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using SNHub.Applications.Application.Interfaces;
using SNHub.Applications.Domain.Enums;
using SNHub.Applications.Infrastructure.Persistence;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Testcontainers.PostgreSql;
using Xunit;

namespace SNHub.Applications.IntegrationTests.Brokers;

public sealed class ApplicationsWebApplicationFactory
    : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("snhub_applications_test")
        .WithUsername("snhub_test")
        .WithPassword("snhub_test_pw")
        .WithCleanUp(true)
        .Build();

    public const string TestJwtSecret   = "INTEGRATION_TESTS_APPS_SECRET_KEY_32CHARS!!";
    public const string TestJwtIssuer   = "snhub-applications-test";
    public const string TestJwtAudience = "snhub-applications-test";

    // Controls what plan the stub returns during a test run
    public CandidatePlan OverridePlan { get; set; } = CandidatePlan.Pro; // Pro = unlimited by default
    public int OverrideMonthlyCount { get; set; } = 0;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _ = CreateClient();

        var opts = new DbContextOptionsBuilder<ApplicationsDbContext>()
            .UseNpgsql(_postgres.GetConnectionString()).Options;
        await using var db = new ApplicationsDbContext(opts);
        await db.Database.EnsureCreatedAsync();
    }

    public async Task ResetDatabaseAsync()
    {
        var opts = new DbContextOptionsBuilder<ApplicationsDbContext>()
            .UseNpgsql(_postgres.GetConnectionString()).Options;
        await using var db = new ApplicationsDbContext(opts);
        await db.Database.ExecuteSqlRawAsync("DELETE FROM applications.applications;");
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
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["JwtSettings:SecretKey"]               = TestJwtSecret,
                ["JwtSettings:Issuer"]                  = TestJwtIssuer,
                ["JwtSettings:Audience"]                = TestJwtAudience,
                ["ConnectionStrings:ApplicationsDb"]    = _postgres.GetConnectionString(),
                ["RunMigrationsOnStartup"]              = "false",
                ["DetailedErrors"]                      = "true",
                ["Cors:AllowedOrigins:0"]               = "http://localhost:3000",
                ["ApplicationInsights:ConnectionString"] = "",
                ["Serilog:MinimumLevel:Default"]        = "Warning",
            });
        });

        builder.ConfigureTestServices(services =>
        {
            // Plain DbContext — no retry for test reliability
            services.RemoveAll<ApplicationsDbContext>();
            services.RemoveAll<DbContextOptions<ApplicationsDbContext>>();
            services.AddDbContext<ApplicationsDbContext>(options =>
                options.UseNpgsql(_postgres.GetConnectionString()), ServiceLifetime.Scoped);

            services.RemoveAll<IUnitOfWork>();
            services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<ApplicationsDbContext>());

            // Replace subscription service with controllable test stub
            services.RemoveAll<ISubscriptionService>();
            services.AddScoped<ISubscriptionService>(_ => new TestSubscriptionService(this));

            var hcs = services
                .Where(d => d.ServiceType.FullName?.Contains("HealthCheck") == true
                         || d.ServiceType.FullName?.Contains("IHealthCheck") == true)
                .ToList();
            foreach (var hc in hcs) services.Remove(hc);
            services.AddHealthChecks();
        });
    }

    // ── JWT token factory ─────────────────────────────────────────────────────

    public static string GenerateToken(Guid userId, string role = "Candidate")
    {
        var key    = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestJwtSecret));
        var creds  = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(ClaimTypes.Role, role),
            new Claim("sub", userId.ToString()),
        };
        return new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
            TestJwtIssuer, TestJwtAudience, claims,
            expires: DateTime.UtcNow.AddHours(1), signingCredentials: creds));
    }
}

/// <summary>Test stub that delegates to the factory's OverridePlan property.</summary>
internal sealed class TestSubscriptionService : ISubscriptionService
{
    private readonly ApplicationsWebApplicationFactory _factory;
    public TestSubscriptionService(ApplicationsWebApplicationFactory factory) => _factory = factory;

    public Task<CandidatePlan> GetCandidatePlanAsync(Guid candidateId, CancellationToken ct = default)
        => Task.FromResult(_factory.OverridePlan);
}
