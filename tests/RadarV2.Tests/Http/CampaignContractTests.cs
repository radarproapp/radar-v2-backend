using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;
using RadarV2.Data;
using RadarV2.Data.Documents;

namespace RadarV2.Tests.Http;

/// <summary>
/// The campaign, run end to end, and the field names the SPA reads it by.
/// </summary>
/// <remarks>
/// The two halves of this product are separate repositories, and every test on each side mocks
/// the other. So the one thing neither suite checks is whether the names match: a controller that
/// sends <c>Score</c> where the client reads <c>score</c> renders a blank where a number should
/// be, with both suites green and nothing to find in either.
///
/// That is not hypothetical here. The casing depends on ASP.NET Core defaulting to
/// <c>JsonSerializerDefaults.Web</c>, which nothing in Program.cs states out loud - it only adds
/// an enum converter - so the whole SPA contract rests on a default that a future
/// <c>AddJsonOptions</c> edit could quietly change.
///
/// These assert the names the TypeScript in src/lib/api.ts actually reads. If one is renamed on
/// either side, this fails rather than a screen going blank in production.
/// </remarks>
[Collection(RadarAppCollection.Name)]
public class CampaignContractTests
{
    private readonly RadarAppFixture _app;

    public CampaignContractTests(RadarAppFixture app) => _app = app;

    private static string SomeAddress() => $"maker-{Guid.NewGuid():N}@example.com";

    private static async Task<HttpClient> AuthorisedAsync(HttpClient client, HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", body.RootElement.GetProperty("token").GetString());

        return client;
    }

    private async Task<HttpClient> ParticipantAsync(string? referralCode = null)
    {
        var client = _app.CreateClient();

        return await AuthorisedAsync(client, await client.PostAsJsonAsync("/api/auth/register", new
        {
            name = "A Maker",
            email = SomeAddress(),
            password = "a-real-password",
            referralCode,
        }));
    }

    private async Task<HttpClient> AdminAsync()
    {
        var client = _app.CreateClient();

        await client.PostAsJsonAsync("/api/auth/register", new
        {
            name = "An Admin",
            email = RadarAppFixture.ConfiguredSuperAdmin,
            password = "a-real-password",
        });

        return await AuthorisedAsync(client, await client.PostAsJsonAsync("/api/admin/auth/login", new
        {
            email = RadarAppFixture.ConfiguredSuperAdmin,
            password = "a-real-password",
        }));
    }

