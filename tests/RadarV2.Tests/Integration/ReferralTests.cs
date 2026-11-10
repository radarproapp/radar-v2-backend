using MongoDB.Driver;
using RadarV2.Data;
using RadarV2.Data.Documents;
using RadarV2.Models;
using RadarV2.Services;

namespace RadarV2.Tests.Integration;

/// <summary>
/// Who brought whom, and how many of them stayed.
/// </summary>
/// <remarks>
/// The distinction the whole scheme turns on: a sign-up is not a user. Somebody who gets thirty
/// people to create an account and walk away has produced thirty rows, and a leaderboard built
/// on that number alone rewards exactly the behaviour nobody wants.
///
/// Against a real mongod, because the unique sparse index is half of what makes a code a code -
/// without sparse, every profile that has not asked for one collides on null with every other,
/// and no second account could ever be created.
/// </remarks>
[Collection(RadarDatabaseCollection.Name)]
public class ReferralTests
{
    private readonly RadarDatabase _db;
    private readonly ReferralService _referrals;

    public ReferralTests(RadarDatabaseFixture fixture)
    {
        _db = fixture.Database("referrals");
        _referrals = new ReferralService(_db);

        // Built here rather than left to the database's own setup, which runs fire-and-forget
        // from the constructor - so relying on it would make this a race, and the test would
        // pass on a machine that happened to be slow enough.
        _db.Profiles.Indexes.CreateOne(
            new CreateIndexModel<UserProfile>(
                Builders<UserProfile>.IndexKeys.Ascending(p => p.ReferralCode),
                new CreateIndexOptions { Unique = true, Sparse = true }));
    }

    /// <param name="confirmed">
    /// Whether this person's address has answered. Default true, because an unconfirmed account
    /// is counted nowhere and a test that forgot it would be asserting against zeroes.
    /// </param>
    private async Task<UserProfile> Joined(
        string id, string name, string? broughtBy = null, int daysAgo = 10,
        bool personalised = false, bool confirmed = true)
    {
        var profile = new UserProfile
        {
            Id = id,
            Name = name,
            CreatedAt = DateTime.UtcNow.AddDays(-daysAgo),
            ReferredByUserId = broughtBy,
            Interests = personalised ? ["Nursing"] : [],
            Goals = personalised ? ["find a job"] : [],
        };

        await _db.Profiles.InsertOneAsync(profile);

        // The account row beside the profile. The counts read the confirmation flag off it, so a
        // profile on its own is somebody who signed up and never answered their email.
        await _db.Users.InsertOneAsync(new UserDocument
        {
            Email = $"{id}@example.com",
            ProfileId = id,
            CreatedAt = profile.CreatedAt,
            EmailVerifiedAt = confirmed ? profile.CreatedAt : null,
        });

        return profile;
    }

    private async Task Did(string userId, int daysAgo) =>
        await _db.AnalyticsEvents.InsertOneAsync(new AnalyticsEvent
        {
            UserId = userId,
            Type = AnalyticsEventType.Open,
            CreatedAt = DateTime.UtcNow.AddDays(-daysAgo),
        });

    // ── the code ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task ACodeIsIssuedOnceAndThenReturnedAgain()
    {
        await Joined("ada", "Ada");

        var first = await _referrals.CodeForAsync("ada");
        var second = await _referrals.CodeForAsync("ada");

        Assert.Equal(first, second);
        Assert.Equal(7, first.Length);
    }

    /// <summary>
    /// Two people cannot hold the same code, and most people hold none.
    /// </summary>
    /// <remarks>
    /// The sparse half matters as much as the unique half. Without sparse, every profile that
    /// has never asked for a code collides on null with every other, and the second account
    /// anybody creates fails to save.
    /// </remarks>
    [Fact]
    public async Task ManyProfilesWithoutACodeCoexist()
    {
        for (var i = 0; i < 5; i++) await Joined($"quiet-{i}", $"Quiet {i}");

        var withNone = await _db.Profiles.CountDocumentsAsync(p => p.ReferralCode == null);

        Assert.Equal(5, withNone);
    }

    /// <summary>A code that nobody holds resolves to nobody.</summary>
    [Fact]
    public async Task AnUnknownCodeBelongsToNobody()
    {
        await Joined("ada", "Ada");
        await _referrals.CodeForAsync("ada");

        var owner = await _db.Profiles.Find(p => p.ReferralCode == "ZZZZZZZ").FirstOrDefaultAsync();

        Assert.Null(owner);
    }

    // ── what the code brought ────────────────────────────────────────────────

