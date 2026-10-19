using System.Net;
using System.Text.RegularExpressions;
using MongoDB.Driver;
using RadarV2.Data;
using RadarV2.Models;
using RadarV2.Services.Interfaces;

namespace RadarV2.Services;

/// <summary>
/// Captures links and notes, and turns them into something useful.
///
/// This previously stored the raw string and nothing else: the "signal" was the URL itself, the
/// source was the literal text "captured just now", and why-it-matters was never populated. The
/// page promised "a signal headline, why it matters, and a route into your roadmap" and delivered
/// none of the three, which is why capturing a link appeared to do nothing at all.
///
/// A link is now fetched, its real title and description read, and a personalised explanation
/// written against the user's own profile. Anything that cannot be determined is left null so the
/// UI omits it, rather than being filled with invented text.
/// </summary>
public partial class MongoCaptureService : ICaptureService
{
    private readonly RadarDatabase _db;
    private readonly IHttpClientFactory _httpFactory;
    private readonly IUserProfileService _profiles;
    private readonly IAiEngine _ai;
    private readonly ILogger<MongoCaptureService> _log;

    public MongoCaptureService(
        RadarDatabase db,
        IHttpClientFactory httpFactory,
        IUserProfileService profiles,
        IAiEngine ai,
        ILogger<MongoCaptureService> log)
    {
        _db = db;
        _httpFactory = httpFactory;
        _profiles = profiles;
        _ai = ai;
        _log = log;
    }

