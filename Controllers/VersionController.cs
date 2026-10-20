using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

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
    private readonly IWebHostEnvironment _environment;
    private readonly IConfiguration _config;

    public VersionController(IWebHostEnvironment environment, IConfiguration config)
    {
        _environment = environment;
        _config = config;
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
            utcNow = DateTime.UtcNow,
        });
    }
}
