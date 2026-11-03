using MongoDB.Driver;
using RadarV2.Data;
using RadarV2.Models;

namespace RadarV2.Services;

/// <summary>
/// Who brought whom, and how many of them stayed.
/// </summary>
/// <remarks>
/// The distinction this service exists for: a sign-up is not a user. Somebody who gets thirty
/// people to create an account and walk away has not introduced Radar to thirty people - they
/// have produced thirty rows. So every count here comes in three parts, and a leaderboard built
/// on the first alone would reward exactly the behaviour nobody wants.
///
/// <list type="bullet">
/// <item><b>Signed up</b> - arrived with the code and made an account.</item>
/// <item><b>Personalised</b> - answered the three questions, so Radar has something to show
/// them. The first point at which the product can do anything for them at all.</item>
/// <item><b>Came back</b> - did something on a later day than they signed up. The only one of
/// the three that cannot be produced by standing over somebody's shoulder.</item>
/// </list>
///
/// All three are reported. Which one a prize is awarded on is a decision for whoever runs the
/// campaign, and it should be an explicit one rather than whichever number the code happened
/// to return.
/// </remarks>
public class ReferralService
{
    private readonly RadarDatabase _db;

    public ReferralService(RadarDatabase db) => _db = db;

    /// <summary>
    /// This participant's code, issuing one the first time they ask.
    /// </summary>
    /// <remarks>
    /// On request rather than at sign-up, so the collection is not filled with codes for
    /// accounts that will never share one.
    ///
    /// The retry is not decoration. The code is random and the index is unique, so two people
    /// asking at the same moment can collide - rare at seven characters, and a participant
    /// being told "something went wrong" when they go to fetch their link is the one moment
    /// the scheme cannot afford to fail.
    /// </remarks>
    public async Task<string> CodeForAsync(string profileId, CancellationToken ct = default)
    {
        var profile = await _db.Profiles.Find(p => p.Id == profileId).FirstOrDefaultAsync(ct);
        if (profile is null) throw new InvalidOperationException("No such profile.");
        if (!string.IsNullOrWhiteSpace(profile.ReferralCode)) return profile.ReferralCode;

        for (var attempt = 0; attempt < 5; attempt++)
        {
            var code = ReferralCodes.Generate();

            try
            {
                await _db.Profiles.UpdateOneAsync(
                    p => p.Id == profileId && p.ReferralCode == null,
                    Builders<UserProfile>.Update.Set(p => p.ReferralCode, code),
                    cancellationToken: ct);

                var saved = await _db.Profiles.Find(p => p.Id == profileId).FirstOrDefaultAsync(ct);
                if (!string.IsNullOrWhiteSpace(saved?.ReferralCode)) return saved.ReferralCode;
            }
            catch (MongoWriteException e) when (e.WriteError?.Category == ServerErrorCategory.DuplicateKey)
            {
                // Somebody else drew the same seven characters. Try again.
            }
        }

        throw new InvalidOperationException("Could not issue a referral code.");
    }

    /// <summary>What one participant has brought in.</summary>
    public sealed record Brought(int SignedUp, int Personalised, int CameBack);

    /// <summary>
    /// The three counts for one participant.
    /// </summary>
    /// <remarks>
    /// "Came back" is read from the events, not from the profile: an account has no last-seen
    /// field, and the honest version of "did they stay" is whether they did anything on a day
    /// after the one they joined. One session, however long, is somebody being shown Radar
    /// rather than somebody using it.
    /// </remarks>
    public async Task<Brought> BroughtByAsync(string profileId, CancellationToken ct = default)
    {
        var referred = await _db.Profiles
            .Find(p => p.ReferredByUserId == profileId)
            .Project(p => new { p.Id, p.CreatedAt, p.Interests, p.Goals })
            .ToListAsync(ct);

        if (referred.Count == 0) return new Brought(0, 0, 0);

        // The same rule the rest of the product uses: at least one interest and one goal.
        var personalised = referred.Count(p =>
            p.Interests.Any(i => !string.IsNullOrWhiteSpace(i))
            && p.Goals.Any(g => !string.IsNullOrWhiteSpace(g)));

        var ids = referred.Select(p => p.Id).ToList();
        var joinedOn = referred.ToDictionary(p => p.Id, p => p.CreatedAt.Date);

        var events = await _db.AnalyticsEvents
            .Find(Builders<AnalyticsEvent>.Filter.In(e => e.UserId, ids))
            .Project(e => new { e.UserId, e.CreatedAt })
            .ToListAsync(ct);

        var cameBack = events
            .Where(e => joinedOn.TryGetValue(e.UserId, out var joined) && e.CreatedAt.Date > joined)
            .Select(e => e.UserId)
            .Distinct()
            .Count();

        return new Brought(referred.Count, personalised, cameBack);
    }

    /// <summary>One row of the acquisition board.</summary>
    public sealed record Introducer(string ProfileId, string Name, string? Code, int SignedUp, int Personalised, int CameBack);

    /// <summary>
    /// Who has actually brought people in, best first.
    /// </summary>
    /// <remarks>
    /// Ordered by the people who came back, then by those who personalised, then by raw
    /// sign-ups. Deliberately that way round: the ordering is the campaign's incentive, and
    /// ranking on sign-ups would tell participants that thirty abandoned accounts beat five
    /// people who use Radar every week.
    ///
    /// Counted in two queries rather than one per participant, because a board that does a
    /// database round trip per row stops loading once the campaign succeeds.
    /// </remarks>
    public async Task<List<Introducer>> BoardAsync(int limit = 50, CancellationToken ct = default)
    {
        limit = Math.Clamp(limit, 1, 200);

        var referred = await _db.Profiles
            .Find(Builders<UserProfile>.Filter.Ne(p => p.ReferredByUserId, null))
            .Project(p => new { p.Id, p.ReferredByUserId, p.CreatedAt, p.Interests, p.Goals })
            .ToListAsync(ct);

        if (referred.Count == 0) return [];

        var events = await _db.AnalyticsEvents
            .Find(Builders<AnalyticsEvent>.Filter.In(e => e.UserId, referred.Select(p => p.Id)))
            .Project(e => new { e.UserId, e.CreatedAt })
            .ToListAsync(ct);

        var joinedOn = referred.ToDictionary(p => p.Id, p => p.CreatedAt.Date);

        var returners = events
            .Where(e => joinedOn.TryGetValue(e.UserId, out var joined) && e.CreatedAt.Date > joined)
            .Select(e => e.UserId)
            .ToHashSet();

        var introducerIds = referred.Select(p => p.ReferredByUserId!).Distinct().ToList();

        var introducers = await _db.Profiles
            .Find(Builders<UserProfile>.Filter.In(p => p.Id, introducerIds))
            .Project(p => new { p.Id, p.Name, p.ReferralCode })
            .ToListAsync(ct);

        var byId = introducers.ToDictionary(p => p.Id);

        return [.. referred
            .GroupBy(p => p.ReferredByUserId!)
            .Where(group => byId.ContainsKey(group.Key))
            .Select(group => new Introducer(
                group.Key,
                byId[group.Key].Name,
                byId[group.Key].ReferralCode,
                group.Count(),
                group.Count(p => p.Interests.Any(i => !string.IsNullOrWhiteSpace(i))
                                 && p.Goals.Any(g => !string.IsNullOrWhiteSpace(g))),
                group.Count(p => returners.Contains(p.Id))))
            .OrderByDescending(row => row.CameBack)
            .ThenByDescending(row => row.Personalised)
            .ThenByDescending(row => row.SignedUp)
            .Take(limit)];
    }
}