    /// <summary>
    /// A sign-up, somebody who answered, and somebody who came back are three numbers.
    /// </summary>
    /// <remarks>
    /// The point the campaign rests on. Collapsed into one, a participant who produced thirty
    /// abandoned accounts outranks one who introduced five people who use Radar every week.
    /// </remarks>
    [Fact]
    public async Task TheThreeCountsAreReportedSeparately()
    {
        await Joined("ada", "Ada");

        // Three sign-ups: one who did nothing, one who answered but never returned, and one
        // who answered and came back.
        await Joined("signed-only", "Signed", broughtBy: "ada");
        await Joined("answered", "Answered", broughtBy: "ada", personalised: true);
        await Joined("returned", "Returned", broughtBy: "ada", personalised: true);

        // Same day as joining is being shown Radar; a later day is using it.
        await Did("answered", daysAgo: 10);
        await Did("returned", daysAgo: 3);

        var brought = await _referrals.BroughtByAsync("ada");

        Assert.Equal(3, brought.SignedUp);
        Assert.Equal(2, brought.Personalised);
        Assert.Equal(1, brought.CameBack);
    }

    /// <summary>
    /// Activity on the day somebody joined is not coming back.
    /// </summary>
    /// <remarks>
    /// One session, however long, is somebody being shown Radar over a participant's shoulder.
    /// Counting it would make standing next to thirty people look like thirty users.
    /// </remarks>
    [Fact]
    public async Task TheSigningUpSessionIsNotAReturn()
    {
        await Joined("ada", "Ada");
        await Joined("same-day", "Same day", broughtBy: "ada", daysAgo: 4, personalised: true);

        for (var i = 0; i < 20; i++) await Did("same-day", daysAgo: 4);

        var brought = await _referrals.BroughtByAsync("ada");

        Assert.Equal(1, brought.SignedUp);
        Assert.Equal(0, brought.CameBack);
    }

    [Fact]
    public async Task SomebodyWhoHasBroughtNobodyScoresZeroRatherThanFailing()
    {
        await Joined("solo", "Solo");

        var brought = await _referrals.BroughtByAsync("solo");

        Assert.Equal(0, brought.SignedUp);
        Assert.Equal(0, brought.CameBack);
    }

    // ── the board ────────────────────────────────────────────────────────────

    /// <summary>
    /// Ranked by who stayed, not by who signed up.
    /// </summary>
    /// <remarks>
    /// The ordering is the campaign's incentive. Ranked on sign-ups, this test's loser wins -
    /// and every participant learns that thirty abandoned accounts beat two people who use
    /// Radar.
    /// </remarks>
    [Fact]
    public async Task TheBoardRanksByWhoCameBack()
    {
        await Joined("volume", "Volume");
        await Joined("quality", "Quality");

        // Five sign-ups, none of whom ever returned.
        for (var i = 0; i < 5; i++) await Joined($"ghost-{i}", $"Ghost {i}", broughtBy: "volume");

        // Two who came back.
        for (var i = 0; i < 2; i++)
        {
            await Joined($"real-{i}", $"Real {i}", broughtBy: "quality", personalised: true);
            await Did($"real-{i}", daysAgo: 2);
        }

        var board = await _referrals.BoardAsync();

        Assert.Equal("Quality", board[0].Name);
        Assert.Equal(2, board[0].CameBack);

        Assert.Equal("Volume", board[1].Name);
        Assert.Equal(5, board[1].SignedUp);
        Assert.Equal(0, board[1].CameBack);
    }

    [Fact]
    public async Task ABoardWithNoReferralsIsEmptyRatherThanAnError()
    {
        await Joined("ada", "Ada");

        Assert.Empty(await _referrals.BoardAsync());
    }

    // ── the code as somebody types it ────────────────────────────────────────

    /// <summary>
    /// A code pasted from a link, in lower case, with a hyphen and a trailing space.
    /// </summary>
    /// <remarks>
    /// All of those are the same code. Refusing them loses the sign-up the whole scheme exists
    /// to produce, over punctuation.
    ///
    /// The query-string and fragment shapes are here because the client's own suite already
    /// asserted them and this did not, so the two "mirrored" normalisers disagreed while both
    /// files claimed they could not. A share button appending "?utm_source=whatsapp" was enough
    /// to lose the attribution on the server.
    /// </remarks>
    [Theory]
    [InlineData("ABC2345")]
    [InlineData("abc2345")]
    [InlineData(" abc2345 ")]
    [InlineData("abc-2345")]
    [InlineData("radar.app/join/ABC2345")]
    [InlineData("https://www.radarproapp.com/join/ABC2345")]
    [InlineData("https://www.radarproapp.com/join/ABC2345/")]
    [InlineData("radar.app/join/ABC2345?utm_source=whatsapp")]
    [InlineData("radar.app/join/ABC2345?ref=x")]
    [InlineData("radar.app/join/ABC2345#top")]
    public void ACodeIsReadHoweverItWasTyped(string typed)
    {
        Assert.Equal("ABC2345", ReferralCodes.Normalise(typed));
    }

    /// <summary>And something that is not a code is not quietly turned into one.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("TOOSHORT")]
    [InlineData("ABC")]
    public void SomethingThatIsNotACodeIsNull(string? typed)
    {
        Assert.Null(ReferralCodes.Normalise(typed));
    }

