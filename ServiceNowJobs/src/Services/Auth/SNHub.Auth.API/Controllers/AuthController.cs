using Asp.Versioning;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SNHub.Auth.Application.Commands.ForgotPassword;
using SNHub.Auth.Application.Commands.LoginUser;
using SNHub.Auth.Application.Commands.RefreshToken;
using SNHub.Auth.Application.Commands.RegisterUser;
using SNHub.Auth.Application.Commands.ResetPassword;
using SNHub.Auth.Application.Commands.RevokeToken;
using SNHub.Auth.Application.Commands.VerifyEmail;
using SNHub.Auth.Application.DTOs;
using SNHub.Shared.Models;

namespace SNHub.Auth.API.Controllers;

/// <summary>Authentication — register, login, token management, password flows.</summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/auth")]
[Produces("application/json")]
public sealed class AuthController : ControllerBase
{
    private readonly IMediator _mediator;

    public AuthController(IMediator mediator) => _mediator = mediator;

    /// <summary>Register a new user account.</summary>
    [HttpPost("register")]
    [AllowAnonymous]
    [EnableRateLimiting("registration")]
    [ProducesResponseType(typeof(ApiResponse<AuthResponseDto>), 201)]
    [ProducesResponseType(typeof(ApiErrorResponse), 400)]
    [ProducesResponseType(typeof(ApiErrorResponse), 409)]
    public async Task<IActionResult> Register(
        [FromBody] RegisterUserCommand command, CancellationToken ct)
    {
        var result = await _mediator.Send(command, ct);
        return StatusCode(201, ApiResponse<AuthResponseDto>.Ok(result, "Registration successful."));
    }

    /// <summary>Login with email and password.</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    [ProducesResponseType(typeof(ApiResponse<AuthResponseDto>), 200)]
    [ProducesResponseType(typeof(ApiErrorResponse), 401)]
    [ProducesResponseType(typeof(ApiErrorResponse), 423)]
    public async Task<IActionResult> Login(
        [FromBody] LoginUserCommand command, CancellationToken ct)
    {
        var result = await _mediator.Send(command, ct);
        return Ok(ApiResponse<AuthResponseDto>.Ok(result, "Login successful."));
    }

    /// <summary>Refresh an expired access token.</summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    [EnableRateLimiting("token")]
    [ProducesResponseType(typeof(ApiResponse<TokenDto>), 200)]
    [ProducesResponseType(typeof(ApiErrorResponse), 401)]
    public async Task<IActionResult> Refresh(
        [FromBody] RefreshTokenCommand command, CancellationToken ct)
    {
        var result = await _mediator.Send(command, ct);
        return Ok(ApiResponse<TokenDto>.Ok(result));
    }

    /// <summary>Revoke a refresh token (logout).</summary>
    [HttpPost("revoke")]
    [AllowAnonymous]
    [ProducesResponseType(204)]
    public async Task<IActionResult> Revoke(
        [FromBody] RevokeTokenCommand command, CancellationToken ct)
    {
        await _mediator.Send(command, ct);
        return NoContent();
    }

    /// <summary>Request a password reset email.</summary>
    [HttpPost("forgot-password")]
    [AllowAnonymous]
    [EnableRateLimiting("passwordReset")]
    [ProducesResponseType(202)]
    public async Task<IActionResult> ForgotPassword(
        [FromBody] ForgotPasswordCommand command, CancellationToken ct)
    {
        await _mediator.Send(command, ct);
        // Always 202 — never reveal whether email exists
        return Accepted(ApiResponse.Ok("If that email is registered, a reset link has been sent."));
    }

    /// <summary>Complete a password reset using the token from email.</summary>
    [HttpPost("reset-password")]
    [AllowAnonymous]
    [EnableRateLimiting("passwordReset")]
    [ProducesResponseType(204)]
    [ProducesResponseType(typeof(ApiErrorResponse), 400)]
    public async Task<IActionResult> ResetPassword(
        [FromBody] ResetPasswordCommand command, CancellationToken ct)
    {
        await _mediator.Send(command, ct);
        return NoContent();
    }

    /// <summary>Verify an email address using the token from the verification email.</summary>
    [HttpGet("verify-email")]
    [AllowAnonymous]
    [ProducesResponseType(204)]
    [ProducesResponseType(typeof(ApiErrorResponse), 400)]
    public async Task<IActionResult> VerifyEmail(
        [FromQuery] string token, [FromQuery] string email, CancellationToken ct)
    {
        await _mediator.Send(new VerifyEmailCommand(email, token), ct);
        return NoContent();
    }
}
