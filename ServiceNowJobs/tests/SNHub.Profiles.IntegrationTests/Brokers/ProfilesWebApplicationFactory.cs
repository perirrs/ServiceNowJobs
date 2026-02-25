using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using SNHub.Profiles.Infrastructure.Persistence;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Testcontainers.PostgreSql;
using Xunit;

namespace SNHub.Profiles.IntegrationTests.Brokers;

public sealed class ProfilesWebApplicationFactory
    : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("snhub_profiles_test")
        .WithUsername("snhub_test")
        .WithPassword("snhub_test_pw")
        .WithCleanUp(true)
        .Build();

    public const string TestJwtSecret   = "INTEGRATION_TESTS_PROFILES_SECRET_32C!!";
    public const string TestJwtIssuer   = "snhub-profiles-test";
    public const string TestJwtAudience = "snhub-profiles-test";

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _ = CreateClient();
        var opts = new DbContextOptionsBuilder<ProfilesDbContext>()
            .UseNpgsql(_postgres.GetConnectionString()).Options;
        await using var db = new ProfilesDbContext(opts);
        await db.Database.EnsureCreatedAsync();
    }

    public async Task ResetDatabaseAsync()
    {
        var opts = new DbContextOptionsBuilder<ProfilesDbContext>()
            .UseNpgsql(_postgres.GetConnectionString()).Options;
        await using var db = new ProfilesDbContext(opts);
        await db.Database.ExecuteSqlRawAsync("DELETE FROM profiles.employer_profiles; DELETE FROM profiles.candidate_profiles;");
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
                ["JwtSettings:SecretKey"]                    = TestJwtSecret,
                ["JwtSettings:Issuer"]                       = TestJwtIssuer,
                ["JwtSettings:Audience"]                     = TestJwtAudience,
                ["ConnectionStrings:ProfilesDb"]             = _postgres.GetConnectionString(),
                ["RunMigrationsOnStartup"]                   = "false",
                ["Azure:BlobStorage:ConnectionString"]       = "stub",
                ["ApplicationInsights:ConnectionString"]     = "",
                ["Cors:AllowedOrigins:0"]                    = "http://localhost:3000",
                ["Serilog:MinimumLevel:Default"]             = "Warning",
            });
        });

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ProfilesDbContext>();
            services.RemoveAll<DbContextOptions<ProfilesDbContext>>();
            services.AddDbContext<ProfilesDbContext>(o =>
                o.UseNpgsql(_postgres.GetConnectionString()));

            var hcs = services
                .Where(d => d.ServiceType.FullName?.Contains("HealthCheck") == true
                         || d.ServiceType.FullName?.Contains("IHealthCheck") == true)
                .ToList();
            foreach (var hc in hcs) services.Remove(hc);
            services.AddHealthChecks();
        });
    }

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
