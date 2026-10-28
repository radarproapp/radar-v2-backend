namespace RadarV2.Services;

/// <summary>
/// Where Radar is served from, for the links in the messages it sends.
/// </summary>
/// <remarks>
/// This was written out four separate times — the welcome email, the reset email, the daily
/// update, and the template footer all carried their own copy of the same fallback. Four copies of
/// one address is four chances for an email to point somewhere the others do not, and the symptom
/// is a dead link in somebody's inbox rather than anything a test or a log would show.
///
/// The canonical host is the one that answers, not the one that redirects. The apex 308s to www,
/// and while that redirect does carry the path and query through today, every link Radar has ever
/// sent would break at once if it stopped — so the links are addressed to the host that actually
/// serves the app.
/// </remarks>
public static class PublicSite
{
    /// <summary>The host that serves the app. Overridden by App:PublicUrl.</summary>
    public const string Canonical = "https://www.radarproapp.com";

    /// <summary>
    /// The site to build links against, without a trailing slash.
    /// </summary>
    /// <remarks>
    /// Trimmed here rather than at each caller, because every caller appends a path and the one
    /// that forgets produces a double slash in a link somebody has to click.
    /// </remarks>
    public static string From(IConfiguration config) =>
        (config["App:PublicUrl"] ?? Canonical).TrimEnd('/');
}
