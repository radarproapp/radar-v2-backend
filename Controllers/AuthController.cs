using System.ComponentModel.DataAnnotations;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using RadarV2.Services.Interfaces;

namespace RadarV2.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _auth;
    private readonly IConfiguration _config;

    public AuthController(IAuthService auth, IConfiguration config)
    {
        _auth = auth;
        _config = config;
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

    public sealed record AuthResponse(string Token, DateTime ExpiresAtUtc, string UserId, string UserName);

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

    private AuthResponse CreateResponse(AuthResult result)
    {
        var expiresAt = DateTime.UtcNow.AddDays(30);
        return new AuthResponse(
            Token: BuildToken(result.UserId!, result.UserName ?? string.Empty, expiresAt),
            ExpiresAtUtc: expiresAt,
            UserId: result.UserId!,
            UserName: result.UserName ?? string.Empty);
    }

    private string BuildToken(string userId, string userName, DateTime expiresAt)
    {
        var issuer   = _config["Auth:Jwt:Issuer"]   ?? "Radar";
        var audience = _config["Auth:Jwt:Audience"] ?? "Radar";
        var key      = _config["Auth:Jwt:Key"]      ?? "radar-dev-only-signing-key-change-me-0123456789abcdef";

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId),
            new(ClaimTypes.NameIdentifier, userId),
            new(JwtRegisteredClaimNames.UniqueName, userName),
            new(ClaimTypes.Name, userName)
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
