namespace RadarV2.Services;

/// <summary>
/// Warns at startup about configuration that is present but will never be read.
///
/// ASP.NET Core maps a double underscore in an environment variable to the ":" separator, so
/// `Jobs__CronKey` becomes `Jobs:CronKey`. A single underscore produces a flat key nobody reads.
/// The variable is visibly set on the host, the dashboard shows it, and the feature stays silently
/// off — which cost real time to track down, because the only symptom was a 503 that looked
/// identical to the variable not existing at all.
///
/// Logs, never throws: a near-miss on an optional key should not stop the service from booting.
/// Never logs a value.
/// </summary>
public static class ConfigurationCheck
{
    /// <summary>Settings read as "Section:Key", so their variable form needs a double underscore.</summary>
    private static readonly string[] SectionedKeys =
    [
        "Jobs:CronKey",
        "MongoDB:ConnectionString",
        "MongoDB:DatabaseName",
        "OpenRouter:ApiKey",
        "YouTube:ApiKey",
        "OpenAlex:ApiKey",
        "Auth:Jwt:Key",
    ];

    /// <summary>
    /// Flags a production service that still accepts any *.vercel.app origin. That wildcard exists
    /// so frontend previews can reach a non-production API, but in production it means a staging
    /// frontend — also a *.vercel.app origin — can call real data.
    /// </summary>
    public static void WarnAboutOpenCors(IConfiguration config, IWebHostEnvironment environment, ILogger logger)
    {
        if (!environment.IsProduction()) return;
        if (config.GetValue("Cors:Strict", false)) return;

        logger.LogWarning(
            "Cors:Strict is off, so this production service accepts any *.vercel.app or *.netlify.app "
            + "origin — including a staging frontend. Once Cors__AllowedOrigins__0 names the production "
            + "site, set Cors__Strict=true to restrict it to that list.");
    }

    /// <summary>
    /// Catches a host environment that disagrees with the ASP.NET environment.
    ///
    /// ASPNETCORE_ENVIRONMENT defaults to Production when unset. A newly created staging service
    /// therefore boots as Production, never reads appsettings.Staging.json, and inherits whatever
    /// database name the environment hands it — which, if the environment was duplicated from
    /// production, is the production database. Nothing fails. It serves real data under a staging
    /// hostname, and the only symptom is test accounts appearing in production.
    ///
    /// Railway injects RAILWAY_ENVIRONMENT_NAME, which comes from the platform rather than from a
    /// variable someone remembered to set, so it cannot drift the way ASPNETCORE_ENVIRONMENT can.
    /// Where the two disagree, the platform is right.
    ///
    /// Warns rather than throws: the mismatch may be deliberate on a one-off service, and refusing
    /// to boot would turn a configuration smell into an outage.
    /// </summary>
    public static void WarnAboutEnvironmentMismatch(
        IWebHostEnvironment environment, IConfiguration config, ILogger logger)
    {
        var hostEnvironment = Environment.GetEnvironmentVariable("RAILWAY_ENVIRONMENT_NAME");
        if (string.IsNullOrWhiteSpace(hostEnvironment)) return;
        if (string.Equals(hostEnvironment, environment.EnvironmentName, StringComparison.OrdinalIgnoreCase)) return;

        logger.LogError(
            "The host says this is the '{HostEnvironment}' environment, but the application is "
            + "running as '{AppEnvironment}' against database '{Database}'. ASPNETCORE_ENVIRONMENT "
            + "is unset or wrong, so appsettings.{AppEnvironment}.json is the file being read and "
            + "appsettings.{HostEnvironment}.json is being ignored. If that database is the "
            + "production one, this service is serving real data. Set ASPNETCORE_ENVIRONMENT={HostEnvironment}.",
            hostEnvironment, environment.EnvironmentName, config["MongoDB:DatabaseName"],
            environment.EnvironmentName, hostEnvironment, hostEnvironment);
    }

    public static void WarnAboutMisnamedKeys(IConfiguration config, ILogger logger)
    {
        foreach (var key in SectionedKeys)
        {
            // Already resolving — nothing to say.
            if (!string.IsNullOrWhiteSpace(config[key])) continue;

            var singleUnderscore = key.Replace(":", "_");
            var noSeparator = key.Replace(":", "");

            foreach (var wrong in new[] { singleUnderscore, noSeparator })
            {
                if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(wrong))) continue;

                logger.LogWarning(
                    "Configuration '{Key}' is empty, but an environment variable named '{Wrong}' is set. "
                    + "Environment variables use a DOUBLE underscore for the section separator: rename it to '{Correct}'. "
                    + "Until then this setting is ignored and the feature using it stays off.",
                    key, wrong, key.Replace(":", "__"));
            }
        }
    }
}
