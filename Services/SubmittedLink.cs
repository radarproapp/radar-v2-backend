namespace RadarV2.Services;

/// <summary>
/// A link somebody pasted, checked properly.
/// </summary>
/// <remarks>
/// <c>[Url]</c> was doing this job and three comments said so. It is a scheme-prefix check and
/// nothing else: measured, it accepts a bare "https://", "https://ok.com/&lt;script&gt;alert(1)",
/// a link with a newline in the middle, "https://user:pw@evil.com@ok.com/", and
/// "https://169.254.169.254/latest/meta-data/" - while rejecting "   https://ok.com/x", which
/// the service then trimmed one line later.
///
/// Nothing in Radar fetches a submitted link, so none of that is an SSRF. What it is, is a
/// stored value that the model's own documentation promised was safe, heading for an href in a
/// reviewer's browser and possibly an email. So it is checked here, once, before it is stored.
/// </remarks>
public static class SubmittedLink
{
    /// <summary>What a link may begin with. A reviewer is going to click this.</summary>
    private static readonly string[] Allowed = ["http", "https"];

    /// <summary>
    /// The link as it will be stored, or null if it is not one.
    /// </summary>
    /// <remarks>
    /// Trimmed first, because validating before trimming refused links that were about to
    /// become valid.
    /// </remarks>
    public static string? Clean(string? raw)
    {
        var text = raw?.Trim();
        if (string.IsNullOrEmpty(text) || text.Length > 500) return null;

        // Anything a browser or a mail header would read as a break. A newline inside a url is
        // how one value becomes two.
        if (text.Any(character => char.IsControl(character) || char.IsWhiteSpace(character))) return null;

        // Characters that only matter once this reaches markup. Rejected rather than escaped,
        // because a url does not need them and a reviewer cannot tell a mangled one from a real
        // one.
        if (text.IndexOfAny(['<', '>', '"', '\'', '\\']) >= 0) return null;

        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri)) return null;
        if (!Allowed.Contains(uri.Scheme, StringComparer.OrdinalIgnoreCase)) return null;

        // Credentials in a url are how "ok.com" turns out to be a different host. A submission
        // never needs them.
        if (!string.IsNullOrEmpty(uri.UserInfo)) return null;

        // A name, not an address. A video on TikTok or YouTube always has a hostname; an IP
        // literal is a machine on whoever's network is looking at the link - and the one that
        // matters, 169.254.169.254, has plenty of dots in it, so a dot check alone let it past.
        if (Uri.CheckHostName(uri.Host) != UriHostNameType.Dns) return null;

        // And a name with no dot is still something local - localhost, an internal short name.
        if (!uri.Host.Contains('.')) return null;

        return text;
    }

    /// <summary>
    /// The same link, in the one form used to tell two submissions apart.
    /// </summary>
    /// <remarks>
    /// Stored beside the link rather than computed on read, so a unique index can hold the rule
    /// under two requests arriving together. Case folded and the trailing slash dropped, because
    /// the same video pasted twice from two apps differs by exactly that - and five copies of
    /// one video is how a participant was going to pad a board.
    /// </remarks>
    public static string KeyFor(string link) =>
        link.TrimEnd('/').ToLowerInvariant();
}
