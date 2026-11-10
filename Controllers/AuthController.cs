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
    private readonly AdminInviteService _invites;
    private readonly EmailVerificationService _verification;

    public AuthController(
        IAuthService auth,
        IConfiguration config,
        PasswordResetService resets,
        IEmailSender email,
        AdminInviteService invites,
        EmailVerificationService verification)
    {
        _auth = auth;
        _config = config;
        _resets = resets;
        _email = email;
        _invites = invites;
        _verification = verification;
    }

    public sealed class RegisterRequest
    {
        [Required, MinLength(1)] public string Name { get; set; } = string.Empty;
        [Required, EmailAddress] public string Email { get; set; } = string.Empty;
        [Required, MinLength(8)] public string Password { get; set; } = string.Empty;

        /// <summary>
        /// Whose code brought them, if a code brought them.
        /// </summary>
        /// <remarks>
        /// Optional, and not validated. A code matching nobody is ignored rather than refused:
        /// a wrong code is somebody mistyping a friend's link, and turning that into a failed
        /// sign-up costs the exact thing the scheme exists to produce.
        /// </remarks>
        public string? ReferralCode { get; set; }
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

        var result = await _auth.RegisterAsync(request.Name.Trim(), request.Email.Trim(), request.Password, request.ReferralCode);
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

        // The admin row on this address, never the ordinary one. The same person may hold both,
        // and signing in here with their reader password must not work - that is the whole point
        // of there being two.
        //
        // The role check stays as well as the kind check. An admin account whose access has since
        // been revoked is still an admin-kind row, and it must stop working.
        var result = await _auth.LoginAsync(request.Email.Trim(), request.Password, adminAccount: true);

        // The break-glass, and the only place the two logins are allowed to meet.
        //
        // An address listed in Auth__SuperAdminEmails or Auth__AdminEmails can reach the admin
        // area with its ordinary account. Without this the first admin on a new deployment could
        // never get in: admin accounts are created by invitation, and there is nobody to send
        // one. It is also the way back when email is down or the grants are wrong, which is what
        // the configured lists are for.
        //
        // Deliberately narrow: only for an address the deployment itself vouches for, and the
        // role check below still applies.
        if (!result.Success && Services.AdminRoles.IsAdmin(Services.AdminRoles.For(_config, request.Email.Trim())))
            result = await _auth.LoginAsync(request.Email.Trim(), request.Password);

        if (!result.Success || !Services.AdminRoles.IsAdmin(result.Role))
            return Unauthorized(new { error = "Invalid email or password." });

        return Ok(CreateResponse(result));
    }

    public sealed class AcceptAdminRequest
    {
        [Required] public string Token { get; set; } = string.Empty;

        [Required, MinLength(8)] public string Password { get; set; } = string.Empty;
    }

    /// <summary>
    /// Creates an admin account from an invitation.
    /// </summary>
    /// <remarks>
    /// Anonymous, necessarily: the person doing this has no admin account yet, which is the
    /// whole point. The token is what authorises it - single use, hashed at rest, and seven days
    /// old at most.
    ///
    /// Returns no token of its own. Creating the account and being signed into it are separate
    /// steps, so a leaked link cannot also hand over a live session - whoever has it still has
    /// to know the password they just set, and the real owner finds an account they cannot sign
    /// in to rather than one somebody else is already using.
    /// </remarks>
    [AllowAnonymous]
    [HttpPost("admin/accept")]
    public async Task<IActionResult> AcceptAdminAsync([FromBody] AcceptAdminRequest request, CancellationToken ct = default)
    {
        var result = await _invites.AcceptAsync(request.Token, request.Password, ct);

        return result switch
        {
            AdminInviteService.AcceptResult.Created => NoContent(),
            AdminInviteService.AcceptResult.PasswordTooShort => BadRequest(new { error = "Use at least 8 characters." }),
            AdminInviteService.AcceptResult.AlreadyAccepted => BadRequest(new
            {
                error = "That invitation has already been used. Sign in instead, or ask for a new one.",
            }),

            // Expired, unknown and revoked-since all answer the same way. Telling them apart
            // would say whether a token was ever real, which is a question nobody holding a
            // broken link needs answered.
            _ => BadRequest(new { error = "That invitation is no longer valid. Ask for a new one." }),
        };
    }

    public sealed class VerifyEmailRequest
    {
        [Required] public string Token { get; set; } = string.Empty;
    }

    /// <summary>
    /// Records that an address answered.
    /// </summary>
    /// <remarks>
    /// Anonymous: the link arrives in an inbox, and somebody reading their mail on a phone they
    /// are not signed in on should not be told to sign in first. The token is what authorises
    /// it - single use, hashed at rest, and seven days old at most.
    ///
    /// Following the same link twice says so rather than failing. It is the ordinary thing a
    /// person does when they are not sure the first tap worked, and "invalid" would send them
    /// asking for a link they do not need.
    /// </remarks>
    [AllowAnonymous]
    [HttpPost("verify-email")]
    public async Task<IActionResult> VerifyEmailAsync([FromBody] VerifyEmailRequest request, CancellationToken ct = default)
    {
        return await _verification.VerifyAsync(request.Token, ct) switch
        {
            EmailVerificationService.VerifyResult.Verified => Ok(new { verified = true, alreadyDone = false }),
            EmailVerificationService.VerifyResult.AlreadyVerified => Ok(new { verified = true, alreadyDone = true }),
            _ => BadRequest(new { error = "That link is no longer valid. Ask for a new one from your settings." }),
        };
    }

    /// <summary>
    /// Sends the confirmation link again.
    /// </summary>
    /// <remarks>
    /// Signed in, so this cannot be used to post mail at an address somebody else owns: the only
    /// address it will ever send to is the one on the calling account. An address that has
    /// already answered is not sent to again either.
    /// </remarks>
    [Authorize]
    [HttpPost("verify-email/resend")]
    public async Task<IActionResult> ResendVerificationAsync(CancellationToken ct = default)
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrWhiteSpace(userId)) return Unauthorized(new { error = "Not signed in" });

        if (!_verification.CanEmail)
            return BadRequest(new { error = "No email is configured for this deployment, so nothing can be sent." });

        var sent = await _verification.SendAsync(userId, ct);

        // False covers two states that look the same from here and should not be told apart on a
        // screen: already answered, and the send failed. Either way there is nothing to wait for.
        return Ok(new { sent });
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
