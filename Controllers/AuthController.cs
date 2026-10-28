using Microsoft.AspNetCore.Authorization;
using System.ComponentModel.DataAnnotations;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using RadarV2.Services;
using RadarV2.Services.Interfaces;

namespace RadarV2.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _auth;
    private readonly IConfiguration _config;
    private readonly PasswordResetService _resets;
    private readonly IEmailSender _email;

    public AuthController(
        IAuthService auth,
        IConfiguration config,
        PasswordResetService resets,
        IEmailSender email)
    {
        _auth = auth;
        _config = config;
        _resets = resets;
        _email = email;
    }

    public sealed class RegisterRequest
    {
        [Required, MinLength(1)] public string Name { get; set; } = string.Empty;
        [Required, EmailAddress] public string Email { get; set; } = string.Empty;
        [Required, MinLength(8)] public string Password { get; set; } = string.Empty;
    }

    public sealed class LoginRequest
    {
        [Required, EmailAddress] public string Email { get; set; } = string.Empty;
        [Required] public string Password { get; set; } = string.Empty;
    }

    public sealed record AuthResponse(string Token, DateTime ExpiresAtUtc, string UserId, string UserName, string Role);

    [HttpPost("register")]
    public async Task<IActionResult> RegisterAsync([FromBody] RegisterRequest request)
    {
        if (!ModelState.IsValid) return ValidationProblem();

        var result = await _auth.RegisterAsync(request.Name.Trim(), request.Email.Trim(), request.Password);
        if (!result.Success)
            return Conflict(new { error = result.Error });

        return Ok(CreateResponse(result));
    }

    [HttpPost("login")]
    public async Task<IActionResult> LoginAsync([FromBody] LoginRequest request)
    {
        if (!ModelState.IsValid) return ValidationProblem();

        var result = await _auth.LoginAsync(request.Email.Trim(), request.Password);
        if (!result.Success)
            return Unauthorized(new { error = result.Error });

        return Ok(CreateResponse(result));
    }

    public sealed class ForgotPasswordRequest
    {
        [Required, EmailAddress] public string Email { get; set; } = string.Empty;
    }

    public sealed class ResetPasswordRequest
    {
        [Required] public string Token { get; set; } = string.Empty;
        [Required, MinLength(8)] public string NewPassword { get; set; } = string.Empty;
    }

    /// <summary>
    /// Starts a password reset.
    /// </summary>
    /// <remarks>
    /// Answers identically whether or not the address has an account. Saying "no such user" would
    /// turn this endpoint into a way to find out who has signed up.
    /// </remarks>
    [AllowAnonymous]
    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPasswordAsync(
        [FromBody] ForgotPasswordRequest request, CancellationToken ct = default)
    {
        if (!ModelState.IsValid) return ValidationProblem();

        // Where the link should point. Taken from configuration rather than the request, so a
        // forged Origin header cannot send somebody a reset link to a site we do not own.
        var appUrl = PublicSite.From(_config);

        await _resets.RequestAsync(request.Email, appUrl, ct);

        return Ok(new
        {
            message = "If that address has a Radar account, a reset link is on its way.",
            // Whether a link could actually be sent. False means no email provider is configured,
            // and a reset form that silently delivers nothing is worse than one that says so.
            canSend = _email.IsConfigured,
        });
    }

    /// <summary>Redeems a reset token and sets the new password.</summary>
    [AllowAnonymous]
    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPasswordAsync(
        [FromBody] ResetPasswordRequest request, CancellationToken ct = default)
    {
        if (!ModelState.IsValid) return ValidationProblem();

        var ok = await _resets.RedeemAsync(request.Token, request.NewPassword, ct);

        // One message for unknown, used and expired alike: which of the three it was is not the
        // user's business to learn by probing.
        return ok
            ? Ok(new { message = "Your password has been changed. You can sign in with it now." })
            : BadRequest(new { error = "That reset link is no longer valid. Request a new one." });
    }

    [HttpPost("~/api/admin/auth/login")]
    public async Task<IActionResult> AdminLoginAsync([FromBody] LoginRequest request)
    {
        if (!ModelState.IsValid) return ValidationProblem();

        // Use the same credential and role resolution path as regular login,
        // but never issue an admin token for a non-admin result.
        var result = await _auth.LoginAsync(request.Email.Trim(), request.Password);
        if (!result.Success || result.Role is not ("PlatformAdmin" or "SuperAdmin"))
            return Unauthorized(new { error = "Invalid email or password." });

        return Ok(CreateResponse(result));
    }

    private AuthResponse CreateResponse(AuthResult result)
    {
        var expiresAt = DateTime.UtcNow.AddDays(30);
        return new AuthResponse(
            Token: BuildToken(result.UserId!, result.UserName ?? string.Empty, result.Role, result.TokenVersion, expiresAt),
            ExpiresAtUtc: expiresAt,
            UserId: result.UserId!,
            UserName: result.UserName ?? string.Empty,
            Role: result.Role);
    }

    private string BuildToken(
        string userId, string userName, string role, int tokenVersion, DateTime expiresAt)
    {
        var issuer   = _config["Auth:Jwt:Issuer"]   ?? "Radar";
        var audience = _config["Auth:Jwt:Audience"] ?? "Radar";
        var key      = _config["Auth:Jwt:Key"]      ?? "radar-dev-only-signing-key-change-me-0123456789abcdef";

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId),
            new(ClaimTypes.NameIdentifier, userId),
            new(JwtRegisteredClaimNames.UniqueName, userName),
            new(ClaimTypes.Name, userName),
            new(ClaimTypes.Role, role),

            // What makes this token revocable. It is checked on every request against the version
            // stored on the account, so a password reset can end the sessions already open.
            new(SessionVersionGuard.ClaimName, tokenVersion.ToString())
        };

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: expiresAt,
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
                SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
