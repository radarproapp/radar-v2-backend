using System.Security.Claims;
using Microsoft.Extensions.Caching.Memory;
using MongoDB.Driver;
using RadarV2.Data;
using RadarV2.Data.Documents;

namespace RadarV2.Services;

/// <summary>
/// Lets an account end the sessions that are already open on it.
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
    private readonly ILogger<SessionVersionGuard> _log;

    public SessionVersionGuard(RadarDatabase db, IMemoryCache cache, ILogger<SessionVersionGuard> log)
    {
        _db = db;
        _cache = cache;
        _log = log;
    }

    private static string CacheKey(string userId) => $"session-version:{userId}";

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

    /// <summary>The version an account's tokens must be at or above to be accepted.</summary>
    private async Task<int> CurrentVersionAsync(string userId, CancellationToken ct = default)
    {
        if (_cache.TryGetValue(CacheKey(userId), out int cached)) return cached;

        // The whole document rather than a projection of the one field. Every account that
        // existed before this has no such field stored at all, and what a server-side projection
        // of a missing field deserialises to is not something to be guessing about on the auth
        // path — ordinary deserialisation leaves an absent field at its default, which is the zero
        // this needs it to be.
        var stored = await _db.Users.Find(u => u.Id == userId).FirstOrDefaultAsync(ct);

        // No such user reads as zero. Nothing in Radar deletes a user row, so the only way here is
        // a manual delete, and inventing a lockout for a case the product cannot reach would make
        // a transient miss look like a compromise.
        var version = stored?.TokenVersion ?? 0;

        _cache.Set(CacheKey(userId), version, CacheFor);
        return version;
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

    /// <summary>
    /// Whether a request carrying this token should still be served.
    /// </summary>
    public async Task<bool> IsStillValidAsync(ClaimsPrincipal? principal, CancellationToken ct = default)
    {
        var userId = principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrWhiteSpace(userId)) return true;

        try
        {
            return Accepts(VersionOf(principal), await CurrentVersionAsync(userId, ct));
        }
        catch (Exception ex)
        {
            // See the fail-open note on the class. Signing out the platform over a dropped
            // connection is the worse failure.
            _log.LogWarning(ex, "Could not check whether a session is still current; allowing it.");
            return true;
        }
    }
}
