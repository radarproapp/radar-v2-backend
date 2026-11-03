using Microsoft.Extensions.Caching.Memory;
using MongoDB.Driver;
using RadarV2.Data;
using RadarV2.Models;

namespace RadarV2.Services;

/// <summary>
/// What role an address is entitled to, from the two places that can grant one.
/// </summary>
/// <remarks>
/// Admin access comes from the deployment's configuration, which only a deploy can change, and
/// from grants made inside the product by a super admin. Configuration wins, and nothing in the
/// admin area can edit it: it is the way back in when this collection is wrong, and a permission
/// system whose every route can be edited by whoever is already inside it has no floor.
///
/// Both callers go through here - the service that stamps a role on a new token, and the guard
/// that decides whether to keep honouring an old one - so the two cannot disagree about who is
/// an admin.
///
/// The grants are cached for <see cref="CacheFor"/>, because the guard asks on every request.
/// The honest bound that buys is that revoking access takes effect within half a minute rather
/// than instantly, which is the same bound the session guard already states for a password
/// reset, and vastly better than the thirty days a token would otherwise last.
/// </remarks>
public class AdminRoleService
{
    /// <summary>How long the grant table is trusted. Matches the session guard's window.</summary>
    private static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(30);

    private const string CacheKey = "admin-grants";

    private readonly RadarDatabase _db;
    private readonly IConfiguration _config;
    private readonly IMemoryCache _cache;
    private readonly IEmailSender _email;

    public AdminRoleService(RadarDatabase db, IConfiguration config, IMemoryCache cache, IEmailSender email)
    {
        _db = db;
        _config = config;
        _cache = cache;
        _email = email;
    }

    /// <summary>Whether an invite can be sent at all on this deployment.</summary>
    public bool CanEmail => _email.IsConfigured;

    /// <summary>
    /// Whether the configured sender accepts mail and quietly drops it.
    /// </summary>
    /// <remarks>
    /// A SendByte test key answers 201 with an id and delivers nothing, so a send "succeeds" and
    /// the invited person is never told. Reported so the screen can say the message did not
    /// really go, rather than showing a clean success for something that did nothing.
    /// </remarks>
    public bool EmailIsSandboxed => (_email as SendByteEmailSender)?.IsSandbox ?? false;

    /// <summary>Why a grant attempt was refused, or that it was not.</summary>
    /// <summary>Why a grant attempt was refused, or that it was not.</summary>
    /// <remarks>
    /// <see cref="EmailNotConfigured"/> and <see cref="CouldNotSend"/> both mean nothing was
    /// written. The invite is the message: an address granted access that is never told has a
    /// row on a screen and no idea it exists, which is indistinguishable from a typo.
    /// </remarks>
    public enum GrantResult { Granted, NotAnEmail, NotARole, AlreadyHasIt, EmailNotConfigured, CouldNotSend }

    /// <summary>Why a revoke attempt was refused, or that it was not.</summary>
    public enum RevokeResult { Revoked, NotGranted, GrantedByConfiguration, CannotRevokeYourself }

    /// <summary>One row of the admin team, from whichever source granted it.</summary>
    /// <param name="FromConfiguration">
    /// True when a deploy granted this, which is what the screen uses to say the row cannot be
    /// removed from inside the product.
    /// </param>
    public record Member(
        string Email,
        string Role,
        bool FromConfiguration,
        string? GrantedByEmail,
        DateTime? GrantedAt);

    /// <summary>An address in the form everything here compares against.</summary>
    public static string Normalise(string? email) => email?.Trim().ToLowerInvariant() ?? string.Empty;

    /// <summary>
    /// Whether this looks like an address somebody could sign in with.
    /// </summary>
    /// <remarks>
    /// Deliberately shallow. The grant is matched against whatever address the person actually
    /// registers, so a typo here produces a grant that simply never applies - not a security
    /// hole - and a strict pattern would reject real addresses for no gain. What this does stop
    /// is an empty string or a stray word becoming a row that looks like access.
    /// </remarks>
    public static bool LooksLikeAnEmail(string email) =>
        email.Length is > 4 and < 255
        && email.Count(character => character == '@') == 1
        && email.IndexOf('@') > 0
        && email.LastIndexOf('.') > email.IndexOf('@') + 1
        && email.Last() != '.'
        && !email.Any(char.IsWhiteSpace);

    // ── resolving ────────────────────────────────────────────────────────────

