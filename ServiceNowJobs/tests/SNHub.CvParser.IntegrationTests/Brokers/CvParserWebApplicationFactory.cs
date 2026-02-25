using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using SNHub.CvParser.Application.Commands.ApplyParsedCv;
using SNHub.CvParser.Application.Interfaces;
using SNHub.CvParser.Infrastructure.Persistence;
using SNHub.CvParser.Infrastructure.Services;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Testcontainers.PostgreSql;
using Xunit;

namespace SNHub.CvParser.IntegrationTests.Brokers;

public sealed class CvParserWebApplicationFactory
    : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("snhub_cvparser_test")
        .WithUsername("snhub_test")
        .WithPassword("snhub_test_pw")
        .WithCleanUp(true)
        .Build();

    public const string TestJwtSecret   = "INTEGRATION_TESTS_CVPARSER_SECRET_32!!";
    public const string TestJwtIssuer   = "snhub-cvparser-test";
    public const string TestJwtAudience = "snhub-cvparser-test";

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _ = CreateClient();
        var opts = new DbContextOptionsBuilder<CvParserDbContext>()
            .UseNpgsql(_postgres.GetConnectionString()).Options;
        await using var db = new CvParserDbContext(opts);
        await db.Database.EnsureCreatedAsync();
    }

    public async Task ResetDatabaseAsync()
    {
        var opts = new DbContextOptionsBuilder<CvParserDbContext>()
            .UseNpgsql(_postgres.GetConnectionString()).Options;
        await using var db = new CvParserDbContext(opts);
        await db.Database.ExecuteSqlRawAsync("DELETE FROM cvparser.cv_parse_results;");
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
                ["JwtSettings:SecretKey"]              = TestJwtSecret,
                ["JwtSettings:Issuer"]                 = TestJwtIssuer,
                ["JwtSettings:Audience"]               = TestJwtAudience,
                ["ConnectionStrings:CvParserDb"]       = _postgres.GetConnectionString(),
                ["RunMigrationsOnStartup"]             = "false",
                ["ApplicationInsights:ConnectionString"] = "",
                ["AzureOpenAI:Endpoint"]               = "",
                ["AzureOpenAI:ApiKey"]                 = "",
                ["Services:ProfilesServiceUrl"]        = "",
                ["Cors:AllowedOrigins:0"]              = "http://localhost:3000",
                ["Serilog:MinimumLevel:Default"]       = "Warning",
            });
        });

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<CvParserDbContext>();
            services.RemoveAll<DbContextOptions<CvParserDbContext>>();
            services.AddDbContext<CvParserDbContext>(o =>
                o.UseNpgsql(_postgres.GetConnectionString()));

            // Use stubs for external services
            services.RemoveAll<IBlobStorageService>();
            services.AddSingleton<IBlobStorageService, LocalBlobStorageService>();

            services.RemoveAll<ICvParserService>();
            services.AddSingleton<ICvParserService, StubCvParserService>();

            services.RemoveAll<IProfilesServiceClient>();
            services.AddSingleton<IProfilesServiceClient, StubProfilesServiceClient>();

            // Remove Azure clients
            services.RemoveAll<Azure.AI.OpenAI.AzureOpenAIClient>();
            services.RemoveAll<Azure.Storage.Blobs.BlobServiceClient>();

            var hcs = services
                .Where(d => d.ServiceType.FullName?.Contains("HealthCheck") == true)
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

[CollectionDefinition(nameof(CvParserApiCollection))]
public sealed class CvParserApiCollection
    : ICollectionFixture<CvParserWebApplicationFactory> { }
