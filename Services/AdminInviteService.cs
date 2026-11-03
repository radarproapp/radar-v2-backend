using System.Security.Cryptography;
using System.Text;
using MongoDB.Driver;
using RadarV2.Data;
using RadarV2.Data.Documents;

namespace RadarV2.Services;

/// <summary>
/// Inviting somebody to create an admin account, and the account they create.
/// </summary>
/// <remarks>
/// An admin login is a separate account from the one the same person reads Radar with. Running
/// the product and using it are different jobs, and doing the first should not mean the second
/// is one password away - an admin session that is also a logged-in reader is a session where
/// the wrong tab can do real damage.
///
/// So this does not grant a role to an existing account. It sends a link, and the person sets a
/// password on it, and what that creates is a second row on the same address with
/// <see cref="UserDocument.Kind"/> of "Admin". Their ordinary account is untouched and keeps its
/// own password.
///
/// The token is shaped like a password reset - hashed at rest, single use, short lived - because
/// anything that creates an account and sets a password deserves the same care as the flow that
/// resets one.
/// </remarks>
public class AdminInviteService
{
    private const int TokenBytes = 32;

    /// <summary>
    /// How long an invitation lasts.
    /// </summary>
    /// <remarks>
    /// Days rather than the hour a password reset gets. A reset is answering somebody who is
    /// waiting at the screen; an invitation goes to a colleague who may be asleep, and one that
    /// expires before they open their email makes the super admin do it twice.
    /// </remarks>
    private static readonly TimeSpan Lifetime = TimeSpan.FromDays(7);

    private readonly RadarDatabase _db;
    private readonly AdminRoleService _roles;
    private readonly IEmailSender _email;
    private readonly IConfiguration _config;
    private readonly ILogger<AdminInviteService> _log;

    public AdminInviteService(
        RadarDatabase db,
        AdminRoleService roles,
        IEmailSender email,
        IConfiguration config,
        ILogger<AdminInviteService> log)
    {
        _db = db;
        _roles = roles;
        _email = email;
        _config = config;
        _log = log;
    }

    public enum InviteResult { Sent, NotAnEmail, NotARole, AlreadyHasIt, AlreadyHasAnAdminAccount, EmailNotConfigured, CouldNotSend }

    /// <summary>Why an invitation could not be redeemed, or that it was.</summary>
    public enum AcceptResult { Created, UnknownOrExpired, PasswordTooShort, AlreadyAccepted }

    private static string Hash(string token) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    /// <summary>
    /// Sends somebody a link to create an admin account.
    /// </summary>
    /// <remarks>
    /// The access is written before the message goes, and removed again if it does not. That is
    /// the opposite order from the version of this that only granted a role: there, sending first
    /// meant no half-state to repair. Here the invitation row <em>is</em> the state, so it has to
    /// exist before a link to it can be sent - and the repair is a single delete of a row nothing
    /// else has seen yet.
    /// </remarks>
    public async Task<InviteResult> InviteAsync(string? email, string role, string invitedByEmail, CancellationToken ct = default)
    {
        var normalised = AdminRoleService.Normalise(email);

        if (!AdminRoleService.LooksLikeAnEmail(normalised)) return InviteResult.NotAnEmail;
        if (!AdminRoles.IsAdmin(role)) return InviteResult.NotARole;

        // Already entitled by a deploy. Saying so beats an invitation that changes nothing.
        if (AdminRoles.IsAdmin(AdminRoles.For(_config, normalised))) return InviteResult.AlreadyHasIt;

        // An admin account already exists on this address. Sending another set-a-password link
        // would be a way to take over an admin account by inviting somebody who already has one.
        var existing = await _db.Users
            .Find(u => u.Email == normalised && u.Kind == UserDocument.AdminKind)
            .FirstOrDefaultAsync(ct);

        if (existing is not null) return InviteResult.AlreadyHasAnAdminAccount;

        if (!_email.IsConfigured) return InviteResult.EmailNotConfigured;

        // Any outstanding invitation for this address stops working. Asking again should not
        // leave two live links, and whoever asks twice expects the second to be the one.
        await _db.AdminInvites.DeleteManyAsync(i => i.Email == normalised && i.AcceptedAt == null, ct);

        var token = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(TokenBytes));

        await _db.AdminInvites.InsertOneAsync(new AdminInviteDoc
        {
            Email = normalised,
            Role = role,
            InvitedByEmail = AdminRoleService.Normalise(invitedByEmail),
            TokenHash = Hash(token),
            ExpiresAt = DateTime.UtcNow.Add(Lifetime),
        }, cancellationToken: ct);

