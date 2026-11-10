using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace RadarV2.Tests.Http;

/// <summary>
/// Who can reach what, asserted rather than read off an attribute.
/// </summary>
/// <remarks>
/// Every claim this suite made about authorisation came from reading <c>[Authorize]</c> and
/// reasoning about it. That leaves the thing itself untested: whether a method-level role
/// attribute narrows the controller's or replaces it, whether the middleware runs in the right
/// order, and what a refusal actually looks like from outside.
///
/// The endpoints below are the ones where being wrong costs the most. The review queue carries
/// every participant's links and names; the user detail is one named person's reading history;
/// inviting an admin widens who can see everything.
/// </remarks>
[Collection(RadarAppCollection.Name)]
public class AuthorizationTests
{
    private readonly RadarAppFixture _app;

    public AuthorizationTests(RadarAppFixture app) => _app = app;

    /// <summary>A unique address per test, so one test's account cannot satisfy another's.</summary>
    private static string SomeAddress() => $"person-{Guid.NewGuid():N}@example.com";

    private HttpClient Anonymous() => _app.CreateClient();

    private async Task<HttpClient> SignedUpAsync(string? email = null)
    {
        var client = _app.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/register", new
        {
            name = "A Reader",
            email = email ?? SomeAddress(),
            password = "a-real-password",
        });