    /// <summary>
    /// The alphabet leaves out the characters that get confused reading a code aloud.
    /// </summary>
    /// <remarks>
    /// A code is read off a phone, written on a whiteboard at a campus society meeting and
    /// typed back in by somebody else. O against 0 and I against 1 cost a sign-up every time
    /// they are confused in that chain.
    /// </remarks>
    [Fact]
    public void NoCodeContainsACharacterThatLooksLikeAnother()
    {
        for (var i = 0; i < 200; i++)
        {
            var code = ReferralCodes.Generate();

            Assert.DoesNotContain(code, c => c is 'O' or '0' or 'I' or '1' or 'L');
        }
    }

    // ── what review found ────────────────────────────────────────────────────

    /// <summary>
    /// A profile holding an empty code is issued one rather than wedged.
    /// </summary>
    /// <remarks>
    /// The guard read IsNullOrWhiteSpace and the update filter read == null, so a profile
    /// holding "" satisfied neither: all five attempts missed, the re-read never cleared the
    /// guard, and the method threw - a permanent 500 on that participant's own referral screen.
    /// Nothing writes that shape today; the two definitions disagreeing was the defect.
    /// </remarks>
    [Fact]
    public async Task AProfileHoldingAnEmptyCodeIsStillIssuedOne()
    {
        await _db.Profiles.InsertOneAsync(new UserProfile { Id = "blank", Name = "Blank", ReferralCode = "" });

        var code = await _referrals.CodeForAsync("blank");

        Assert.Equal(7, code.Length);
        Assert.Equal(code, await _referrals.CodeForAsync("blank"));
    }

    // -- the confirmation gate --------------------------------------------------

    /// <summary>
    /// A sign-up whose address has never answered is counted nowhere.
    /// </summary>
    /// <remarks>
    /// This is the point of the gate. Nothing stopped somebody creating thirty accounts on
    /// addresses they invented, which made "came back" - the strongest of the three counts, and
    /// the one a prize is most likely to ride on - manufacturable with two days of patience.
    /// </remarks>
    [Fact]
    public async Task AnUnconfirmedSignUpIsCountedNowhere()
    {
        await Joined("ada", "Ada");
        var invented = await Joined("invented", "Invented", broughtBy: "ada", daysAgo: 3, personalised: true, confirmed: false);
        await Did(invented.Id, daysAgo: 1);

        var brought = await _referrals.BroughtByAsync("ada");

        Assert.Equal(0, brought.SignedUp);
        Assert.Equal(0, brought.Personalised);
        Assert.Equal(0, brought.CameBack);
    }

    /// <summary>But it is said out loud, not dropped.</summary>
    /// <remarks>
    /// Somebody who brought five people who have not opened their email reads as having brought
    /// nobody otherwise - discouraging, untrue, and something they can fix by asking those
    /// people to confirm.
    /// </remarks>
    [Fact]
    public async Task UnconfirmedSignUpsAreReportedRatherThanHidden()
    {
        await Joined("ada", "Ada");
        await Joined("waiting-1", "One", broughtBy: "ada", confirmed: false);
        await Joined("waiting-2", "Two", broughtBy: "ada", confirmed: false);
        await Joined("answered", "Three", broughtBy: "ada");

        var brought = await _referrals.BroughtByAsync("ada");

        Assert.Equal(1, brought.SignedUp);
        Assert.Equal(2, brought.AwaitingConfirmation);
    }

    /// <summary>
    /// And somebody whose sign-ups have all yet to answer still appears on the board.
    /// </summary>
    /// <remarks>
    /// Taking the board's rows from the confirmed sign-ups alone would drop them entirely, so
    /// the screen would say they brought nobody rather than that nobody has confirmed yet.
    /// </remarks>
    [Fact]
    public async Task SomebodyWithOnlyUnconfirmedSignUpsIsStillOnTheBoard()
    {
        await Joined("ada", "Ada");
        await Joined("waiting", "Waiting", broughtBy: "ada", confirmed: false);

        var row = Assert.Single(await _referrals.BoardAsync(), r => r.ProfileId == "ada");

        Assert.Equal(0, row.SignedUp);
        Assert.Equal(1, row.AwaitingConfirmation);
    }

    /// <summary>
    /// Nobody is ever their own referrer.
    /// </summary>
    /// <remarks>
    /// Unreachable through sign-up, because the profile does not exist yet when its own code
    /// would have to be found, and nothing writes an attribution afterwards. Asserted anyway
    /// because the consequence was measured: a profile whose ReferredByUserId equals its own id
    /// reports "signed up 1, came back 1" and appears on its own board row.
    /// </remarks>
    [Fact]
    public async Task AProfileAttributedToItselfIsNotCountedAsHavingBroughtAnybody()
    {
        var self = await Joined("ada", "Ada", broughtBy: "ada", daysAgo: 3, personalised: true);
        await Did(self.Id, daysAgo: 1);

        var brought = await _referrals.BroughtByAsync("ada");

        Assert.Equal(0, brought.SignedUp);
        Assert.Equal(0, brought.CameBack);
        Assert.DoesNotContain("ada", (await _referrals.BoardAsync()).Select(row => row.ProfileId));
    }
}