        if (!await SendAsync(normalised, role, invitedByEmail, token, ct))
        {
            // The link never reached anybody, so the row it points at should not survive.
            await _db.AdminInvites.DeleteManyAsync(i => i.Email == normalised && i.AcceptedAt == null, ct);
            return InviteResult.CouldNotSend;
        }

        // The access itself, so the role is waiting when the account is created.
        await _roles.GrantAsync(normalised, role, invitedByEmail, ct);

        _log.LogInformation("An admin invitation was sent, granting {Role}.", role);
        return InviteResult.Sent;
    }

    /// <summary>
    /// Creates the admin account from an invitation.
    /// </summary>
    /// <remarks>
    /// No profile. An admin account is for running Radar, not reading it - so it has nothing to
    /// personalise, never appears in the user count, and cannot be mistaken for somebody using
    /// the product. The same person's ordinary account is a separate row with its own password.
    /// </remarks>
    public async Task<AcceptResult> AcceptAsync(string? token, string password, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token)) return AcceptResult.UnknownOrExpired;
        if (password.Length < 8) return AcceptResult.PasswordTooShort;

        var hash = Hash(token);

        var invite = await _db.AdminInvites.Find(i => i.TokenHash == hash).FirstOrDefaultAsync(ct);

        if (invite is null || invite.ExpiresAt < DateTime.UtcNow)
        {
            // An expired row is removed on the way out rather than left to accumulate.
            if (invite is not null) await _db.AdminInvites.DeleteOneAsync(i => i.Id == invite.Id, ct);
            return AcceptResult.UnknownOrExpired;
        }

        if (invite.AcceptedAt is not null) return AcceptResult.AlreadyAccepted;

        // Whatever the configuration or the grants say right now, not what the invitation said
        // when it was written. An invitation that sat in an inbox while the access was revoked
        // must not hand it back.
        var role = await _roles.RoleForAsync(invite.Email, ct);
        if (!AdminRoles.IsAdmin(role)) return AcceptResult.UnknownOrExpired;

        await _db.Users.InsertOneAsync(new UserDocument
        {
            Email = invite.Email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
            Role = role,
            Kind = UserDocument.AdminKind,

            // Deliberately empty. See the remark on this method.
            ProfileId = string.Empty,
        }, cancellationToken: ct);

        // Single use, consumed whether or not anything later fails.
        await _db.AdminInvites.UpdateOneAsync(
            i => i.Id == invite.Id,
            Builders<AdminInviteDoc>.Update.Set(i => i.AcceptedAt, DateTime.UtcNow),
            cancellationToken: ct);

        _log.LogInformation("An admin account was created from an invitation.");
        return AcceptResult.Created;
    }

    /// <summary>Addresses with an invitation nobody has accepted yet.</summary>
    public async Task<HashSet<string>> PendingAsync(CancellationToken ct = default)
    {
        var open = await _db.AdminInvites
            .Find(i => i.AcceptedAt == null && i.ExpiresAt > DateTime.UtcNow)
            .Project(i => i.Email)
            .ToListAsync(ct);

        return open.ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The message, and the only place the token exists outside the person's inbox.
    /// </summary>
    /// <remarks>
    /// It names who invited them and what the role is. An email offering to set a password on an
    /// admin account is exactly the shape of a phishing message, and a name the reader recognises
    /// beside a claim they can check is the one thing that lets them judge it.
    /// </remarks>
    private async Task<bool> SendAsync(string email, string role, string invitedByEmail, string token, CancellationToken ct)
    {
        var site = PublicSite.From(_config);
        var named = role == AdminRoles.SuperAdmin ? "Super Admin" : "Platform Admin";
        var from = AdminRoleService.Normalise(invitedByEmail);

        var (html, text) = EmailTemplate.Build(
            heading: "Set up your Radar admin account",
            preheader: $"{named} access, invited by {from}.",
            paragraphs:
            [
                $"{from} has invited this address to run Radar as a {named}.",
                "Choose a password on the link below. This is a separate account from any ordinary Radar account you use - it has its own password, and it is only for the admin area.",
                "The link works once and expires in seven days.",
            ],
            action: new EmailTemplate.Action_("Set your admin password", $"{site}/admin/accept?token={token}"),
            footnote: $"If you were not expecting this, ignore it - no account is created until somebody sets a password on that link. Ask {from} if you are unsure.",
            publicUrl: site);

        return await _email.SendAsync(email, "Set up your Radar admin account", text, html, ct);
    }
}