    private async Task<Dictionary<string, string>> GrantsAsync(CancellationToken ct = default)
    {
        if (_cache.TryGetValue(CacheKey, out Dictionary<string, string>? cached) && cached is not null) return cached;

        var active = await _db.AdminGrants
            .Find(Builders<AdminGrant>.Filter.Eq(g => g.RevokedAt, null))
            .ToListAsync(ct);

        var byEmail = active
            .Where(grant => AdminRoles.IsAdmin(grant.Role))
            .GroupBy(grant => Normalise(grant.Email))
            .ToDictionary(group => group.Key, group => group.First().Role, StringComparer.OrdinalIgnoreCase);

        _cache.Set(CacheKey, byEmail, CacheFor);
        return byEmail;
    }

    /// <summary>Drops the cached grants, so the next question reads the collection.</summary>
    /// <remarks>
    /// Called by whichever instance made the change, so it is exact there from the next request.
    /// Another instance catches up inside the cache window. Without this, the admin who just
    /// revoked somebody would watch them stay on the screen for half a minute and reasonably
    /// conclude it had not worked.
    /// </remarks>
    private void Forget() => _cache.Remove(CacheKey);

    /// <summary>
    /// The role this address is entitled to right now.
    /// </summary>
    /// <remarks>
    /// Configuration first, so a grant made in the product can never quietly reduce what a
    /// deploy said - including down to User, which is how somebody would lock the owner out.
    /// </remarks>
    public async Task<string> RoleForAsync(string? email, CancellationToken ct = default)
    {
        var configured = AdminRoles.For(_config, email);
        if (AdminRoles.IsAdmin(configured)) return configured;

        var normalised = Normalise(email);
        if (normalised.Length == 0) return AdminRoles.User;

        return (await GrantsAsync(ct)).GetValueOrDefault(normalised, AdminRoles.User);
    }

    // ── the team ─────────────────────────────────────────────────────────────

    /// <summary>Everybody who can reach the admin area, configured rows first.</summary>
    public async Task<List<Member>> TeamAsync(CancellationToken ct = default)
    {
        var configured = AdminRoles.Configured(_config)
            .Select(entry => new Member(Normalise(entry.Email), entry.Role, true, null, null))
            .ToList();

        var fromConfig = configured.Select(member => member.Email).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var granted = await _db.AdminGrants
            .Find(Builders<AdminGrant>.Filter.Eq(g => g.RevokedAt, null))
            .ToListAsync(ct);

        // An address in both places is shown once, as the configured row, because that is the
        // one that decides and the one that cannot be removed here.
        var rest = granted
            .Where(grant => AdminRoles.IsAdmin(grant.Role))
            .Where(grant => !fromConfig.Contains(Normalise(grant.Email)))
            .GroupBy(grant => Normalise(grant.Email), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(grant => grant.GrantedAt).First())
            .Select(grant => new Member(
                Normalise(grant.Email), grant.Role, false, grant.GrantedByEmail, grant.GrantedAt))
            .OrderBy(member => member.Email, StringComparer.OrdinalIgnoreCase);

        return [.. configured, .. rest];
    }

    /// <summary>
    /// Gives an address admin access, and tells the person.
    /// </summary>
    /// <remarks>
    /// The message is sent <em>before</em> the row is written, and a failure to send means
    /// nothing is written at all. That order is the whole design: writing first and then undoing
    /// on failure needs a rollback that has to know whether this was a new invite, a re-invite of
    /// somebody revoked, or a role change - three different states to restore, each a chance to
    /// restore the wrong one. Sending first has no half-state to repair.
    ///
    /// The cost is a narrow window where the message goes and the write then fails, so somebody
    /// is told about access they do not have. That is the lesser failure: they ask, and a super
    /// admin presses the button again.
    ///
    /// Nothing in here creates an account. Radar's sign-up is self-serve, so an invited person
    /// chooses their own password through the ordinary flow - the email points them at it. There
    /// is no token, and so no second way into the product to keep hardened.
    /// </remarks>
    public async Task<GrantResult> GrantAsync(string? email, string role, string grantedByEmail, CancellationToken ct = default)
    {
        var normalised = Normalise(email);
        if (!LooksLikeAnEmail(normalised)) return GrantResult.NotAnEmail;
        if (!AdminRoles.IsAdmin(role)) return GrantResult.NotARole;

        // Already entitled by a deploy. Saying so beats writing a row that changes nothing and
        // then showing it beside the configured one as though two things had happened.
        if (AdminRoles.IsAdmin(AdminRoles.For(_config, normalised))) return GrantResult.AlreadyHasIt;

        // Said plainly rather than discovered by a send that quietly does nothing. A deployment
        // with no email cannot invite anybody, and the way in is the configured lists - which
        // the error message names, because that route still works.
        if (!_email.IsConfigured) return GrantResult.EmailNotConfigured;

        if (!await TellThemAsync(normalised, role, grantedByEmail, ct)) return GrantResult.CouldNotSend;

        await _db.AdminGrants.UpdateOneAsync(
            g => g.Email == normalised,
            Builders<AdminGrant>.Update
                // On insert only, and explicitly. An upsert that does not set _id lets the server
                // generate an ObjectId, which this model cannot deserialise back into its string
                // id - so every invite would have been written and then thrown on the next read.
                .SetOnInsert(g => g.Id, Guid.NewGuid().ToString())

                .Set(g => g.Email, normalised)
                .Set(g => g.Role, role)
                .Set(g => g.GrantedByEmail, Normalise(grantedByEmail))
                .Set(g => g.GrantedAt, DateTime.UtcNow)

                // Cleared, so re-granting somebody who was revoked works rather than landing on
                // a row the resolver filters out.
                .Set(g => g.RevokedAt, null)
                .Set(g => g.RevokedByEmail, null),
            new UpdateOptions { IsUpsert = true },
            ct);

        Forget();
        return GrantResult.Granted;
    }

