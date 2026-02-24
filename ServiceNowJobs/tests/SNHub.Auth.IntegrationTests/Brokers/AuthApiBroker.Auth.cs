using Xunit;
using SNHub.Auth.IntegrationTests.Models;

namespace SNHub.Auth.IntegrationTests.Brokers;

public sealed partial class AuthApiBroker
{
    private const string AuthBase = "api/v1/auth";

    // ── Register ──────────────────────────────────────────────────────────────

    public async Task<(HttpResponseMessage Raw, ApiResponse<AuthResponse>? Body)>
        RegisterAsync(RegisterRequest request)
    {
        var response = await PostAsync($"{AuthBase}/register", request);
        var body = await ReadApiResponseAsync<AuthResponse>(response);
        return (response, body);
    }

    /// <summary>Returns raw HttpResponseMessage without deserializing — for diagnostics.</summary>
    public async Task<HttpResponseMessage> RegisterRawAsync(RegisterRequest request) =>
        await PostAsync($"{AuthBase}/register", request);

    /// <summary>Returns raw HttpResponseMessage without deserializing — for diagnostics.</summary>
    public async Task<HttpResponseMessage> LoginRawAsync(LoginRequest request) =>
        await PostAsync($"{AuthBase}/login", request);

    // ── Login ─────────────────────────────────────────────────────────────────

    public async Task<(HttpResponseMessage Raw, ApiResponse<AuthResponse>? Body)>
        LoginAsync(LoginRequest request)
    {
        var response = await PostAsync($"{AuthBase}/login", request);
        var body = await ReadApiResponseAsync<AuthResponse>(response);
        return (response, body);
    }

    // ── Refresh token ─────────────────────────────────────────────────────────

    public async Task<(HttpResponseMessage Raw, ApiResponse<TokenResponse>? Body)>
        RefreshAsync(RefreshRequest request)
    {
        var response = await PostAsync($"{AuthBase}/refresh", request);
        var body = await ReadApiResponseAsync<TokenResponse>(response);
        return (response, body);
    }

    // ── Revoke token ──────────────────────────────────────────────────────────

    public async Task<HttpResponseMessage> RevokeAsync(RevokeRequest request) =>
        await PostAsync($"{AuthBase}/revoke", request);

    // ── Forgot password ───────────────────────────────────────────────────────

    public async Task<HttpResponseMessage> ForgotPasswordAsync(ForgotPasswordRequest request) =>
        await PostAsync($"{AuthBase}/forgot-password", request);

    // ── Reset password ────────────────────────────────────────────────────────

    public async Task<HttpResponseMessage> ResetPasswordAsync(ResetPasswordRequest request) =>
        await PostAsync($"{AuthBase}/reset-password", request);

    // ── Verify email (GET with query string params) ───────────────────────────

    public async Task<HttpResponseMessage> VerifyEmailAsync(VerifyEmailRequest request) =>
        await _client.GetAsync(
            $"{AuthBase}/verify-email" +
            $"?email={Uri.EscapeDataString(request.Email)}" +
            $"&token={Uri.EscapeDataString(request.Token)}");

    // ── Resend verification ───────────────────────────────────────────────────

    public async Task<HttpResponseMessage> ResendVerificationAsync(ResendVerificationRequest request) =>
        await PostAsync($"{AuthBase}/resend-verification", request);

    // ── Health ────────────────────────────────────────────────────────────────

    public async Task<HttpResponseMessage> GetHealthAsync() =>
        await _client.GetAsync("/health");
}
