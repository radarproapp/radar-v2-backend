using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RadarV2.Services.Ingestion;

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
    private readonly ILogger<JobsController> _log;

    public JobsController(IConfiguration config, ContentIngestionService ingestion, ILogger<JobsController> log)
    {
        _config = config;
        _ingestion = ingestion;
        _log = log;
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