        response.EnsureSuccessStatusCode();
        await AuthoriseAsync(client, response);
        return client;
    }

    /// <summary>
    /// An admin client, through the configured break-glass.
    /// </summary>
    /// <remarks>
    /// The address is in the deployment's lists, so its ordinary account reaches the admin area -
    /// which is the path that exists so a new deployment's first admin can get in at all.
    /// </remarks>
    private async Task<HttpClient> AdminAsync(string email)
    {
        var client = _app.CreateClient();

        // Registering is allowed to fail: another test in the class may already have created
        // this account, and the point here is the sign-in that follows.
        await client.PostAsJsonAsync("/api/auth/register", new
        {
            name = "An Admin",
            email,
            password = "a-real-password",
        });

        var response = await client.PostAsJsonAsync("/api/admin/auth/login", new
        {
            email,
            password = "a-real-password",
        });

        response.EnsureSuccessStatusCode();
        await AuthoriseAsync(client, response);
        return client;
    }

    private static async Task AuthoriseAsync(HttpClient client, HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var token = body.RootElement.GetProperty("token").GetString();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    /// <summary>Everything that must be admin-only, with the method that reaches it.</summary>
    public static TheoryData<string, string> AdminOnly() => new()
    {
        { "GET", "/api/admin/summary" },
        { "GET", "/api/admin/queue" },
        { "GET", "/api/admin/sources" },
        { "GET", "/api/admin/users" },
        { "GET", "/api/admin/reports" },
        { "GET", "/api/admin/activity" },
        { "GET", "/api/admin/engagement" },
        { "GET", "/api/admin/team" },

        // The campaign's admin half. The queue carries every participant's links and names.
        { "GET", "/api/campaign/submissions" },
        { "GET", "/api/campaign/board" },
        { "GET", "/api/referrals/board" },
    };

    // ── signed out ───────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(AdminOnly))]
    public async Task NobodySignedOutReachesTheAdminArea(string method, string path)
    {
        var response = await Anonymous().SendAsync(new HttpRequestMessage(new HttpMethod(method), path));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>A refusal is JSON, not a redirect to a sign-in page that does not exist here.</summary>
    [Fact]
    public async Task ARefusalIsJsonRatherThanARedirect()
    {
        var response = await Anonymous().GetAsync("/api/admin/summary");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
    }

    // ── signed in, but not an admin ──────────────────────────────────────────

    /// <summary>
    /// The case that costs the most if the attribute is wrong.
    /// </summary>
    /// <remarks>
    /// An ordinary account is one anybody can create. If any of these answered 200 to it, the
    /// review queue - every participant's links and names - would be world-readable to anyone
    /// who signed up.
    /// </remarks>
    [Theory]
    [MemberData(nameof(AdminOnly))]
    public async Task AnOrdinaryUserReachesNothingInTheAdminArea(string method, string path)
    {
        var client = await SignedUpAsync();

        var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), path));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>An ordinary user cannot mark somebody's submission.</summary>
    [Fact]
    public async Task AnOrdinaryUserCannotJudgeASubmission()
    {
        var client = await SignedUpAsync();

        var response = await client.PostAsJsonAsync("/api/campaign/submissions/anything/judge", new
        {
            score = 10,
            verdict = "Marking this myself, thanks",
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>An ordinary user cannot hand themselves admin.</summary>
    [Fact]
    public async Task AnOrdinaryUserCannotInviteAnAdmin()
    {
        var client = await SignedUpAsync();

        var response = await client.PostAsJsonAsync("/api/admin/team", new
        {
            email = "me-again@example.com",
            role = "SuperAdmin",
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>What a participant may reach is their own, and only their own.</summary>
    [Fact]
    public async Task AParticipantReachesTheirOwnCampaignPagesAndNoOthers()
    {
        var client = await SignedUpAsync();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/referrals/mine")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/campaign/submissions/mine")).StatusCode);
    }

    // ── signed in as an admin ────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(AdminOnly))]
    public async Task AnAdminReachesTheAdminArea(string method, string path)
    {
        var client = await AdminAsync(RadarAppFixture.ConfiguredSuperAdmin);

        var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), path));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>
    /// The lesser role is genuinely lesser.
    /// </summary>
    /// <remarks>
    /// This is the one I could not assert before. A method-level [Authorize(Roles="SuperAdmin")]
    /// inside a controller gated on "SuperAdmin,PlatformAdmin" has to narrow rather than widen -
    /// if the two attributes were ORed instead of ANDed, a platform admin could read one named
    /// person's entire reading history, and appoint themselves a super admin.
    /// </remarks>
    [Fact]
    public async Task APlatformAdminCannotOpenOnePersonsHistory()
    {
        var client = await AdminAsync(RadarAppFixture.ConfiguredPlatformAdmin);

        var response = await client.GetAsync("/api/admin/users/any-profile-id");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task APlatformAdminCannotInviteAnAdmin()
    {
        var client = await AdminAsync(RadarAppFixture.ConfiguredPlatformAdmin);

        var response = await client.PostAsJsonAsync("/api/admin/team", new
        {
            email = "promoted@example.com",
            role = "SuperAdmin",
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task APlatformAdminCannotRemoveAnAdmin()
    {
        var client = await AdminAsync(RadarAppFixture.ConfiguredPlatformAdmin);

        var response = await client.DeleteAsync("/api/admin/team/somebody@example.com");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>But the lesser role still runs the product.</summary>
    [Fact]
    public async Task APlatformAdminStillReachesTheReviewQueue()
    {
        var client = await AdminAsync(RadarAppFixture.ConfiguredPlatformAdmin);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/campaign/submissions")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/admin/team")).StatusCode);
    }

    // ── the two sign-ins do not reach each other ─────────────────────────────

    /// <summary>
    /// An ordinary account cannot sign in at the admin door.
    /// </summary>
    /// <remarks>
    /// Unless the configuration vouches for it, which is the deliberate break-glass and the
    /// reason the other admin tests above can sign in at all.
    /// </remarks>
    [Fact]
    public async Task AnOrdinaryAccountIsRefusedAtTheAdminDoor()
    {
        var email = SomeAddress();
        var client = _app.CreateClient();

        var registered = await client.PostAsJsonAsync("/api/auth/register", new
        {
            name = "A Reader",
            email,
            password = "a-real-password",
        });
        registered.EnsureSuccessStatusCode();

        var response = await client.PostAsJsonAsync("/api/admin/auth/login", new { email, password = "a-real-password" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>A wrong password is refused with the same answer as an unknown address.</summary>
    /// <remarks>
    /// Telling them apart would say whether an address has an admin account, which is a question
    /// nobody outside needs answered.
    /// </remarks>
    [Fact]
    public async Task AWrongAdminPasswordLooksLikeAnUnknownAddress()
    {
        var wrongPassword = await _app.CreateClient().PostAsJsonAsync("/api/admin/auth/login", new
        {
            email = RadarAppFixture.ConfiguredSuperAdmin,
            password = "not-the-password",
        });

        var unknown = await _app.CreateClient().PostAsJsonAsync("/api/admin/auth/login", new
        {
            email = "nobody-at-all@example.com",
            password = "a-real-password",
        });

        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknown.StatusCode);
        Assert.Equal(
            await wrongPassword.Content.ReadAsStringAsync(),
            await unknown.Content.ReadAsStringAsync());
    }

    /// <summary>Accepting an invitation needs no sign-in, and an unknown token creates nothing.</summary>
    [Fact]
    public async Task AnUnknownInvitationTokenIsRefusedWithoutSigningIn()
    {
        var response = await Anonymous().PostAsJsonAsync("/api/auth/admin/accept", new
        {
            token = "not-a-real-token",
            password = "a-real-password",
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
