using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;
using RadarV2.Data;
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

    private readonly IConfiguration _config;
    private readonly ContentIngestionService _ingestion;
    private readonly IOpportunityService _opportunities;
    private readonly RadarDatabase _db;
    private readonly ILogger<JobsController> _log;

    public JobsController(
        IConfiguration config,
        ContentIngestionService ingestion,
        IOpportunityService opportunities,
        RadarDatabase db,
        ILogger<JobsController> log)
    {
        _config = config;
        _ingestion = ingestion;
        _opportunities = opportunities;
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
