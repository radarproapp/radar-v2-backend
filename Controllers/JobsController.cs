using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;
using RadarV2.Data;
using RadarV2.Services;
using RadarV2.Services.Ingestion;
using RadarV2.Services.Interfaces;

namespace RadarV2.Controllers;

/// <summary>
/// Scheduler entry points. Railway's cron calls these from a small side service; the request is
/// authenticated with a shared secret header rather than a JWT (a cron container has no user).
/// </summary>
[ApiController]
[Route("api/jobs")]
public class JobsController : ControllerBase
{
    private const string CronKeyHeader = "X-Cron-Key";
    private readonly DailyDigestSender _digests;

    private readonly IConfiguration _config;
    private readonly ContentIngestionService _ingestion;
    private readonly IOpportunityService _opportunities;
    private readonly OpportunityIngestionService _opportunityIngestion;
    private readonly RadarDatabase _db;
    private readonly ILogger<JobsController> _log;

    public JobsController(
        IConfiguration config,
        ContentIngestionService ingestion,
        IOpportunityService opportunities,
        OpportunityIngestionService opportunityIngestion,
        DailyDigestSender digests,
        RadarDatabase db,
        ILogger<JobsController> log)
    {
        _config = config;
        _ingestion = ingestion;
        _opportunities = opportunities;
        _opportunityIngestion = opportunityIngestion;
        _digests = digests;
        _db = db;
        _log = log;
    }

    /// <summary>
    /// Builds every user's opportunity batch for today.
    ///
    /// This is what makes the daily batch daily. Computing it on read meant ranking a static set
    /// by an unchanging score, so the same twelve items returned every day — the "I've been seeing
    /// the same opportunities since you sent me the app" report. Running it on a schedule gives the
    /// batch a date, keeps it stable while a user looks at it, and moves the per-user scoring loop
    /// off the request path.
    ///
    /// Idempotent: a batch is upserted per user per day, so a retry or an overlapping run is safe.
    /// </summary>
    [AllowAnonymous]
    [HttpPost("opportunity-batch")]
    public async Task<IActionResult> RunOpportunityBatchAsync(CancellationToken ct)
    {
        if (!TryAuthorizeCron(out var failure)) return failure!;

        var profiles = await _db.Profiles.Find(_ => true).ToListAsync(ct);
        var built = 0;
        var failed = 0;

        foreach (var profile in profiles)
        {
            if (ct.IsCancellationRequested) break;
            try
            {
                await _opportunities.BuildDailyFeedAsync(profile);
                built++;
            }
            catch (Exception ex)
            {
                // One bad profile must not stop everyone else's batch from being built.
                failed++;
                _log.LogWarning(ex, "Daily opportunity batch failed for profile {ProfileId}", profile.Id);
            }
        }

        _log.LogInformation("Daily opportunity batch complete: {Built} built, {Failed} failed.", built, failed);
        return Ok(new { built, failed, total = profiles.Count });
    }

    /// <summary>
    /// Fetches opportunities from their source feeds. Separate from the content ingestion job so
    /// it can run on its own schedule and be triggered independently when debugging supply.
    /// </summary>
    [AllowAnonymous]
    [HttpPost("opportunity-ingestion")]
    public async Task<IActionResult> RunOpportunityIngestionAsync(CancellationToken ct)
    {
        if (!TryAuthorizeCron(out var failure)) return failure!;

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromMinutes(5));

        var added = await _opportunityIngestion.RunAsync(cts.Token);
        return Ok(new { added });
    }

    /// <summary>Shared cron-key check, so every job authenticates the same way.</summary>
    private bool TryAuthorizeCron(out IActionResult? failure)
    {
        var expected = _config["Jobs:CronKey"];
        if (string.IsNullOrWhiteSpace(expected))
        {
            failure = StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { error = "Scheduled jobs are not enabled. Set Jobs__CronKey." });
            return false;
        }

        var provided = Request.Headers[CronKeyHeader].FirstOrDefault() ?? string.Empty;
        if (!FixedTimeEquals(provided, expected))
        {
            _log.LogWarning("Rejected a scheduled job trigger with an invalid cron key.");
            failure = Unauthorized(new { error = "Invalid cron key." });
            return false;
        }

        failure = null;
        return true;
    }

    /// <summary>
    /// Sends the daily digest to everybody who asked for it.
    /// </summary>
    /// <remarks>
    /// Opted in only — the preference defaults to off — and nobody with nothing worth saying gets
    /// a message. The response reports how many of those there were, which is the number worth
    /// watching: a digest that always finds something to say about everybody has its threshold
    /// set too low, and the cost of that lands on the whole sending domain.
    /// </remarks>
    [AllowAnonymous]
    [HttpPost("daily-digest")]
    public async Task<IActionResult> RunDailyDigestAsync(CancellationToken ct)
    {
        var expected = _config["Jobs:CronKey"];
        if (string.IsNullOrWhiteSpace(expected))
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "Scheduled jobs are not enabled. Set Jobs__CronKey." });

        var provided = Request.Headers[CronKeyHeader].FirstOrDefault() ?? string.Empty;
        if (!FixedTimeEquals(provided, expected)) return Unauthorized();

        var result = await _digests.RunAsync(ct);

        return Ok(new
        {
            optedIn = result.Considered,
            sent = result.Sent,
            nothingToSay = result.NothingToSay,
            failed = result.Failed,
        });
    }

    /// <summary>
    /// Runs one ingestion cycle. Idempotent and safe to call on a schedule; a call that arrives
    /// while a cycle is already running is a no-op.
    /// </summary>
    [AllowAnonymous]
    [HttpPost("ingestion")]
    public async Task<IActionResult> RunIngestionAsync(CancellationToken ct)
    {
        var expected = _config["Jobs:CronKey"];
        if (string.IsNullOrWhiteSpace(expected))
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "Scheduled jobs are not enabled. Set Jobs__CronKey." });

        var provided = Request.Headers[CronKeyHeader].FirstOrDefault() ?? string.Empty;
        if (!FixedTimeEquals(provided, expected))
        {
            _log.LogWarning("Rejected scheduled ingestion trigger with an invalid cron key.");
            return Unauthorized(new { error = "Invalid cron key." });
        }

        // Fire-and-forget mode: the cron container gets an immediate 202 and can exit, while the
        // long-running API service carries the cycle on in the background.
        var asyncMode = string.Equals(Request.Query["async"].FirstOrDefault(), "true", StringComparison.OrdinalIgnoreCase);
        if (asyncMode)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    using var background = new CancellationTokenSource(TimeSpan.FromMinutes(30));
                    await _ingestion.RunCycleOnceAsync(background.Token);
                }
                catch (Exception ex)
                {
                    _log.LogWarning(ex, "Background ingestion trigger failed.");
                }
            });
            return Accepted(new { started = true, mode = "async" });
        }

        // Bound the run so a stuck source can't hold the request forever.
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromMinutes(20));

        var ran = await _ingestion.RunCycleOnceAsync(cts.Token);
        return Ok(new { ran, alreadyRunning = !ran, completedAtUtc = DateTime.UtcNow });
    }

    private static bool FixedTimeEquals(string a, string b)
    {
        var left = Encoding.UTF8.GetBytes(a);
        var right = Encoding.UTF8.GetBytes(b);
        return left.Length == right.Length && CryptographicOperations.FixedTimeEquals(left, right);
    }
}