    /// <summary>
    /// Marks the signed-in account's address as answered.
    /// </summary>
    /// <remarks>
    /// Written straight to the database rather than through the link, because the token exists
    /// only in the email and nothing stores it in readable form - which is the point of hashing
    /// it. What is under test here is what the counts do once an address has answered, not the
    /// mechanics of answering, which EmailVerificationTests covers.
    /// </remarks>
    private async Task ConfirmAsync(HttpClient client)
    {
        var me = await ReadAsync(await client.GetAsync("/api/me"));
        var profileId = me.GetProperty("id").GetString();

        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadarDatabase>();

        await db.Users.UpdateOneAsync(
            u => u.ProfileId == profileId,
            Builders<UserDocument>.Update
                .Set(u => u.EmailVerifiedAt, DateTime.UtcNow));
    }

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var text = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    /// <summary>Every name the client reads off this object has to be here, spelled this way.</summary>
    private static void Carries(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            Assert.True(
                element.TryGetProperty(name, out _),
                $"Expected a property named '{name}'. Got: {string.Join(", ", element.EnumerateObject().Select(p => p.Name))}");
        }
    }

    /// <summary>
    /// The whole campaign, in the order a real one happens.
    /// </summary>
    /// <remarks>
    /// One test rather than six, because the interesting part is the sequence: a submission has
    /// to exist before a queue can hold it, be marked before a board can rank it, and the counts
    /// on the board have to agree with what came back from marking it.
    /// </remarks>
    [Fact]
    public async Task AParticipantSubmits_AnAdminMarksIt_AndBothBoardsReadBack()
    {
        var maker = await ParticipantAsync();
        var link = $"https://tiktok.com/@maker/{Guid.NewGuid():N}";

        // ── submitting ───────────────────────────────────────────────────────
        var submitted = await ReadAsync(await maker.PostAsJsonAsync("/api/campaign/submissions", new
        {
            link,
            platform = "TikTok",
            note = "Shows the feed and why it ranked that way",
        }));

        Carries(submitted,
            "id", "profileId", "profileName", "link", "platform", "note",
            "submittedAt", "score", "verdict", "judgedAt", "isJudged", "judgedBy", "marks");

        var id = submitted.GetProperty("id").GetString()!;

        // Not looked at yet is a null score and a false flag, never a zero. The whole model
        // rests on those being different, and the client branches on isJudged.
        Assert.Equal(JsonValueKind.Null, submitted.GetProperty("score").ValueKind);
        Assert.False(submitted.GetProperty("isJudged").GetBoolean());

        // ── the participant's own list ───────────────────────────────────────
        var mine = await ReadAsync(await maker.GetAsync("/api/campaign/submissions/mine"));
        Assert.Equal(JsonValueKind.Array, mine.ValueKind);
        Carries(mine.EnumerateArray().Single(), "id", "link", "platform", "score", "isJudged", "marks");

        // Who marked it is a reviewer's business. A participant gets the mark and the reason.
        Assert.Equal(JsonValueKind.Null, mine.EnumerateArray().Single().GetProperty("judgedBy").ValueKind);

        // ── the review queue ─────────────────────────────────────────────────
        var admin = await AdminAsync();

        var queue = await ReadAsync(await admin.GetAsync("/api/campaign/submissions"));
        Carries(queue, "waiting", "recentlyJudged");

        var waiting = queue.GetProperty("waiting").EnumerateArray().Single(e => e.GetProperty("id").GetString() == id);
        Carries(waiting, "id", "profileName", "link", "platform", "note", "submittedAt", "isJudged");

        // ── marking it ───────────────────────────────────────────────────────
        var judged = await admin.PostAsJsonAsync($"/api/campaign/submissions/{id}/judge", new
        {
            score = 9,
            verdict = "Shows a real feed and says why it ranked that way",
        });

        Assert.Equal(HttpStatusCode.NoContent, judged.StatusCode);

        // ── the content board ────────────────────────────────────────────────
        var board = await ReadAsync(await admin.GetAsync("/api/campaign/board"));
        Carries(board, "makers", "awaitingReview", "rankedOn");

        var made = board.GetProperty("makers").EnumerateArray().Single();
        Carries(made, "profileId", "name", "best", "judged", "submitted");

        Assert.Equal(9, made.GetProperty("best").GetInt32());

        // Submitted and judged are different facts, and the board reports both.
        Assert.Equal(1, made.GetProperty("judged").GetInt32());
        Assert.Equal(1, made.GetProperty("submitted").GetInt32());

        // ── and the participant sees the mark, with its reason ───────────────
        var after = (await ReadAsync(await maker.GetAsync("/api/campaign/submissions/mine"))).EnumerateArray().Single();

        Assert.True(after.GetProperty("isJudged").GetBoolean());
        Assert.Equal(9, after.GetProperty("score").GetInt32());
        Assert.Equal("Shows a real feed and says why it ranked that way", after.GetProperty("verdict").GetString());
    }

    /// <summary>
    /// A referral, from the code to the board row it produces.
    /// </summary>
    /// <remarks>
    /// The acquisition half. Three counts rather than one, and the names the panel and the board
    /// read them by.
    /// </remarks>
    [Fact]
    public async Task AReferralCodeBringsSomebodyIn_AndTheBoardSaysSo()
    {
        var introducer = await ParticipantAsync();

        var mine = await ReadAsync(await introducer.GetAsync("/api/referrals/mine"));
        Carries(mine, "code", "link", "signedUp", "personalised", "cameBack", "awaitingConfirmation");

        var code = mine.GetProperty("code").GetString()!;
        Assert.Equal(7, code.Length);

        // Nothing yet, and said as zero rather than as an absent field.
        Assert.Equal(0, mine.GetProperty("signedUp").GetInt32());

        // Somebody arrives with the code, and has not confirmed their address.
        var newcomer = await ParticipantAsync(referralCode: code);

        var before = await ReadAsync(await introducer.GetAsync("/api/referrals/mine"));

        // Counted nowhere until the address answers - which is the whole of how somebody would
        // otherwise manufacture a number - but said out loud rather than dropped.
        Assert.Equal(0, before.GetProperty("signedUp").GetInt32());
        Assert.Equal(1, before.GetProperty("awaitingConfirmation").GetInt32());

        await ConfirmAsync(newcomer);

        var after = await ReadAsync(await introducer.GetAsync("/api/referrals/mine"));
        Assert.Equal(1, after.GetProperty("signedUp").GetInt32());
        Assert.Equal(0, after.GetProperty("awaitingConfirmation").GetInt32());

        // A sign-up is still not a user: they have answered nothing and done nothing since.
        Assert.Equal(0, after.GetProperty("personalised").GetInt32());
        Assert.Equal(0, after.GetProperty("cameBack").GetInt32());

        // ── the acquisition board ────────────────────────────────────────────
        var admin = await AdminAsync();
        var board = await ReadAsync(await admin.GetAsync("/api/referrals/board"));

        Assert.Equal(JsonValueKind.Array, board.ValueKind);
        Carries(board.EnumerateArray().First(),
            "profileId", "name", "code", "signedUp", "personalised", "cameBack", "awaitingConfirmation");
    }

    /// <summary>
    /// A link that nobody could open is refused, and the reason comes back readable.
    /// </summary>
    /// <remarks>
    /// The panel shows the server's own words. A rule that arrives as an empty body reads as a
    /// form that silently does nothing.
    /// </remarks>
    [Fact]
    public async Task AnUnusableLinkIsRefusedWithAReasonTheScreenCanShow()
    {
        var maker = await ParticipantAsync();

        var response = await maker.PostAsJsonAsync("/api/campaign/submissions", new
        {
            link = "javascript:alert(document.cookie)",
            platform = "TikTok",
            note = "",
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(string.IsNullOrWhiteSpace(body.RootElement.GetProperty("error").GetString()));
    }

    /// <summary>The same link twice is refused, with its own reason.</summary>
    [Fact]
    public async Task TheSameLinkTwiceIsRefused()
    {
        var maker = await ParticipantAsync();
        var link = $"https://tiktok.com/@maker/{Guid.NewGuid():N}";

        var first = await maker.PostAsJsonAsync("/api/campaign/submissions", new { link, platform = "TikTok", note = "" });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var second = await maker.PostAsJsonAsync("/api/campaign/submissions", new { link, platform = "TikTok", note = "" });
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    /// <summary>One participant cannot take back another's work.</summary>
    [Fact]
    public async Task NobodyCanWithdrawSomebodyElsesSubmission()
    {
        var maker = await ParticipantAsync();
        var stranger = await ParticipantAsync();

        var submitted = await ReadAsync(await maker.PostAsJsonAsync("/api/campaign/submissions", new
        {
            link = $"https://tiktok.com/@maker/{Guid.NewGuid():N}",
            platform = "TikTok",
            note = "",
        }));

        var id = submitted.GetProperty("id").GetString()!;

        Assert.Equal(HttpStatusCode.NotFound, (await stranger.DeleteAsync($"/api/campaign/submissions/{id}")).StatusCode);

        // And it is still there for the person who sent it.
        var mine = await ReadAsync(await maker.GetAsync("/api/campaign/submissions/mine"));
        Assert.Contains(mine.EnumerateArray(), e => e.GetProperty("id").GetString() == id);
    }
}
