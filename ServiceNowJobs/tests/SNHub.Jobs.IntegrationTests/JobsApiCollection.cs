using Microsoft.IdentityModel.Tokens;
using SNHub.Jobs.IntegrationTests.Brokers;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Xunit;

namespace SNHub.Jobs.IntegrationTests;

[CollectionDefinition(nameof(JobsApiCollection))]
public sealed class JobsApiCollection : ICollectionFixture<JobsWebApplicationFactory> { }

/// <summary>Generates test JWT tokens that match the factory's bearer validator.</summary>
public static class TestTokenHelper
{
    public static string GenerateToken(Guid userId, string role = "Employer")
    {
        var key     = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JobsWebApplicationFactory.TestJwtSecret));
        var creds   = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var claims  = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(ClaimTypes.Role, role),
            new Claim("sub", userId.ToString()),
        };
        var token = new JwtSecurityToken(
            issuer:             JobsWebApplicationFactory.TestJwtIssuer,
            audience:           JobsWebApplicationFactory.TestJwtAudience,
            claims:             claims,
            expires:            DateTime.UtcNow.AddHours(1),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
