using MongoDB.Bson;
using MongoDB.Driver;
using RadarV2.Data;
using RadarV2.Data.Documents;
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
/// <item><b>Came back</b> - did something on a later day than they signed up. The hardest of
/// the three to produce by standing over somebody's shoulder, and the closest thing here to a
/// real user. Not proof of one: nothing verifies an email address, so somebody with throwaway
/// addresses and two days of patience can manufacture this figure like any other. A prize
/// awarded on it should be gated on verified addresses, which this service cannot do for
/// itself.</item>
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
                // "No code yet" has to mean the same thing here as in the guard above, which
                // reads IsNullOrWhiteSpace. A profile holding an empty string satisfied neither
                // this filter nor that guard, so it burned all five attempts and then threw -
                // a permanent 500 on the participant's own referral screen. Nothing writes that
                // shape today; the two definitions disagreeing is the defect.
                await _db.Profiles.UpdateOneAsync(
                    Builders<UserProfile>.Filter.And(
                        Builders<UserProfile>.Filter.Eq(p => p.Id, profileId),
                        Builders<UserProfile>.Filter.Or(
                            Builders<UserProfile>.Filter.Eq(p => p.ReferralCode, null),
                            Builders<UserProfile>.Filter.Eq(p => p.ReferralCode, string.Empty))),
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
    /// <param name="AwaitingConfirmation">
    /// People who arrived with the code and whose address has not answered yet, and who are
    /// therefore in none of the three counts above.
    /// </param>
    /// <remarks>
    /// Reported rather than quietly dropped. Without it, somebody who brought in five people who
    /// have not opened their email reads as having brought in nobody, which is both discouraging
    /// and untrue - and the fix is theirs to make, by telling those people to confirm.
    /// </remarks>
    public sealed record Brought(int SignedUp, int Personalised, int CameBack, int AwaitingConfirmation);

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
        // Not themselves. Nobody can reach that shape through sign-up - the profile does not
        // exist yet when its own code would have to be found - but the consequence was measured:
        // a row attributed to itself reports "signed up 1, came back 1" and lands on its own
        // board row, so the check sits where the number is produced rather than only where the
        // attribution is written.
        var referred = await _db.Profiles
            .Find(p => p.ReferredByUserId == profileId && p.Id != profileId)
            .Project(p => new { p.Id, p.CreatedAt, p.Interests, p.Goals })
            .ToListAsync(ct);

        if (referred.Count == 0) return new Brought(0, 0, 0, 0);

        // Only accounts whose address has answered. A count is worth what the accounts behind it
        // are worth, and an address nobody has to own is how thirty of them get made in an
        // afternoon - which made "came back", the strongest of the three, manufacturable with
        // two days of patience.
        var confirmed = await ConfirmedAsync(referred.Select(p => p.Id), ct);
        var counted = referred.Where(p => confirmed.Contains(p.Id)).ToList();

        // The same rule the rest of the product uses: at least one interest and one goal.
        var personalised = counted.Count(p =>
            p.Interests.Any(i => !string.IsNullOrWhiteSpace(i))
            && p.Goals.Any(g => !string.IsNullOrWhiteSpace(g)));

        var joinedOn = counted.ToDictionary(p => p.Id, p => p.CreatedAt.Date);
        var lastSeen = await LastSeenAsync(joinedOn.Keys, ct);

        var cameBack = lastSeen.Count(seen =>
            joinedOn.TryGetValue(seen.Key, out var joined) && seen.Value.Date > joined);

        return new Brought(counted.Count, personalised, cameBack, referred.Count - counted.Count);
    }

    /// <summary>
    /// Which of these profiles belong to an address that has answered.
    /// </summary>
    /// <remarks>
    /// The flag lives on the account row and the counting works from profiles, so this is the
    /// join between them. Projected to the profile id alone, because that is all the caller
    /// compares against and the rest of an account row has no business being read here.
    /// </remarks>
    private async Task<HashSet<string>> ConfirmedAsync(IEnumerable<string> profileIds, CancellationToken ct)
    {
        var ids = profileIds.ToList();
        if (ids.Count == 0) return [];

        var confirmed = await _db.Users
            .Find(Builders<UserDocument>.Filter.And(
                Builders<UserDocument>.Filter.In(u => u.ProfileId, ids),
                Builders<UserDocument>.Filter.Ne(u => u.EmailVerifiedAt, null)))
            .Project(u => u.ProfileId)
            .ToListAsync(ct);

        return confirmed.ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>
    /// When each of these people last did anything, counted in the database.
    /// </summary>
    /// <remarks>
    /// One row per person rather than one per event. This read every event for every referred
    /// account into this process and then distinct-counted them in LINQ - five thousand referred
    /// accounts averaging a hundred events each is half a million rows and roughly thirty-five
    /// megabytes on the wire, on a screen a participant opens from Settings.
    ///
    /// The remaining ceiling is the id list, which goes into the query document: around four
    /// hundred thousand ids would pass MongoDB's 16 MB command limit. Far beyond the referred
    /// population this is counting, and the fix when it matters is a last-active date kept on
    /// the profile, which removes the join entirely.
    /// </remarks>
    private async Task<Dictionary<string, DateTime>> LastSeenAsync(IEnumerable<string> profileIds, CancellationToken ct)
    {
        var ids = profileIds.ToList();
        if (ids.Count == 0) return [];

        var rows = await _db.AnalyticsEvents.Aggregate()
            .Match(Builders<AnalyticsEvent>.Filter.In(e => e.UserId, ids))
            .Group(new BsonDocument
            {
                ["_id"] = "$UserId",
                ["last"] = new BsonDocument("$max", "$CreatedAt"),
            })
            .ToListAsync(ct);

        return rows
            .Where(row => row["_id"].IsString && row["last"].IsValidDateTime)
            .ToDictionary(row => row["_id"].AsString, row => row["last"].ToUniversalTime());
    }

    /// <summary>One row of the acquisition board.</summary>
    /// <param name="AwaitingConfirmation">
    /// People they brought whose address has not answered yet, and who are in none of the three
    /// counts. Carried so somebody with five unconfirmed sign-ups is not shown as having brought
    /// nobody - which is discouraging, untrue, and fixable by them.
    /// </param>
    public sealed record Introducer(
        string ProfileId, string Name, string? Code,
        int SignedUp, int Personalised, int CameBack, int AwaitingConfirmation);

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
            .Find(Builders<UserProfile>.Filter.And(
                Builders<UserProfile>.Filter.Ne(p => p.ReferredByUserId, null),

                // Nobody is their own referrer. Two fields compared against each other, so this
                // has to be $expr rather than a typed filter.
                new BsonDocumentFilterDefinition<UserProfile>(new BsonDocument("$expr",
                    new BsonDocument("$ne", new BsonArray { "$ReferredByUserId", "$_id" })))))
            .Project(p => new { p.Id, p.ReferredByUserId, p.CreatedAt, p.Interests, p.Goals })
            .ToListAsync(ct);

        // Confirmed addresses only, for the same reason as the panel: a board that ranks
        // unconfirmed sign-ups ranks whoever was most willing to invent addresses.
        var confirmed = await ConfirmedAsync(referred.Select(p => p.Id), ct);
        var awaiting = referred.Where(p => !confirmed.Contains(p.Id))
            .GroupBy(p => p.ReferredByUserId!)
            .ToDictionary(group => group.Key, group => group.Count());

        referred = [.. referred.Where(p => confirmed.Contains(p.Id))];

        var joinedOn = referred.ToDictionary(p => p.Id, p => p.CreatedAt.Date);
        var lastSeen = await LastSeenAsync(joinedOn.Keys, ct);

        var returners = lastSeen
            .Where(seen => joinedOn.TryGetValue(seen.Key, out var joined) && seen.Value.Date > joined)
            .Select(seen => seen.Key)
            .ToHashSet();

        var brought = referred.GroupBy(p => p.ReferredByUserId!).ToDictionary(group => group.Key, group => group.ToList());

        // Everybody who brought anybody, confirmed or not. Taking the ids from the confirmed
        // rows alone would drop an introducer whose five sign-ups have all yet to answer, and
        // the board would say they brought nobody.
        var introducerIds = brought.Keys.Union(awaiting.Keys, StringComparer.Ordinal).ToList();

        var introducers = await _db.Profiles
            .Find(Builders<UserProfile>.Filter.In(p => p.Id, introducerIds))
            .Project(p => new { p.Id, p.Name, p.ReferralCode })
            .ToListAsync(ct);

        var byId = introducers.ToDictionary(p => p.Id);

        return [.. introducerIds
            .Where(byId.ContainsKey)
            .Select(id =>
            {
                var theirs = brought.GetValueOrDefault(id, []);

                return new Introducer(
                    id,
                    byId[id].Name,
                    byId[id].ReferralCode,
                    theirs.Count,
                    theirs.Count(p => p.Interests.Any(i => !string.IsNullOrWhiteSpace(i))
                                      && p.Goals.Any(g => !string.IsNullOrWhiteSpace(g))),
                    theirs.Count(p => returners.Contains(p.Id)),
                    awaiting.GetValueOrDefault(id));
            })
            .OrderByDescending(row => row.CameBack)
            .ThenByDescending(row => row.Personalised)
            .ThenByDescending(row => row.SignedUp)
            .ThenBy(row => row.Name, StringComparer.OrdinalIgnoreCase)
            .Take(limit)];
    }
}
