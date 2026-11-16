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
    /// <summary>
    /// Settings read as "Section:Key", so their variable form needs a double underscore.
    ///
    /// Only keys the product actually needs. OpenAlex was listed here and does not belong: it is
    /// a free, keyless API, and the optional `api_key` is appended to one RSS URL and used nowhere
    /// else. Listing it told anyone setting Radar up that they had a key to go and find.
    /// </summary>
    private static readonly string[] SectionedKeys =
    [
        "Jobs:CronKey",
        "MongoDB:ConnectionString",
        "MongoDB:DatabaseName",
        "OpenRouter:ApiKey",
        "YouTube:ApiKey",
        "TalentApply:ApiKey",
        "Auth:Jwt:Key",
        "Cors:Strict",
        "Cors:AllowedOrigins:0",

        // Every other credential the code actually reads. Each of these is optional, and the
        // failure when its variable is misspelt is the same every time: the feature is simply
        // off, the dashboard shows the variable set, and nothing says otherwise. GroqWhisper was
        // the one that proved the point - transcription would have stayed silently off.
        "GroqWhisper:ApiKey",
        "Taddy:ApiKey",
        "Taddy:UserId",
        "PodcastIndex:ApiKey",
        "PodcastIndex:ApiSecret",
        "Mediastack:ApiKey",
        "SendByte:ApiKey",
        "Email:Password",
        "App:PublicUrl",
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

            // Compared on letters and digits alone, rather than against two spellings guessed in
            // advance. The old version built "Section_Key" and "SectionKey" and looked each up
            // exactly, which misses everything else somebody actually types: a stray underscore
            // inside the section name, or different casing - and casing matters, because Railway
            // runs Linux, where environment variables are case-sensitive.
            //
            // "Groq_whisper_ApiKey" is the one that got through. It is neither of the two
            // spellings, so the check that exists for exactly this said nothing.
            foreach (var actual in NearMisses(key))
            {
                logger.LogWarning(
                    "Configuration '{Key}' is empty, but an environment variable named '{Wrong}' is set. "
                    + "Environment variables use a DOUBLE underscore for the section separator, and the "
                    + "name is case-sensitive on Linux: rename it to '{Correct}'. "
                    + "Until then this setting is ignored and the feature using it stays off.",
                    key, actual, key.Replace(":", "__"));
            }
        }
    }

    /// <summary>
    /// Environment variables that were plainly meant to be this setting and are not.
    /// </summary>
    /// <remarks>
    /// Matches on letters and digits only, so separators and casing stop mattering:
    /// <c>GroqWhisper:ApiKey</c> reduces to "groqwhisperapikey", and so do
    /// <c>Groq_whisper_ApiKey</c>, <c>GROQWHISPER__APIKEY</c> and <c>groqWhisperApiKey</c>. Any
    /// of those is obviously the same intent and none of them binds.
    ///
    /// The correctly spelt variable is excluded rather than matched, because this only runs when
    /// the setting failed to resolve - if the right name is somehow present and still empty, the
    /// problem is an empty value, and telling somebody to rename a variable to the name it
    /// already has would send them the wrong way.
    ///
    /// Names only. A value never reaches the log.
    /// </remarks>
    private static IEnumerable<string> NearMisses(string key)
    {
        var wanted = Flatten(key);
        var correct = key.Replace(":", "__");

        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            if (entry.Key is not string name) continue;
            if (string.Equals(name, correct, StringComparison.Ordinal)) continue;
            if (string.IsNullOrWhiteSpace(entry.Value as string)) continue;

            if (Flatten(name) == wanted) yield return name;
        }
    }

    private static string Flatten(string value) =>
        string.Concat(value.Where(char.IsLetterOrDigit)).ToLowerInvariant();
}
