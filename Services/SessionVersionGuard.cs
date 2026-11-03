using System.Security.Claims;
using Microsoft.Extensions.Caching.Memory;
using MongoDB.Driver;
using RadarV2.Data;
using RadarV2.Data.Documents;

namespace RadarV2.Services;

/// <summary>
/// Decides whether a token that is correctly signed should still be served.
/// </summary>
/// <remarks>
/// A JWT is a signed assertion, not a row somewhere: once issued it is good until it expires, and
/// Radar's last thirty days. Nothing on the server was consulted, so changing a password changed
/// nothing for whoever was already signed in — which is the entire reason somebody resets a
/// password they think has been seen. The reset worked, said so, and left the attacker in place
/// for a month.
///
/// The fix is one number. The user document carries a version, every token is stamped with the
/// version current when it was issued, and a token whose stamp is behind the stored number is
/// refused. Raising the number is therefore "end every session on this account", and
/// <see cref="PasswordResetService"/> raises it.
///
/// Two deliberate choices in here:
///
/// <list type="bullet">
/// <item><b>Cached for <see cref="CacheFor"/>.</b> This runs on every authenticated request, and a
/// database round trip per request to read an integer that changes once a year is not a trade
/// worth making. The instance that handles a reset drops its cached value as it goes, so it is
/// exact from the next request; another instance catches up within the window. So the honest
/// bound is that a stolen token survives a reset by up to half a minute, not thirty days.</item>
/// <item><b>A failed lookup lets the request through.</b> Fail-closed would mean a momentary
/// database hiccup signs out everybody on the platform, and to exploit the open side you need a
/// stolen token and an outage at the same moment. Logged as a warning so it is not silent.</item>
/// </list>
///
/// It answers a second question on the same cached read: whether the configuration still grants
/// this account the role its token claims. Admin roles come from two lists of addresses in
/// configuration, and removing an address only demoted the account at its next sign-in - which
/// is no use against the one case that matters, somebody whose access is being withdrawn and who
/// has no reason to sign in again. They kept full admin for the remaining thirty days of the
/// token they were already holding.
/// </remarks>
public class SessionVersionGuard
{
    /// <summary>
    /// The claim carrying the version a token was issued at.
    /// </summary>
    /// <remarks>
    /// Short because it is in the header of every request. Absent means zero, which is what makes
    /// this deployable without signing out everybody holding a token issued before it.
    /// </remarks>
    public const string ClaimName = "sv";

    /// <summary>
    /// How long a read is trusted. Long enough to keep this off the hot path, short enough that
    /// another instance catches up before anybody has finished typing their new password.
    /// </summary>
    private static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(30);

    private readonly RadarDatabase _db;
    private readonly IMemoryCache _cache;
    private readonly IConfiguration _config;
    private readonly ILogger<SessionVersionGuard> _log;

    public SessionVersionGuard(RadarDatabase db, IMemoryCache cache, IConfiguration config, ILogger<SessionVersionGuard> log)
    {
        _db = db;
        _cache = cache;
        _config = config;
        _log = log;
    }

    private static string CacheKey(string userId) => $"session-version:{userId}";

    /// <summary>
    /// The little of an account this guard needs, cached together.
    /// </summary>
    /// <remarks>
    /// The address as well as the version, because the second question asked here is whether the
    /// configuration still grants this account the role its token claims - and the token carries
    /// no email, only an id. One cached read answers both; two separate reads on the auth path
    /// would not be worth a second question.
    /// </remarks>
    private sealed record Account(int TokenVersion, string Email);

    /// <summary>
    /// The version this token was stamped with. Zero when there is no stamp or it is unreadable.
    /// </summary>
    /// <remarks>
    /// Unreadable reads as zero rather than throwing. A malformed claim is either a token from
    /// before this existed or one that has been tampered with — and tampering is already caught by
    /// the signature, so there is nothing for this to defend against by being strict.
    /// </remarks>
    public static int VersionOf(ClaimsPrincipal? principal)
    {
        var raw = principal?.FindFirst(ClaimName)?.Value;
        return int.TryParse(raw, out var version) ? version : 0;
    }

    /// <summary>The stored version and address for an account.</summary>
    private async Task<Account> AccountAsync(string userId, CancellationToken ct = default)
    {
        if (_cache.TryGetValue(CacheKey(userId), out Account? cached) && cached is not null) return cached;

        // The whole document rather than a projection of the one field. Every account that
        // existed before this has no such field stored at all, and what a server-side projection
        // of a missing field deserialises to is not something to be guessing about on the auth
        // path — ordinary deserialisation leaves an absent field at its default, which is the zero
        // this needs it to be.
        var stored = await _db.Users.Find(u => u.Id == userId).FirstOrDefaultAsync(ct);

        // No such user reads as zero, with no address. Nothing in Radar deletes a user row, so the
        // only way here is a manual delete, and inventing a lockout for a case the product cannot
        // reach would make a transient miss look like a compromise.
        var account = new Account(stored?.TokenVersion ?? 0, stored?.Email ?? string.Empty);

        _cache.Set(CacheKey(userId), account, CacheFor);
        return account;
    }

