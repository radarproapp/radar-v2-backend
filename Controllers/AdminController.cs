using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;
using RadarV2.Data;
using RadarV2.Services;

namespace RadarV2.Controllers;

[ApiController]
[Authorize(Roles = "SuperAdmin,PlatformAdmin")]
[Route("api/admin")]
public class AdminController : ControllerBase
{
    private readonly RadarDatabase _db;
    private readonly AdminInsightsService _insights;
    private readonly AdminRoleService _roles;
    private readonly ILogger<AdminController> _log;

    public AdminController(RadarDatabase db, AdminInsightsService insights, AdminRoleService roles, ILogger<AdminController> log)
    {
        _db = db;
        _insights = insights;
        _roles = roles;
        _log = log;
    }

    /// <summary>
    /// The signed-in admin's own address, for recording who granted or revoked what.
    /// </summary>
    /// <remarks>
    /// Read from the account rather than off the token, because the token carries a display name
    /// and an id but no address - and "who let them in" has to be answerable by somebody who was
    /// not here, which an internal id is not.
    /// </remarks>
    private async Task<string> MyEmailAsync(CancellationToken ct)
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrWhiteSpace(userId)) return string.Empty;

        var me = await _db.Users.Find(u => u.Id == userId).FirstOrDefaultAsync(ct);
        return me?.Email ?? string.Empty;
    }

    /// <summary>
    /// The most recent unhandled exceptions, newest first.
    /// </summary>
    /// <remarks>
    /// Production responses carry no error detail on purpose, so without this the only way to see
    /// why something is failing there is the host's log dashboard. Admin-gated, because a stack
    /// trace is not for everyone.
    /// </remarks>
    [HttpGet("errors")]
    public async Task<IActionResult> GetRecentErrorsAsync([FromQuery] int take = 20, CancellationToken ct = default)
    {
        var errors = await _db.ErrorLog
            .Find(FilterDefinition<Data.Documents.ErrorLogDoc>.Empty)
            .SortByDescending(e => e.OccurredAt)
            .Limit(Math.Clamp(take, 1, 100))
            .ToListAsync(ct);

        return Ok(errors);
    }

    /// <summary>
    /// What every ingestion source last did, worst first.
    /// </summary>
    /// <remarks>
    /// The question this exists to answer is "why is the feed missing X?". Before it, that took
    /// adding a source, waiting, probing the feed and inferring — four rounds, in the case that
    /// prompted it, each one finding a different silent failure. Now it is one request.
    ///
    /// Sorted by how long a source has been silent, because the interesting ones are the ones
    /// that have stopped.
    /// </remarks>
    [HttpGet("source-health")]
    public async Task<IActionResult> GetSourceHealthAsync(CancellationToken ct = default)
    {
        var health = await _db.SourceHealth
            .Find(FilterDefinition<Data.Documents.SourceHealthDoc>.Empty)
            .ToListAsync(ct);

        var byId = health.ToDictionary(h => h.Id, StringComparer.Ordinal);
        var now = DateTime.UtcNow;

        var rows = Services.Ingestion.SourceRegistry.All
            .Where(source => source.IsEnabled)
            .Select(source =>
            {
                byId.TryGetValue(source.Id, out var h);
                return new
                {
                    id = source.Id,
                    name = source.Name,
                    layer = source.Layer.ToString(),
                    // Never fetched at all is a different failure from fetched and empty, and the
                    // two were indistinguishable for as long as there was nothing to look at.
                    state = h is null ? "never attempted"
                        : h.LastSuccessAt is null ? "never produced an item"
                        : h.ConsecutiveEmpty >= 3 ? "silent"
                        : "producing",
                    lastAttemptAt = h?.LastAttemptAt,
                    lastSuccessAt = h?.LastSuccessAt,
                    hoursSinceSuccess = h?.LastSuccessAt is null
                        ? (double?)null
                        : Math.Round((now - h.LastSuccessAt.Value).TotalHours, 1),
                    lastHttpStatus = h?.LastHttpStatus,
                    lastItemsFetched = h?.LastItemsFetched,
                    consecutiveEmpty = h?.ConsecutiveEmpty,
                    lastError = h?.LastError,
                };
            })
            .OrderByDescending(r => r.state == "never attempted")
            .ThenByDescending(r => r.state == "never produced an item")
            .ThenByDescending(r => r.consecutiveEmpty ?? 0)
            .ToList();

        return Ok(new
        {
            total = rows.Count,
            producing = rows.Count(r => r.state == "producing"),
            silent = rows.Count(r => r.state == "silent"),
            neverProduced = rows.Count(r => r.state == "never produced an item"),
            neverAttempted = rows.Count(r => r.state == "never attempted"),
            sources = rows,
        });
    }

    [HttpGet("summary")]
    public async Task<IActionResult> GetSummaryAsync(CancellationToken ct)
    {
        var openReports = await _db.ContentReports.CountDocumentsAsync(
            Builders<Models.ContentReport>.Filter.Eq(r => r.IsResolved, false), cancellationToken: ct);

        return Ok(new
        {
            users = await _db.Profiles.CountDocumentsAsync(FilterDefinition<Models.UserProfile>.Empty, cancellationToken: ct),
            publishedContent = await _db.ContentItems.CountDocumentsAsync(FilterDefinition<Models.ContentItem>.Empty, cancellationToken: ct),
            openReports,
            role = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value ?? "User"
        });
    }

    /// <summary>
    /// Who can get into the admin area, and what each of them is allowed to do.
    /// </summary>
    /// <remarks>
    /// This screen listed four named admins with email addresses and last-active times, written
    /// into the page - Chidi, Blessing and Yusuf did not have accounts. There was no endpoint
    /// behind it, and so no way to answer "who can get in here", which is a question the owner of
    /// a product has to be able to ask.
    ///
    /// Rows come from both places that can grant access: the deployment's configuration, and
    /// grants made here by a super admin. Each row says which, because the two are not equally
    /// removable - a configured one is the way back in and nothing in this controller will touch
    /// it.
    ///
    /// Visible to both admin roles. Which addresses hold the keys is not one person's private
    /// data - it is the shape of the trusted set, and an admin who cannot see who else is an
    /// admin cannot notice an address that should not be there.
    /// </remarks>
    [HttpGet("team")]
    public async Task<IActionResult> GetTeamAsync(CancellationToken ct = default)
    {
        var members = await _roles.TeamAsync(ct);
        var emails = members.Select(member => member.Email).ToList();

        var accounts = emails.Count == 0
            ? []
            : await _db.Users
                .Find(Builders<Data.Documents.UserDocument>.Filter.In(u => u.Email, emails))
                .Project(u => new { u.Email, u.CreatedAt })
                .ToListAsync(ct);

        var byEmail = accounts
            .GroupBy(account => account.Email, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var me = await MyEmailAsync(ct);

        return Ok(new
        {
            team = members.Select(member =>
            {
                var account = byEmail.GetValueOrDefault(member.Email);

                return new
                {
                    email = member.Email,
                    role = member.Role,
                    fromConfiguration = member.FromConfiguration,
                    grantedByEmail = member.GrantedByEmail,
                    grantedAt = member.GrantedAt,

                    // Whether anybody has actually signed up with this address. An address
                    // granted access that has never been used is not a person on the team yet,
                    // and showing it as one would overstate who is watching the product.
                    hasAccount = account is not null,
                    joinedAt = account?.CreatedAt,

                    // Said per row, so the screen does not have to work out the rules again and
                    // get them subtly different. A configured row is the way back in; your own
                    // row is what guarantees somebody is always left.
                    canRevoke = !member.FromConfiguration
                                && !string.Equals(member.Email, me, StringComparison.OrdinalIgnoreCase),
                };
            }),

            // Whether this admin may change the list at all, so the screen can show the form to
            // the people it will work for rather than to everybody.
            canInvite = User.IsInRole(Services.AdminRoles.SuperAdmin),

            you = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value ?? "User",

            // How a row that cannot be removed here is removed, on the screen that shows it.
            configuredBy = "Auth__SuperAdminEmails__n and Auth__AdminEmails__n in the deployment environment",
        });
    }

    public sealed class TeamInviteRequest
    {
        [System.ComponentModel.DataAnnotations.Required]
        public string Email { get; set; } = string.Empty;

        /// <summary>Which of the two roles. Anything else is refused rather than defaulted.</summary>
        [System.ComponentModel.DataAnnotations.Required]
        public string Role { get; set; } = string.Empty;
    }

    /// <summary>
    /// Gives an address admin access.
    /// </summary>
    /// <remarks>
    /// Super admin only, and logged. Granting admin is the one action here that widens who can
    /// see everything, so the lesser role cannot take it - a platform admin who could appoint a
    /// super admin would be a super admin with extra steps.
    ///
    /// By address rather than by account, because the person usually does not have one yet. The
    /// grant waits until they sign up, or until they next sign in if they already use Radar.
    /// </remarks>
    [Authorize(Roles = Services.AdminRoles.SuperAdmin)]
    [HttpPost("team")]
    public async Task<IActionResult> InviteAsync([FromBody] TeamInviteRequest request, CancellationToken ct = default)
    {
        var me = await MyEmailAsync(ct);
        var email = AdminRoleService.Normalise(request.Email);

        var result = await _roles.GrantAsync(email, request.Role, me, ct);

        if (result is AdminRoleService.GrantResult.Granted)
        {
            _log.LogInformation("Admin {Admin} granted {Role} to {Email}", me, request.Role, email);
            return NoContent();
        }

        return BadRequest(new
        {
            error = result switch
            {
                AdminRoleService.GrantResult.NotAnEmail => "That does not look like an email address.",
                AdminRoleService.GrantResult.NotARole => "A role is either SuperAdmin or PlatformAdmin.",
                _ => "That address already has admin access from the deployment configuration.",
            },
        });
    }

    /// <summary>
    /// Takes admin access back.
    /// </summary>
    /// <remarks>
    /// Takes effect within the half-minute the grant table is cached, not at that person's next
    /// sign-in - the role a token claims is re-checked against this on every request, so an
    /// admin token already in circulation stops working rather than lasting out its thirty days.
    /// </remarks>
    [Authorize(Roles = Services.AdminRoles.SuperAdmin)]
    [HttpDelete("team/{email}")]
    public async Task<IActionResult> RevokeAsync(string email, CancellationToken ct = default)
    {
        var me = await MyEmailAsync(ct);
        var result = await _roles.RevokeAsync(email, me, ct);

        if (result is AdminRoleService.RevokeResult.Revoked)
        {
            _log.LogInformation("Admin {Admin} revoked admin access for {Email}", me, AdminRoleService.Normalise(email));
            return NoContent();
        }

        return result switch
        {
            AdminRoleService.RevokeResult.NotGranted => NotFound(new { error = "That address does not have a grant to remove." }),
            AdminRoleService.RevokeResult.CannotRevokeYourself => BadRequest(new
            {
                error = "You cannot remove your own access. Ask another Super Admin to do it.",
            }),
            _ => BadRequest(new
            {
                error = "That address is an admin because of the deployment configuration, so it has to be removed there.",
            }),
        };
    }

    [HttpGet("queue")]
    public async Task<IActionResult> GetQueueAsync([FromQuery] int limit = 50, CancellationToken ct = default)
    {
        var items = await _db.ContentItems.Find(_ => true)
            .SortByDescending(item => item.PublishedAt)
            .Limit(Math.Clamp(limit, 1, 100))
            .ToListAsync(ct);

        return Ok(items.Select(item => new
        {
            id = item.Id,
            title = item.Title,
            signal = item.Signal,
            source = item.Source,
            publishedAt = item.PublishedAt,
            credibilityTier = item.CredibilityTier,
            isEnriched = item.IsEnriched,
            type = item.Type.ToString(),
        }));
    }

    /// <summary>
    /// The source register, with what each source has actually filed.
    /// </summary>
    /// <remarks>
    /// <c>itemsInRadar</c> is counted from the corpus. The field of the same name on
    /// <see cref="Models.SourceProfile"/> is seed data - eighteen, twelve, twenty-four, written
    /// by hand when the sources were first listed - and nothing has ever updated it, so reading
    /// it back put a constant on the screen under the heading "Items in Radar" and summed those
    /// constants into a total. <c>itemsRead</c> is gone for the same reason and has not been
    /// replaced: opens are recorded per content item, and rolling them up to a source is what
    /// the Engagement section does with real events.
    ///
    /// <c>active</c> is gone too. It was the literal <c>true</c>, so every row rendered "Active"
    /// in teal whatever the source was doing, and the register carried a health column that no
    /// part of Radar can fill - nothing records a fetch, a failure or a cooldown anywhere.
    /// </remarks>
    [HttpGet("sources")]
    public async Task<IActionResult> GetSourcesAsync(CancellationToken ct)
    {
        var sources = await _db.Sources.Find(_ => true).SortBy(s => s.Name).ToListAsync(ct);

        var counted = await _db.ContentItems.Aggregate()
            .Group(new MongoDB.Bson.BsonDocument
            {
                ["_id"] = "$Source",
                ["count"] = new MongoDB.Bson.BsonDocument("$sum", 1),
            })
            .ToListAsync(ct);

        var itemsBySource = counted
            .Where(row => row["_id"].IsString)
            .ToDictionary(row => row["_id"].AsString, row => row["count"].ToInt32(), StringComparer.OrdinalIgnoreCase);

        return Ok(sources.Select(source => new
        {
            id = source.Id,
            name = source.Name,
            domain = source.Domain,
            type = source.Type,
            itemsInRadar = itemsBySource.GetValueOrDefault(source.Name),
            publishFrequency = source.PublishFrequency,
        }));
    }

    [HttpGet("users")]
    public async Task<IActionResult> GetUsersAsync([FromQuery] int limit = 50, CancellationToken ct = default)
    {
        var users = await _db.Profiles.Find(_ => true)
            .SortByDescending(profile => profile.CreatedAt)
            .Limit(Math.Clamp(limit, 1, 100))
            .ToListAsync(ct);

        return Ok(users.Select(profile => new
        {
            id = profile.Id,
            name = profile.Name,
            email = profile.Email,
            persona = profile.Persona.ToString(),
            goal = profile.PrimaryGoal,
            region = string.Join(", ", new[] { profile.City, profile.Region }.Where(value => !string.IsNullOrWhiteSpace(value))),
            onboardingComplete = profile.OnboardingComplete,
            roadmapProgressPercent = profile.Stats.RoadmapProgressPercent,
            createdAt = profile.CreatedAt,
        }));
    }

    [HttpGet("reports")]
    public async Task<IActionResult> GetReportsAsync(CancellationToken ct)
    {
        var reports = await _db.ContentReports.Find(report => !report.IsResolved)
            .SortByDescending(report => report.CreatedAt)
            .ToListAsync(ct);
        return Ok(reports.Select(report => new
        {
            id = report.Id,
            signal = report.ItemSignal,
            source = report.Source,
            reason = report.Reason.ToString(),
            note = report.Note,
            createdAt = report.CreatedAt,
        }));
    }

    [HttpPost("reports/{id}/resolve")]
    public async Task<IActionResult> ResolveReportAsync(string id, CancellationToken ct)
    {
        await _db.ContentReports.UpdateOneAsync(
            report => report.Id == id,
            Builders<Models.ContentReport>.Update.Set(report => report.IsResolved, true),
            cancellationToken: ct);
        return NoContent();
    }

    // ── what is actually happening ───────────────────────────────────────────

    /// <summary>
    /// Events and distinct people per day.
    /// </summary>
    /// <remarks>
    /// Seventeen kinds of event were already being recorded and nothing in the admin area read
    /// any of them, so "what are people engaging with" was a question the database could answer
    /// and the product could not.
    /// </remarks>
    [HttpGet("activity")]
    public async Task<IActionResult> GetActivityAsync([FromQuery] int days = 30, CancellationToken ct = default)
    {
        return Ok(new
        {
            // The window that was used, not the one that was asked for. The service clamps to
            // 180 days; echoing the request back put "nothing recorded in the last 10000 days"
            // above half a year of data, which is a sentence nothing produced.
            days = Services.AdminInsightsService.WindowFor(days),

            byDay = await _insights.ActivityByDayAsync(days, ct),
            mix = await _insights.EventMixAsync(days, ct),
            funnel = await _insights.FunnelAsync(days, ct),

            // Every kind of event Radar knows how to record, so the screen can name the ones it
            // has seen nothing of. The numbers above are only as good as what the client
            // actually sends, and a dashboard that does not say which kinds never arrive invites
            // its own figures to be read as the whole picture. Which kinds those are is worked
            // out by comparing this list against the mix, rather than claimed here - a count
            // written into a comment goes stale the first time an event is wired up.
            recorded = Enum.GetValues<Models.AnalyticsEventType>().Select(type => type.ToString()),
        });
    }

    /// <summary>What people opened and saved, and which sources earned it.</summary>
    [HttpGet("engagement")]
    public async Task<IActionResult> GetEngagementAsync([FromQuery] int days = 30, CancellationToken ct = default)
    {
        return Ok(new
        {
            days = Services.AdminInsightsService.WindowFor(days),
            items = await _insights.MostEngagedAsync(days, 20, ct),
            sources = await _insights.BySourceAsync(days, 25, ct),
        });
    }

    /// <summary>
    /// One person: what they told Radar, and what they have done since.
    /// </summary>
    /// <remarks>
    /// SuperAdmin only, and logged. The rest of this controller is aggregate; this is one named
    /// person's reading history, which is a different thing to hold and a different thing to
    /// justify. It exists because support and abuse questions cannot be answered from totals -
    /// and the audit line is so that looking at it is itself a recorded act, rather than a
    /// capability nobody can see being used.
    /// </remarks>
    [Authorize(Roles = "SuperAdmin")]
    [HttpGet("users/{id}")]
    public async Task<IActionResult> GetUserAsync(string id, CancellationToken ct = default)
    {
        var profile = await _db.Profiles.Find(p => p.Id == id).FirstOrDefaultAsync(ct);
        if (profile is null) return NotFound(new { error = "Profile not found" });

        var who = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "unknown";
        _log.LogInformation("Admin {Admin} opened the detail for profile {Profile}", who, id);

        var events = await _db.AnalyticsEvents
            .Find(e => e.UserId == id)
            .SortByDescending(e => e.CreatedAt)
            .Limit(100)
            .ToListAsync(ct);

        var contentIds = events.Where(e => e.ContentItemId is not null).Select(e => e.ContentItemId!).Distinct().ToList();

        var titles = contentIds.Count == 0
            ? []
            : await _db.ContentItems
                .Find(Builders<Models.ContentItem>.Filter.In(item => item.Id, contentIds))
                .Project(item => new { item.Id, item.Title, item.Source })
                .ToListAsync(ct);

        var titleOf = titles.ToDictionary(t => t.Id);

        return Ok(new
        {
            profile = new
            {
                profile.Id,
                profile.Name,
                profile.Email,
                persona = profile.Persona.ToString(),
                chosenCategory = profile.HasChosenCategory,
                profile.Interests,
                profile.Goals,
                problems = profile.Problems,
                profile.PrimaryGoal,
                profile.IsPersonalised,
                profile.OnboardingComplete,
                profile.CreatedAt,
            },

            // Last seen is the newest event there is. Null rather than a guess when they have
            // done nothing: an account that has never acted has no last-seen, and showing their
            // sign-up date in that column would read as activity.
            lastSeen = events.Count > 0 ? events[0].CreatedAt : (DateTime?)null,

            recent = events.Select(e => new
            {
                type = e.Type.ToString(),
                at = e.CreatedAt,
                contentItemId = e.ContentItemId,
                title = e.ContentItemId is not null && titleOf.TryGetValue(e.ContentItemId, out var item) ? item.Title : null,
                source = e.ContentItemId is not null && titleOf.TryGetValue(e.ContentItemId, out var where) ? where.Source : null,
            }),
        });
    }
}
