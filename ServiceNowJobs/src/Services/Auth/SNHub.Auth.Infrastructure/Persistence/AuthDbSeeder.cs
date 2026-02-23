using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SNHub.Auth.Domain.Entities;
using SNHub.Auth.Domain.Enums;

namespace SNHub.Auth.Infrastructure.Persistence;

/// <summary>
/// Seeds essential data on first startup.
/// Creates the SuperAdmin account if it does not already exist.
/// Credentials are read from configuration / Azure Key Vault — never hardcoded.
/// </summary>
public sealed class AuthDbSeeder
{
    private readonly AuthDbContext _db;
    private readonly IConfiguration _config;
    private readonly ILogger<AuthDbSeeder> _logger;

    public AuthDbSeeder(
        AuthDbContext db,
        IConfiguration config,
        ILogger<AuthDbSeeder> logger)
    {
        _db = db;
        _config = config;
        _logger = logger;
    }

    public async Task SeedAsync(CancellationToken ct = default)
    {
        await SeedSuperAdminAsync(ct);
    }

    // ─── SuperAdmin ───────────────────────────────────────────────────────────

    private async Task SeedSuperAdminAsync(CancellationToken ct)
    {
        var email = _config["Seed:SuperAdmin:Email"];
        var password = _config["Seed:SuperAdmin:Password"];
        var firstName = _config["Seed:SuperAdmin:FirstName"] ?? "SNHub";
        var lastName = _config["Seed:SuperAdmin:LastName"] ?? "Admin";

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            _logger.LogWarning(
                "Seed:SuperAdmin:Email or Seed:SuperAdmin:Password not configured — skipping SuperAdmin seed.");
            return;
        }

        var normalizedEmail = email.ToUpperInvariant();
        var exists = await _db.Users
            .AnyAsync(u => u.NormalizedEmail == normalizedEmail, ct);

        if (exists)
        {
            _logger.LogDebug("SuperAdmin already exists — skipping seed.");
            return;
        }

        _logger.LogInformation("Seeding SuperAdmin account: {Email}", email);

        // Use PBKDF2 inline — no service injection to keep seeder self-contained
        var passwordHash = HashPassword(password);

        var admin = User.Create(
            email: email,
            passwordHash: passwordHash,
            firstName: firstName,
            lastName: lastName,
            primaryRole: UserRole.SuperAdmin,
            country: "GB",
            timeZone: "Europe/London");

        // SuperAdmin is pre-verified — no email verification flow
        admin.VerifyEmail(admin.EmailVerificationToken!);
        admin.AddRole(UserRole.Moderator);

        await _db.Users.AddAsync(admin, ct);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("SuperAdmin seeded successfully: {UserId}", admin.Id);
    }

    // ─── PBKDF2 inline helper ─────────────────────────────────────────────────

    private static string HashPassword(string password)
    {
        const int saltSize = 32;
        const int hashSize = 64;
        const int iterations = 600_000;

        var salt = System.Security.Cryptography.RandomNumberGenerator.GetBytes(saltSize);
        var hash = System.Security.Cryptography.Rfc2898DeriveBytes.Pbkdf2(
            System.Text.Encoding.UTF8.GetBytes(password),
            salt, iterations,
            System.Security.Cryptography.HashAlgorithmName.SHA512,
            hashSize);

        return $"{iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
    }
}