    /// <summary>
    /// The write that ends every session on an account.
    /// </summary>
    /// <remarks>
    /// Handed out as an update rather than performed here so a caller can combine it with its own
    /// change and have the database apply both at once — which is what
    /// <see cref="PasswordResetService"/> needs: a new password that lands without ending the old
    /// sessions, or sessions ended without the new password landing, are both worse than either
    /// happening alone.
    ///
    /// An increment rather than a read-then-write. Two resets arriving together both have to
    /// count, and a caller holding a copy of the user document would otherwise write back the
    /// version it read and undo the other one.
    /// </remarks>
    public static UpdateDefinition<UserDocument> EndAllSessions() =>
        Builders<UserDocument>.Update.Inc(u => u.TokenVersion, 1);

    /// <summary>
    /// Drops the cached version for an account, so the next request reads the stored one.
    /// </summary>
    /// <remarks>
    /// For a caller that has raised the version itself as part of a larger write and so does not
    /// know the resulting number. Dropping costs one read on the next request; caching a guess
    /// would keep serving a session that should be over.
    /// </remarks>
    public void Forget(string userId) => _cache.Remove(CacheKey(userId));

    /// <summary>
    /// Whether a token stamped <paramref name="presented"/> passes against a stored
    /// <paramref name="current"/>.
    /// </summary>
    /// <remarks>
    /// At or above, not equal to. Somebody who has just reset their password and signed in holds a
    /// token one ahead of what another instance has cached for the next half minute, and equality
    /// would sign them straight back out of the account they were in the middle of recovering.
    /// Only a token behind the stored number — one issued before the reset — is refused.
    /// </remarks>
    public static bool Accepts(int presented, int current) => presented >= current;

    /// <summary>Whether to keep serving a token, and what to say when not.</summary>
    public sealed record Verdict(bool Ok, string? Reason = null)
    {
        public static readonly Verdict Serve = new(true);
    }

    /// <summary>
    /// Whether a token claims a role the configuration no longer grants.
    /// </summary>
    /// <remarks>
    /// The missing half of revocation. Taking an address out of the admin lists demoted the
    /// account the next time it signed in, which is no use at all against the case that matters:
    /// somebody whose access is being withdrawn is holding a thirty-day token and has no reason
    /// to sign in again. Their existing token kept full admin for a month.
    ///
    /// Checked on the claim rather than on the stored role, because the claim is what
    /// authorisation actually reads. Only ever takes access away - a token claiming User is left
    /// alone even if the address has since been granted admin, because widening a live session's
    /// powers from a background config read is not something this should be able to do. A new
    /// admin signs in again and gets a token that says so.
    /// </remarks>
    private bool StillEntitled(ClaimsPrincipal principal, Account account)
    {
        var claimed = principal.FindFirst(ClaimTypes.Role)?.Value;
        if (!AdminRoles.IsAdmin(claimed)) return true;

        // An account row with no address cannot be checked against a list of addresses. Left
        // alone rather than demoted, for the same reason a missing row reads as version zero.
        if (string.IsNullOrEmpty(account.Email)) return true;

        var entitled = AdminRoles.For(_config, account.Email);

        // Super admin keeps a platform admin token working. The claim is the lesser power, and
        // refusing it would sign out somebody who had just been promoted.
        return entitled == claimed || entitled == AdminRoles.SuperAdmin;
    }

    /// <summary>
    /// Whether a request carrying this token should still be served.
    /// </summary>
    public async Task<Verdict> CheckAsync(ClaimsPrincipal? principal, CancellationToken ct = default)
    {
        var userId = principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (principal is null || string.IsNullOrWhiteSpace(userId)) return Verdict.Serve;

        try
        {
            var account = await AccountAsync(userId, ct);

            if (!Accepts(VersionOf(principal), account.TokenVersion))
                return new Verdict(false, "This session ended when the account password was changed.");

            if (!StillEntitled(principal, account))
                return new Verdict(false, "This account no longer has admin access.");

            return Verdict.Serve;
        }
        catch (Exception ex)
        {
            // See the fail-open note on the class. Signing out the platform over a dropped
            // connection is the worse failure.
            _log.LogWarning(ex, "Could not check whether a session is still current; allowing it.");
            return Verdict.Serve;
        }
    }
}