    [GeneratedRegex(@"<title[^>]*>(.*?)</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex TitleTag();

    [GeneratedRegex(@"<meta[^>]+property\s*=\s*[""']og:title[""'][^>]+content\s*=\s*[""']([^""']+)[""']", RegexOptions.IgnoreCase)]
    private static partial Regex OgTitle();

    [GeneratedRegex(@"<meta[^>]+(?:name|property)\s*=\s*[""'](?:description|og:description)[""'][^>]+content\s*=\s*[""']([^""']+)[""']", RegexOptions.IgnoreCase)]
    private static partial Regex MetaDescription();

    [GeneratedRegex(@"<meta[^>]+property\s*=\s*[""']og:site_name[""'][^>]+content\s*=\s*[""']([^""']+)[""']", RegexOptions.IgnoreCase)]
    private static partial Regex OgSiteName();

    public async Task<CapturedItem> CaptureAsync(string userId, CaptureMode mode, string input)
    {
        var item = new CapturedItem
        {
            UserId = userId,
            Mode = mode,
            Input = input,
            CapturedAt = DateTime.UtcNow,
            IsProcessing = false,
        };

        if (mode == CaptureMode.Link && Uri.TryCreate(input, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            await EnrichLinkAsync(item, uri);
        }
        else
        {
            // Notes and anything that is not a usable URL: the first line is the headline.
            item.Signal = Shorten(input, 90);
            item.Source = mode.ToString();
        }

        await AddPersonalisedWhyAsync(item, userId);
        await _db.CapturedItems.InsertOneAsync(item);
        return item;
    }

    /// <summary>
    /// Reads the page's own title and description. Failure is expected and fine — plenty of sites
    /// block bots — so we fall back to the host name rather than surfacing an error for something
    /// the user has already successfully saved.
    /// </summary>
    private async Task EnrichLinkAsync(CapturedItem item, Uri uri)
    {
        item.Source = uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase)
            ? uri.Host[4..]
            : uri.Host;

        try
        {
            var client = _httpFactory.CreateClient("Ingestion");
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            var html = await client.GetStringAsync(uri, cts.Token);

            var title = FirstMatch(OgTitle(), html) ?? FirstMatch(TitleTag(), html);
            var description = FirstMatch(MetaDescription(), html);
            var siteName = FirstMatch(OgSiteName(), html);

            if (!string.IsNullOrWhiteSpace(title)) item.Signal = Shorten(Clean(title), 120);
            if (!string.IsNullOrWhiteSpace(description)) item.AiSummary = Shorten(Clean(description), 400);
            if (!string.IsNullOrWhiteSpace(siteName)) item.Source = Clean(siteName);
        }
        catch (Exception ex)
        {
            _log.LogInformation(ex, "Could not read {Url} when capturing; keeping the link with its host name.", uri);
        }

        // Still nothing readable: show the host and path rather than echoing the whole URL back.
        item.Signal ??= $"{item.Source}{(uri.AbsolutePath.Length > 1 ? uri.AbsolutePath.TrimEnd('/') : "")}";
    }

    /// <summary>
    /// The part the page actually promised. Uses the same engine as the feed, so a captured link
    /// gets the same treatment as anything Radar surfaced itself.
    /// </summary>
    private async Task AddPersonalisedWhyAsync(CapturedItem item, string userId)
    {
        var profile = await _profiles.GetCurrentUserAsync();
        if (profile is null || profile.Id != userId) return;

        var prompt = $"""
            THE PERSON
            - Goal: "{profile.PrimaryGoal}"
            - Target role: {Fallback(profile.TargetRole, "not stated")}
            - Location: {Fallback(profile.City, profile.Region)}
            - Interests: {Fallback(string.Join(", ", profile.Interests), "none stated")}
            - Skills: {Fallback(string.Join(", ", profile.Capabilities), "none stated")}
            - Blocked by: {Fallback(string.Join(", ", profile.Problems), "nothing stated")}

            THEY SAVED THIS
            - Headline: {Fallback(item.Signal, item.Input)}
            - Source: {Fallback(item.Source, "unknown")}
            - Description: {Fallback(item.AiSummary, "none available")}

            Return strict JSON with these keys: canConnect (true or false), why (string), topic (string).

            "why": two or three sentences on why this is worth their time, tied to their own goal,
            role and situation. Name specifics. If you cannot honestly connect it to them, set
            "canConnect" to false and leave "why" empty rather than inventing a link.

            "topic": two or three words naming the subject, so it can be added to their roadmap.
            """;

        try
        {
            var result = await _ai.GenerateJsonAsync<CaptureInsight>(
                "capture-why",
                "You explain why a saved item matters to one specific person.",
                prompt);

            if (result is not null && result.CanConnect && !string.IsNullOrWhiteSpace(result.Why))
            {
                item.WhyItMatters = result.Why.Trim();
                if (!string.IsNullOrWhiteSpace(result.Topic)) item.Topic = result.Topic.Trim();
            }
        }
        catch (Exception ex)
        {
            // A capture that saved is still a capture. Never fail the save over the explanation.
            _log.LogWarning(ex, "Could not write a personalised why for capture {CaptureId}", item.Id);
        }

        // No key, a declined connection, or a failure: still say something grounded in the user's
        // own profile rather than leaving the card blank. Saving a link and getting nothing back is
        // exactly what made this feature feel broken.
        if (string.IsNullOrWhiteSpace(item.WhyItMatters))
            item.WhyItMatters = GroundedWhy(profile, item);
    }

    /// <summary>
    /// Built only from things the user actually told us, so it is specific without being invented.
    /// </summary>
    private static string GroundedWhy(UserProfile profile, CapturedItem item)
    {
        var goal = SubjectPhrase.Clean(profile.PrimaryGoal, profile.Interests.FirstOrDefault());
        var source = Fallback(item.Source, "this source");

        var sentence = string.IsNullOrWhiteSpace(profile.PrimaryGoal)
            ? $"You saved this from {source}."
            : $"You saved this from {source} while working towards {goal}.";

        return $"{sentence} Radar will explain how it connects once it has read it. "
             + "You can add it to your roadmap now if you already know where it fits.";
    }

    private sealed class CaptureInsight
    {
        public bool CanConnect { get; set; } = true;
        public string Why { get; set; } = string.Empty;
        public string Topic { get; set; } = string.Empty;
    }

    public async Task<List<CapturedItem>> GetRecentCapturesAsync(string userId)
    {
        return await _db.CapturedItems
            .Find(c => c.UserId == userId)
            .SortByDescending(c => c.CapturedAt)
            .Limit(50)
            .ToListAsync();
    }

    private static string? FirstMatch(Regex regex, string html)
    {
        var match = regex.Match(html);
        return match.Success ? match.Groups[1].Value : null;
    }

    private static string Clean(string value) =>
        WebUtility.HtmlDecode(value).Replace('\n', ' ').Replace('\r', ' ').Trim();

    private static string Shorten(string value, int max)
    {
        var text = value.Trim();
        return text.Length <= max ? text : text[..max].TrimEnd() + "…";
    }

    private static string Fallback(string? value, string? fallback) =>
        !string.IsNullOrWhiteSpace(value) ? value
        : !string.IsNullOrWhiteSpace(fallback) ? fallback
        : "not stated";
}
