using MongoDB.Driver;
using RadarV2.Data;
using RadarV2.Data.Documents;
using RadarV2.Models;
using RadarV2.Services;

namespace RadarV2.Tests.Integration;

/// <summary>
/// Who is reading, resolved once per request.
/// </summary>
/// <remarks>
/// Authentication says who somebody is and authorisation says what they may do. Neither says what
/// they should be shown, so that was decided separately in every endpoint that serves anything -
/// <c>GetCurrentUserAsync</c> is called from a hundred and thirteen places, seventeen of them in
/// one controller, and each call was two round trips. A request to the mentor could ask the
/// database the same question thirty-four times.
///
/// Caching it is the obvious fix and it makes one specific bug easy to write: a write lands, and
/// the rest of the request goes on reading the version from before it. That bug has happened in
/// this product already, without a cache - personalising navigated home and the home screen still
/// believed the profile was unpersonalised, so it asked the three questions again, immediately
/// after they had been answered. Most of what is below guards that.
/// </remarks>
[Collection(RadarDatabaseCollection.Name)]
public class ReaderContextTests
{
    private readonly RadarDatabase _db;
    private readonly UserSessionService _session = new();

    public ReaderContextTests(RadarDatabaseFixture fixture) => _db = fixture.Database("reader-context");

    private ReaderContext Reading() => new(_db, _session);

    private async Task<UserProfile> SignedIn(string id = "u1", bool personalised = false)
    {
        var profile = new UserProfile
        {
            Id = $"profile-{id}",
            Name = "Ada",
            Interests = personalised ? ["Nursing"] : [],
            Goals = personalised ? ["find a job"] : [],
            Capabilities = personalised ? ["Research"] : [],
        };

        await _db.Profiles.InsertOneAsync(profile);
        await _db.Users.InsertOneAsync(new UserDocument { Id = id, Email = $"{id}@example.com", ProfileId = profile.Id });

        _session.SetUser(id, "Ada");
        return profile;
    }

    // ── resolving ────────────────────────────────────────────────────────────

    [Fact]
    public async Task ItResolvesTheProfileForWhoeverIsSignedIn()
    {
        var profile = await SignedIn();
        var reading = Reading();

        Assert.Equal(profile.Id, (await reading.LoadAsync())?.Id);
        Assert.True(reading.IsSignedIn);
    }

    /// <summary>Nobody signed in is a null profile, not an exception.</summary>
    /// <remarks>
    /// This runs on every request, including the ones serving anonymous callers. Throwing would
    /// turn it into a gate on the public pages.
    /// </remarks>
    [Fact]
    public async Task NobodySignedInResolvesToNothing()
    {
        var reading = Reading();

        Assert.Null(await reading.LoadAsync());
        Assert.False(reading.IsSignedIn);
    }

    /// <summary>
    /// An admin account has no profile, and that is correct rather than a miss.
    /// </summary>
    /// <remarks>
    /// An admin login exists to run Radar, not to read it, so it carries no ProfileId. Treating
    /// that as a failure is the bug that once sat an admin on a loading spinner for ever.
    /// </remarks>
    [Fact]
    public async Task AnAdminAccountWithNoProfileResolvesToNothing()
    {
        await _db.Users.InsertOneAsync(new UserDocument
        {
            Id = "admin-1",
            Email = "owner@radar.com",
            Kind = UserDocument.AdminKind,
            ProfileId = string.Empty,
        });

        _session.SetUser("admin-1", "The Admin");

        Assert.Null(await Reading().LoadAsync());
    }

    /// <summary>Reading twice asks the database once.</summary>
    /// <remarks>
    /// Measured by deleting the row out from under it: a second read that went back to the
    /// database would return nothing, so the profile still being there proves it did not.
    /// </remarks>
    [Fact]
    public async Task TheSecondReadDoesNotGoBackToTheDatabase()
    {
        var profile = await SignedIn();
        var reading = Reading();

        await reading.LoadAsync();
        await _db.Profiles.DeleteOneAsync(p => p.Id == profile.Id);

        Assert.Equal(profile.Id, (await reading.LoadAsync())?.Id);
    }

    // ── the three dimensions ─────────────────────────────────────────────────

