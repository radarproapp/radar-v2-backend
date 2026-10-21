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
