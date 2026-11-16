using MongoDB.Driver;
using RadarV2.Data;
using RadarV2.Models;
using RadarV2.Services.Interfaces;

namespace RadarV2.Services;

/// <summary>
/// Who is reading, and what they are entitled to see — resolved once per request.
/// </summary>
/// <remarks>
/// Authentication establishes who somebody is and authorisation establishes what they may do.
/// Neither says anything about what they should be <em>shown</em>, and so that was decided
/// separately in every endpoint that serves anything: <c>GetCurrentUserAsync</c> is called from a
/// hundred and thirteen places, seventeen of them in one controller, and each call was two round
/// trips - the account row, then the profile. A request to the mentor could therefore ask the
/// database the same question thirty-four times.
///
/// Worse than the cost: a hundred and thirteen chances to forget. Relevance applied per endpoint
/// is relevance that one new endpoint quietly skips, and the symptom is a reader seeing things
/// that have nothing to do with them.
///
/// So this is resolved once, at the edge, and every handler reads the same picture. It carries
/// the three dimensions the onboarding questions exist to collect - what somebody cares about,
/// what they can do, and what they are looking for - so the gate that uses them has a single
/// source rather than re-deriving them from a profile each time.
///
/// It decides nothing on its own. It is the input the decision is made from.
/// </remarks>
public class ReaderContext
{
    private readonly RadarDatabase _db;
    private readonly IUserSessionService _session;

    private bool _loaded;

    public ReaderContext(RadarDatabase db, IUserSessionService session)
    {
        _db = db;
        _session = session;
    }

    /// <summary>The profile this request is being served for, or null when nobody is signed in.</summary>
    public UserProfile? Profile { get; private set; }

    /// <summary>Whether somebody is signed in at all.</summary>
    public bool IsSignedIn => Profile is not null;

    /// <summary>
    /// Whether this reader has answered enough to be shown a personalised anything.
    /// </summary>
    /// <remarks>
    /// The same rule the rest of the product uses, in one place rather than re-derived. A feed
    /// ranked against an empty profile is not a personalised feed - it is whatever happened to
    /// be ingested most recently, handed over as though it were chosen.
    /// </remarks>
    public bool IsPersonalised => Profile?.IsPersonalised ?? false;

    /// <summary>Which of the three categories they chose, if they have chosen.</summary>
    public PersonaType? Category => Profile is { HasChosenCategory: true } profile ? profile.Persona : null;

    // ── the three dimensions ─────────────────────────────────────────────────

    /// <summary>What they care about.</summary>
    public IReadOnlyList<string> Interests => Profile?.Interests ?? [];

    /// <summary>What they can do, and at what level.</summary>
    /// <remarks>
    /// Carries weight in the ranker already and is populated by nothing: onboarding asks for
    /// interests, goals and obstacles, and never for skills. Exposed here so the gap is visible
    /// at the one place that reads it rather than buried in a scorer.
    /// </remarks>
    public IReadOnlyList<string> Capabilities => Profile?.Capabilities ?? [];

    /// <summary>What they are looking for.</summary>
    public IReadOnlyList<string> Goals => Profile?.Goals ?? [];

    /// <summary>What is in their way, which the third question asks about today.</summary>
    public IReadOnlyList<string> Problems => Profile?.Problems ?? [];

    /// <summary>
    /// Everything they have said about themselves, as one list.
    /// </summary>
    /// <remarks>
    /// For the matchers that do not care which question an answer came from. Blank entries are
    /// dropped here so no caller has to remember to: a profile can hold an empty string from a
    /// tag box somebody opened and closed.
    /// </remarks>
    public IReadOnlyList<string> EverythingSaid =>
        [.. new[] { Interests, Capabilities, Goals, Problems }
            .SelectMany(each => each)
            .Where(said => !string.IsNullOrWhiteSpace(said))
            .Distinct(StringComparer.OrdinalIgnoreCase)];

    // ── resolving ────────────────────────────────────────────────────────────

    /// <summary>
    /// Reads the profile, once.
    /// </summary>
    /// <remarks>
    /// Idempotent and cheap to call again, because the whole point is that the callers after the
    /// first do no work. Nobody signed in resolves to a null profile rather than an exception:
    /// plenty of endpoints serve anonymous callers, and a context that threw would make the
    /// middleware a gate on the public pages too.
    /// </remarks>
    public async Task<UserProfile?> LoadAsync(CancellationToken ct = default)
    {
        if (_loaded) return Profile;
        _loaded = true;

        if (!_session.IsAuthenticated) return null;

        var user = await _db.Users.Find(u => u.Id == _session.UserId).FirstOrDefaultAsync(ct);
        if (user is null) return null;

        // An admin login has no profile at all - it exists to run Radar, not to read it - so this
        // resolving to null for one is correct rather than a miss.
        Profile = await _db.Profiles.Find(p => p.Id == user.ProfileId).FirstOrDefaultAsync(ct);

        return Profile;
    }

    /// <summary>
    /// Replaces the resolved profile.
    /// </summary>
    /// <remarks>
    /// For the few writes that change the profile mid-request - personalising, switching
    /// profile, completing onboarding. Without this the rest of that request would go on reading
    /// the version from before the write, which is how personalising once used to leave the next
    /// screen still asking the three questions.
    /// </remarks>
    public void Replace(UserProfile? profile)
    {
        Profile = profile;
        _loaded = true;
    }

    /// <summary>
    /// Forgets what was resolved, so the next read goes back to the database.
    /// </summary>
    /// <remarks>
    /// For writes that change which profile is current, or change it in a way the caller does
    /// not hold - switching profile, creating one, a code being issued, notification settings
    /// being saved. Cheaper to re-read once than to reason about which fields moved.
    ///
    /// The failure this exists to prevent has happened before, without a cache: personalising
    /// navigated home, and the home screen still believed the profile was unpersonalised and
    /// asked the three questions again. A per-request cache makes that class of bug easier to
    /// write, so every writer clears it.
    /// </remarks>
    public void Invalidate()
    {
        Profile = null;
        _loaded = false;
    }
}