    [Fact]
    public async Task ItCarriesWhatTheThreeQuestionsCollected()
    {
        await SignedIn(personalised: true);
        var reading = Reading();
        await reading.LoadAsync();

        Assert.Equal(["Nursing"], reading.Interests);
        Assert.Equal(["find a job"], reading.Goals);
        Assert.Equal(["Research"], reading.Capabilities);
        Assert.True(reading.IsPersonalised);
    }

    /// <summary>Somebody who has answered nothing is not personalised, and says so.</summary>
    /// <remarks>
    /// A feed ranked against an empty profile is not a personalised feed - it is whatever was
    /// ingested most recently, handed over as though it had been chosen.
    /// </remarks>
    [Fact]
    public async Task SomebodyWhoHasAnsweredNothingIsNotPersonalised()
    {
        await SignedIn();
        var reading = Reading();
        await reading.LoadAsync();

        Assert.False(reading.IsPersonalised);
        Assert.Empty(reading.EverythingSaid);
    }

    /// <summary>Blank answers are dropped, so no caller has to remember to.</summary>
    /// <remarks>
    /// A profile can hold an empty string from a tag box somebody opened and closed.
    /// </remarks>
    [Fact]
    public async Task BlankAnswersAreNotCountedAsAnswers()
    {
        var profile = await SignedIn();
        profile.Interests = ["Nursing", "", "   "];
        profile.Goals = [""];
        await _db.Profiles.ReplaceOneAsync(p => p.Id == profile.Id, profile);

        var reading = Reading();
        await reading.LoadAsync();

        Assert.Equal(["Nursing"], reading.EverythingSaid);
    }

    /// <summary>The same word said twice is one thing said.</summary>
    [Fact]
    public async Task TheSameAnswerInTwoQuestionsIsCountedOnce()
    {
        var profile = await SignedIn();
        profile.Interests = ["Nursing"];
        profile.Goals = ["nursing"];
        await _db.Profiles.ReplaceOneAsync(p => p.Id == profile.Id, profile);

        var reading = Reading();
        await reading.LoadAsync();

        Assert.Single(reading.EverythingSaid);
    }

    // ── staying current ──────────────────────────────────────────────────────

    /// <summary>
    /// A write lands, and the rest of the request reads what was written.
    /// </summary>
    /// <remarks>
    /// The bug this whole class exists for. Personalising and then rendering in the same request
    /// used to render against the unpersonalised profile, so the three questions were asked a
    /// second time immediately after being answered.
    /// </remarks>
    [Fact]
    public async Task AProfileWrittenMidRequestIsWhatTheRestOfItReads()
    {
        var profile = await SignedIn();
        var reading = Reading();
        await reading.LoadAsync();

        Assert.False(reading.IsPersonalised);

        profile.Interests = ["Nursing"];
        profile.Goals = ["find a job"];
        reading.Replace(profile);

        Assert.True(reading.IsPersonalised);
        Assert.Equal(["Nursing"], reading.Interests);
    }

    /// <summary>
    /// And a write this request does not hold sends it back to the database.
    /// </summary>
    /// <remarks>
    /// For the writers that change the profile without producing a copy of it - a referral code
    /// being issued, notification settings saved, a switch to another profile. Cheaper to re-read
    /// once than to reason about which fields moved.
    /// </remarks>
    [Fact]
    public async Task InvalidatingSendsTheNextReadBackToTheDatabase()
    {
        var profile = await SignedIn();
        var reading = Reading();
        await reading.LoadAsync();

        profile.Interests = ["Nursing"];
        await _db.Profiles.ReplaceOneAsync(p => p.Id == profile.Id, profile);

        // Still the version from before the write, which is the whole hazard of caching it.
        Assert.Empty(reading.Interests);

        reading.Invalidate();
        await reading.LoadAsync();

        Assert.Equal(["Nursing"], reading.Interests);
    }

    /// <summary>Asking for the dimensions before anything is resolved answers empty, not null.</summary>
    /// <remarks>
    /// Every one of these is read straight into a scorer or a template. A null would be an
    /// exception on a path that has no profile, which is most of the public site.
    /// </remarks>
    [Fact]
    public void TheDimensionsAreEmptyRatherThanNullBeforeAnythingIsResolved()
    {
        var reading = Reading();

        Assert.Empty(reading.Interests);
        Assert.Empty(reading.Capabilities);
        Assert.Empty(reading.Goals);
        Assert.Empty(reading.Problems);
        Assert.Empty(reading.EverythingSaid);
        Assert.Null(reading.Category);
        Assert.False(reading.IsPersonalised);
    }
}
