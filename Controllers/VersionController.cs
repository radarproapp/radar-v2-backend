using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RadarV2.Services;
using RadarV2.Services.Ingestion;

namespace RadarV2.Controllers;

/// <summary>
/// Unauthenticated liveness and identity for this instance.
///
/// Deliberately open: a health check that requires a token is not a health check. Railway cannot
/// probe it, and during the last deploy investigation there was no way to ask a running instance
/// what it was — which environment, which commit — so the only option was probing random routes
/// and inferring from 401-vs-404.
///
/// Returns nothing secret: an environment name, a commit sha and a database name. No connection
/// strings, no keys.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/version")]
public class VersionController : ControllerBase
{
    /// <summary>Process start, captured once when the type is first touched.</summary>
    private static readonly DateTime StartedAt = DateTime.UtcNow;

    private readonly IWebHostEnvironment _environment;
    private readonly IConfiguration _config;
    private readonly GroqWhisperClient _groq;
    private readonly TaddyClient _taddy;
    private readonly IObjectStore _objectStore;
    private readonly IEmailSender _email;

    public VersionController(
        IWebHostEnvironment environment,
        IConfiguration config,
        GroqWhisperClient groq,
        TaddyClient taddy,
        IObjectStore objectStore,
        IEmailSender email)
    {
        _environment = environment;
        _config = config;
        _groq = groq;
        _taddy = taddy;
        _objectStore = objectStore;
        _email = email;
    }

    [HttpGet]
    public IActionResult Get()
    {
        // Railway injects the commit it built from. Falls back to the assembly version when running
        // anywhere else, so the field is never empty and never guessed.
        var commit = _config["RAILWAY_GIT_COMMIT_SHA"]
            ?? Environment.GetEnvironmentVariable("RAILWAY_GIT_COMMIT_SHA")
            ?? Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? "unknown";

        return Ok(new
        {
            status = "ok",
            environment = _environment.EnvironmentName,
            isProduction = _environment.IsProduction(),
            commit = commit.Length > 12 ? commit[..12] : commit,
            // Named so a staging instance accidentally pointed at the production database is
            // visible at a glance rather than discovered through its effects.
            database = _config["MongoDB:DatabaseName"],

            // What the platform calls this environment, as opposed to what the app believes it is.
            // ASPNETCORE_ENVIRONMENT defaults to Production when unset, so a staging service with
            // the variable missing reports itself as production and reads the production database
            // while answering on a staging hostname. Showing both makes the disagreement obvious
            // from a single curl instead of requiring someone to read startup logs.
            hostEnvironment = Environment.GetEnvironmentVariable("RAILWAY_ENVIRONMENT_NAME"),

            // Whether each required secret is visible to this instance. Booleans only — never the
            // values. Without this, a missing variable is only discoverable by triggering the
            // feature and reading a 503, and a cron container reports "succeeded" either way
            // because it only reports that the container exited.
            configured = new
            {
                cronJobs = !string.IsNullOrWhiteSpace(_config["Jobs:CronKey"]),
                openRouter = !string.IsNullOrWhiteSpace(_config["OpenRouter:ApiKey"]),
                youTube = !string.IsNullOrWhiteSpace(_config["YouTube:ApiKey"]),
                mongo = !string.IsNullOrWhiteSpace(_config["MongoDB:ConnectionString"]),
                // The two transcript sources. Added because "is transcription even switched on?"
                // was a question with no answer from outside the box, and the honest reading of
                // zero stored transcripts depends entirely on it: an unconfigured key and a
                // broken pipeline look identical from the feed.
                //
                // Asked of the clients rather than re-derived from config. Taddy needs a user id
                // as well as a key, so a flag checking only the key would have reported it live
                // while the client refused every call — which is the mistake isCloudLlmActive
                // made with the OpenRouter key.
                groqWhisper = _groq.IsConfigured,
                taddy = _taddy.IsConfigured,
                // All four R2 settings, not one of them. A store missing its bucket name is not
                // configured, and reporting otherwise is the mistake isCloudLlmActive made.
                objectStore = _objectStore.IsConfigured,
                // Without this there is no password reset: a reset that cannot deliver a link
                // is a form that silently does nothing.
                email = _email.IsConfigured,
                // A SendByte sk_test_ key behaves exactly like a working one from here — 201, an
                // id, no email. Reported so "configured but nothing arrives" is answerable from
                // outside rather than by waiting for an inbox that will never fill.
                emailSandbox = (_email as SendByteEmailSender)?.IsSandbox ?? false,
            },

            // Where emailed links point, and where the logo in them is loaded from.
            //
            // Not a secret — it is a public URL — and worth reporting because getting it wrong is
            // silent and total: a reset link would arrive pointing somewhere dead, and the only
            // symptom would be a user saying the link does not work. It said exactly that while
            // the default pointed at a domain that had not been assigned yet.
            //
            // Marked when it is the default rather than configured, so the two cannot be confused
            // by someone reading this to find out which one is in force.
            publicUrl = _config["App:PublicUrl"] ?? $"{PublicSite.Canonical} (default)",

            // What happened the last time Radar tried to send. No recipient, no key — a status
            // and an error code, which is the difference between "the key is wrong" and "the
            // domain is not verified yet". Both otherwise look identical: silence.
            lastEmail = new
            {
                at = SendByteEmailSender.LastSendAt,
                outcome = SendByteEmailSender.LastSendOutcome ?? "nothing sent since this instance started",
            },

            // Whether the one-off data repairs ran, and what they removed.
            //
            // Added because "did the cleanup run?" had no answer from outside the box: the
            // repairs were buried in a method whose exceptions are discarded, and six invented
            // opportunities stayed on the page for a day after the deploy meant to delete them.
            repairs = StartupRepairService.LastRun,

            // What CORS is actually doing, as opposed to what the dashboard says it was set to.
            //
            // Production spent a morning rejecting its own frontend because Cors:Strict resolved
            // true while the allow-list named domains that were not yet wired up. Nothing in the
            // API said so: every response was a valid 200 with no Access-Control-Allow-Origin
            // header, which a browser turns into a blocked request and a server log turns into
            // nothing at all. The only way to see it was to guess the right curl.
            //
            // Names no secret - an allow-list is public by construction, since it is echoed back
            // to any origin that matches it.
            cors = new
            {
                strict = _config.GetValue("Cors:Strict", false),
                allowedOrigins = _config.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [],
            },

            // When this process started. Variables are read once at startup, so a variable changed
            // on the host does nothing until the container restarts — and a redeploy of the same
            // commit looks identical by sha. Without this there is no way to tell "the rename did
            // not take" from "the service has not restarted yet".
            startedAt = StartedAt,
            uptimeMinutes = (int)(DateTime.UtcNow - StartedAt).TotalMinutes,

            utcNow = DateTime.UtcNow,
        });
    }
}
