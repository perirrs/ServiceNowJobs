using Azure.Communication.Email;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SNHub.Auth.Application.Interfaces;
using System.Security.Cryptography;
using System.Text;

namespace SNHub.Auth.Infrastructure.Services;

// ─── Password Hasher ─────────────────────────────────────────────────────────

/// <summary>
/// PBKDF2-SHA512 — NIST compliant, no third-party dependency, built into .NET.
/// </summary>
public sealed class PasswordHasher : IPasswordHasher
{
    private const int SaltSize = 32;
    private const int HashSize = 64;
    private const int Iterations = 600_000;

    public string HashPassword(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password),
            salt, Iterations,
            HashAlgorithmName.SHA512, HashSize);

        return $"{Iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
    }

    public bool VerifyPassword(string password, string storedHash)
    {
        if (string.IsNullOrWhiteSpace(storedHash)) return false;

        var parts = storedHash.Split('.');
        if (parts.Length != 3 || !int.TryParse(parts[0], out var iterations))
            return false;

        var salt = Convert.FromBase64String(parts[1]);
        var expected = Convert.FromBase64String(parts[2]);
        var actual = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password),
            salt, iterations,
            HashAlgorithmName.SHA512, HashSize);

        // Constant-time comparison prevents timing attacks
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}

// ─── Email Service — Azure Communication Services ─────────────────────────────

public sealed class EmailService : IEmailService
{
    private readonly AzureEmailSettings _settings;
    private readonly ILogger<EmailService> _logger;

    public EmailService(IOptions<AzureEmailSettings> settings, ILogger<EmailService> logger)
    {
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task SendEmailVerificationAsync(
        string email, string firstName, string token, CancellationToken ct = default)
    {
        var url = $"{_settings.AppBaseUrl}/auth/verify-email?token={Uri.EscapeDataString(token)}&email={Uri.EscapeDataString(email)}";
        await SendAsync(email, firstName,
            "Verify your SNHub email address",
            BuildVerificationHtml(firstName, url), ct);
    }

    public async Task SendPasswordResetAsync(
        string email, string firstName, string token, CancellationToken ct = default)
    {
        var url = $"{_settings.AppBaseUrl}/auth/reset-password?token={Uri.EscapeDataString(token)}&email={Uri.EscapeDataString(email)}";
        await SendAsync(email, firstName,
            "Reset your SNHub password",
            BuildPasswordResetHtml(firstName, url), ct);
    }

    public async Task SendWelcomeEmailAsync(
        string email, string firstName, CancellationToken ct = default)
    {
        await SendAsync(email, firstName,
            "Welcome to SNHub — The ServiceNow Platform",
            BuildWelcomeHtml(firstName), ct);
    }

    public async Task SendAccountSuspendedAsync(
        string email, string firstName, string reason, CancellationToken ct = default)
    {
        await SendAsync(email, firstName,
            "Your SNHub account has been suspended",
            $"<p>Hi {firstName},</p><p>Your account has been suspended. Reason: {reason}</p><p>Contact support@snhub.io</p>",
            ct);
    }

    private async Task SendAsync(
        string toEmail, string toName,
        string subject, string html,
        CancellationToken ct)
    {
        try
        {
            var client = new EmailClient(_settings.ConnectionString);

            var message = new EmailMessage(
                senderAddress: _settings.SenderAddress,
                recipients: new EmailRecipients([new EmailAddress(toEmail, toName)]),
                content: new EmailContent(subject) { Html = html });

            await client.SendAsync(Azure.WaitUntil.Started, message, ct);
            _logger.LogInformation("Email sent to {Email}: {Subject}", toEmail, subject);
        }
        catch (Exception ex)
        {
            // Email failure must never crash the application
            _logger.LogError(ex, "Failed to send email to {Email}", toEmail);
        }
    }

    private static string BuildVerificationHtml(string name, string url) => $@"
        <div style='font-family:Arial,sans-serif;max-width:600px;margin:0 auto'>
          <div style='background:#0A1628;padding:24px;text-align:center'>
            <h1 style='color:#00C7B1;margin:0'>SNHub</h1>
            <p style='color:#8FB8E0;margin:4px 0 0'>ServiceNow Talent Platform</p>
          </div>
          <div style='padding:32px 24px;background:#fff'>
            <h2 style='color:#1A1A2E'>Hi {name},</h2>
            <p style='color:#4B5563'>Please verify your email to complete registration.</p>
            <div style='text-align:center;margin:32px 0'>
              <a href='{url}' style='background:#1B4FD8;color:#fff;padding:14px 32px;border-radius:8px;
                 text-decoration:none;font-weight:bold'>Verify Email Address</a>
            </div>
            <p style='color:#9CA3AF;font-size:13px'>Link expires in 24 hours.</p>
          </div>
        </div>";

    private static string BuildPasswordResetHtml(string name, string url) => $@"
        <div style='font-family:Arial,sans-serif;max-width:600px;margin:0 auto'>
          <div style='background:#0A1628;padding:24px;text-align:center'>
            <h1 style='color:#00C7B1;margin:0'>SNHub</h1>
          </div>
          <div style='padding:32px 24px;background:#fff'>
            <h2 style='color:#1A1A2E'>Hi {name},</h2>
            <p style='color:#4B5563'>Click below to reset your password.</p>
            <div style='text-align:center;margin:32px 0'>
              <a href='{url}' style='background:#EF4444;color:#fff;padding:14px 32px;border-radius:8px;
                 text-decoration:none;font-weight:bold'>Reset Password</a>
            </div>
            <p style='color:#9CA3AF;font-size:13px'>Link expires in 1 hour.</p>
          </div>
        </div>";

    private static string BuildWelcomeHtml(string name) => $@"
        <div style='font-family:Arial,sans-serif;max-width:600px;margin:0 auto'>
          <div style='background:#0A1628;padding:24px;text-align:center'>
            <h1 style='color:#00C7B1;margin:0'>Welcome to SNHub</h1>
          </div>
          <div style='padding:32px 24px;background:#fff'>
            <h2 style='color:#1A1A2E'>Hi {name}! 🎉</h2>
            <p style='color:#4B5563'>You're now part of the ServiceNow talent and community platform.</p>
          </div>
        </div>";
}

public sealed class AzureEmailSettings
{
    public const string SectionName = "AzureEmail";
    public string ConnectionString { get; init; } = string.Empty;
    public string SenderAddress { get; init; } = string.Empty;
    public string AppBaseUrl { get; init; } = string.Empty;
}
