using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using SNHub.Auth.Application.Interfaces;
using SNHub.Auth.Domain.Entities;
using StackExchange.Redis;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace SNHub.Auth.Infrastructure.Services;

public sealed class TokenService : ITokenService
{
    private readonly JwtSettings _settings;
    private readonly IConnectionMultiplexer _redis;

    public TokenService(IOptions<JwtSettings> settings, IConnectionMultiplexer redis)
    {
        _settings = settings.Value;
        _redis = redis;
    }

    public string GenerateAccessToken(User user)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_settings.SecretKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(JwtRegisteredClaimNames.Iat,
                DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(),
                ClaimValueTypes.Integer64),
            new("given_name", user.FirstName),
            new("family_name", user.LastName),
            new("email_verified", user.IsEmailVerified.ToString().ToLower()),
        };

        foreach (var role in user.Roles)
            claims.Add(new Claim(ClaimTypes.Role, role.ToString()));

        var token = new JwtSecurityToken(
            issuer: _settings.Issuer,
            audience: _settings.Audience,
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: DateTime.UtcNow.AddMinutes(_settings.AccessTokenExpiryMinutes),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public string GenerateRefreshToken()
    {
        var bytes = new byte[64];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        return Convert.ToBase64String(bytes);
    }

    public async Task BlacklistAccessTokenAsync(string jti, DateTimeOffset expiry, CancellationToken ct = default)
    {
        var db = _redis.GetDatabase();
        var ttl = expiry - DateTimeOffset.UtcNow;
        if (ttl > TimeSpan.Zero)
            await db.StringSetAsync($"blacklisted:{jti}", "1", ttl);
    }

    public async Task<bool> IsTokenBlacklistedAsync(string jti, CancellationToken ct = default)
    {
        var db = _redis.GetDatabase();
        return await db.KeyExistsAsync($"blacklisted:{jti}");
    }
}

public sealed class JwtSettings
{
    public const string SectionName = "JwtSettings";
    public string SecretKey { get; init; } = string.Empty;
    public string Issuer { get; init; } = string.Empty;
    public string Audience { get; init; } = string.Empty;
    public int AccessTokenExpiryMinutes { get; init; } = 15;
    public int RefreshTokenExpiryDays { get; init; } = 30;
}