    /// <summary>
    /// The message an invited person gets.
    /// </summary>
    /// <remarks>
    /// It says who gave them access and what the role is, because an email granting admin rights
    /// to a product is exactly the shape of a phishing message - and the one thing that lets
    /// somebody judge it is a name they recognise beside a claim they can check.
    ///
    /// No link that does anything. It points at the ordinary sign-in page, so following it
    /// cannot be the act that grants anything, and somebody who did not expect this can ignore
    /// it with no consequence.
    /// </remarks>
    private async Task<bool> TellThemAsync(string email, string role, string grantedByEmail, CancellationToken ct)
    {
        var site = PublicSite.From(_config);
        var named = role == AdminRoles.SuperAdmin ? "Super Admin" : "Platform Admin";
        var from = Normalise(grantedByEmail);

        var (html, text) = EmailTemplate.Build(
            heading: "You have admin access to Radar",
            preheader: $"{named} access, granted by {from}.",
            paragraphs:
            [
                $"{from} has given this address {named} access to the Radar admin area.",

                // The two paths, because which one applies depends on something Radar cannot see
                // from here - whether this address already has an account.
                "If you already use Radar, sign out and sign in again and the admin area will be there.",
                "If you do not have a Radar account yet, create one with this same email address. You choose your own password.",
            ],
            action: new EmailTemplate.Action_("Go to Radar", $"{site}/admin/login"),
            footnote: $"If you were not expecting this, you can ignore it - nothing in this message grants anything on its own. Ask {from} if you are unsure.",
            publicUrl: site);

        return await _email.SendAsync(email, "You have admin access to Radar", text, html, ct);
    }

    /// <summary>Takes admin access back.</summary>
    public async Task<RevokeResult> RevokeAsync(string? email, string revokedByEmail, CancellationToken ct = default)
    {
        var normalised = Normalise(email);

        // Nobody removes their own access.
        //
        // Not politeness - it is what guarantees at least one admin is left. Every caller here
        // is an admin, so refusing to let them remove themselves means the set can never empty,
        // and a deployment with no configured admin and nobody granted is one nobody can get
        // into without a deploy.
        if (normalised == Normalise(revokedByEmail)) return RevokeResult.CannotRevokeYourself;

        // The configured lists are the way back in. Editing them from in here would remove the
        // floor this whole arrangement rests on.
        if (AdminRoles.IsAdmin(AdminRoles.For(_config, normalised))) return RevokeResult.GrantedByConfiguration;

        var result = await _db.AdminGrants.UpdateOneAsync(
            Builders<AdminGrant>.Filter.And(
                Builders<AdminGrant>.Filter.Eq(g => g.Email, normalised),
                Builders<AdminGrant>.Filter.Eq(g => g.RevokedAt, null)),
            Builders<AdminGrant>.Update
                .Set(g => g.RevokedAt, DateTime.UtcNow)
                .Set(g => g.RevokedByEmail, Normalise(revokedByEmail)),
            cancellationToken: ct);

        if (result.MatchedCount == 0) return RevokeResult.NotGranted;

        Forget();
        return RevokeResult.Revoked;
    }
}
