using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using RadarV2.Models;
using System.Text.RegularExpressions;

namespace RadarV2.Services.Ingestion;



public partial class RssIngestionService
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<RssIngestionService> _log;

    private static readonly XNamespace Atom = "http://www.w3.org/2005/Atom";
    private static readonly XNamespace Dc   = "http://purl.org/dc/elements/1.1/";

    private readonly IConfiguration _config;

    public RssIngestionService(IHttpClientFactory httpFactory, IConfiguration config, ILogger<RssIngestionService> log)
    {
        _httpFactory = httpFactory;
        _config = config;
        _log = log;
    }

    /// <summary>
    /// What one fetch of one source did. Returned rather than logged, so the caller can record it.
    /// </summary>
    /// <remarks>
    /// An empty list used to mean four different things — the feed refused us, the request never
    /// completed, the XML would not parse, or the publisher genuinely had nothing new. They are
    /// indistinguishable to the caller, and that is most of why a silent source took an afternoon
    /// to diagnose.
    /// </remarks>
    public sealed record FetchOutcome(List<RawFeedItem> Items, int HttpStatus, string? Error);

    public async Task<FetchOutcome> FetchAsync(FeedSource source, CancellationToken ct)
    {
        return source.SourceType switch
        {
            FeedSourceType.OpenAlexApi => await FetchOpenAlexAsync(source, ct),
            _                          => await FetchXmlFeedAsync(source, ct)
        };
    }

    // ── RSS 2.0 + Atom ────────────────────────────────────────────────────────

    private async Task<FetchOutcome> FetchXmlFeedAsync(FeedSource source, CancellationToken ct)
    {
        var items = new List<RawFeedItem>();
        var status = 0;
        string? error = null;

        try
        {
            var client = _httpFactory.CreateClient("Ingestion");
            using var response = await client.GetAsync(source.Url, ct);
            status = (int)response.StatusCode;

            if (!response.IsSuccessStatusCode)
            {
                error = $"HTTP {(int)response.StatusCode} {response.StatusCode}";
                _log.LogWarning("Feed {Source} returned {Status}; it published nothing as far as Radar can tell.",
                    source.Name, response.StatusCode);
                return new FetchOutcome(items, status, error);
            }

            var xml = await response.Content.ReadAsStringAsync(ct);

            // Parsed leniently about its preamble only. Twenty of this registry's sources serve a
            // current feed behind a byte-order mark or a blank line, which a strict reader rejects
            // outright - they were logged as "fetch failed" and dropped every night.
            var doc = FeedXml.Parse(xml);
            if (doc is null)
            {
                error = "the body was not a feed";
                _log.LogWarning(
                    "Feed {Source} returned {Status} but the body is not a feed.", source.Name, status);
                return new FetchOutcome(items, status, error);
            }

            // Detect Atom vs RSS
            if (doc.Root?.Name.LocalName == "feed")
                items = ParseAtom(doc, source);
            else
                items = ParseRss(doc, source);
        }
        catch (Exception ex)
        {
            error = $"{ex.GetType().Name}: {ex.Message}";
            _log.LogWarning("Feed fetch failed for {Source}: {Message}", source.Name, ex.Message);
        }

        if (error is null && items.Count == 0)
            error = "Fetched successfully but the feed offered no items.";

        return new FetchOutcome(items, status, error);
    }

    /// <summary>
    /// Removes a CDATA wrapper that arrived as text rather than as markup.
    /// </summary>
    /// <remarks>
    /// Some publishers escape their own CDATA, so the element contains
    /// <c>&amp;lt;![CDATA[Real Title]]&amp;gt;</c>. XML decoding turns that into the literal
    /// characters "&lt;![CDATA[Real Title]]&gt;", which is then a perfectly valid string as far
    /// as everything downstream is concerned — and Pharmacy Times headlines reached the feed
    /// reading "&lt;![CDATA[Kidney Week 2026: What to Know".
    /// </remarks>
    public static string Unwrap(string? text)
    {
        var value = (text ?? string.Empty).Trim();

        const string open = "<![CDATA[";
        const string close = "]]>";

        if (value.StartsWith(open, StringComparison.Ordinal) && value.EndsWith(close, StringComparison.Ordinal))
            value = value[open.Length..^close.Length].Trim();

        return value;
    }

    private static List<RawFeedItem> ParseRss(XDocument doc, FeedSource source)
    {
        return doc.Descendants("item")
            .Take(15)
            .Select(item =>
            {
                var url = item.Element("link")?.Value?.Trim() ?? string.Empty;
                // Podcast RSS carries the audio file in <enclosure url=…>. Capture it (and the
                // duration, when present) so the player has something to play; without it a
                // podcast item is just a headline with no audio.
                var enclosure = item.Elements("enclosure")
                                    .FirstOrDefault(e => (e.Attribute("type")?.Value ?? "")
                                        .StartsWith("audio", StringComparison.OrdinalIgnoreCase))
                                ?? item.Element("enclosure");
                var audioUrl  = enclosure?.Attribute("url")?.Value?.Trim();
                var durRaw    = item.Elements().FirstOrDefault(e => e.Name.LocalName == "duration")
                                    ?.Value;
                var duration  = ParseDuration(durRaw);

                return new RawFeedItem
                {
                    SourceId        = source.Id,
                    SourceName      = source.Name,
                    Layer           = source.Layer,
                    Tier            = source.Tier,
                    DefaultType     = source.DefaultContentType,
                    Topics          = source.Topics,
                    Title           = Unwrap(item.Element("title")?.Value),
                    Url             = url,
                    UrlHash         = HashUrl(url),
                    Description     = StripHtml(Unwrap(item.Element("description")?.Value)),
                    PublishedAt     = ParseDate(item.Element("pubDate")?.Value
                                          ?? item.Element(Dc + "date")?.Value),
                    Author          = item.Element("author")?.Value?.Trim()
                                   ?? item.Element(Dc + "creator")?.Value?.Trim(),
                    AudioUrl        = string.IsNullOrWhiteSpace(audioUrl) ? null : audioUrl,
                    DurationSeconds = duration,
                    ThumbnailUrl    = ExtractThumbnail(item),
                };
            })
            .Where(i => !string.IsNullOrWhiteSpace(i.Url))
            .ToList();
    }

    private static List<RawFeedItem> ParseAtom(XDocument doc, FeedSource source)
    {
        return doc.Descendants(Atom + "entry")
            .Take(15)
            .Select(entry =>
            {
                var url = entry.Elements(Atom + "link")
                               .FirstOrDefault(l => l.Attribute("rel")?.Value != "alternate" || true)
                               ?.Attribute("href")?.Value?.Trim() ?? string.Empty;
                return new RawFeedItem
                {
                    SourceId    = source.Id,
                    SourceName  = source.Name,
                    Layer       = source.Layer,
                    Tier        = source.Tier,
                    DefaultType = source.DefaultContentType,
                    Topics      = source.Topics,
                    Title       = Unwrap(entry.Element(Atom + "title")?.Value),
                    Url         = url,
                    UrlHash     = HashUrl(url),
                    Description = StripHtml(entry.Element(Atom + "summary")?.Value
                                         ?? entry.Element(Atom + "content")?.Value
                                         ?? string.Empty),
                    PublishedAt = ParseDate(entry.Element(Atom + "published")?.Value
                                         ?? entry.Element(Atom + "updated")?.Value),
                    Author      = entry.Element(Atom + "author")?.Element(Atom + "name")?.Value?.Trim(),
                    ThumbnailUrl = ExtractThumbnail(entry),
                };
            })
            .Where(i => !string.IsNullOrWhiteSpace(i.Url))
            .ToList();
    }

    // ── OpenAlex REST API ─────────────────────────────────────────────────────

    private async Task<FetchOutcome> FetchOpenAlexAsync(FeedSource source, CancellationToken ct)
    {
        var items = new List<RawFeedItem>();
        var status = 0;
        string? error = null;

        try
        {
            var client = _httpFactory.CreateClient("Ingestion");
            var apiKey = _config["OpenAlex:ApiKey"];
            var suffix = string.IsNullOrWhiteSpace(apiKey) ? string.Empty : $"&api_key={apiKey}";

            // Cap the window at today, or sorting by publication_date descending returns the
            // future. OpenAlex carries records dated 2045 and 2031 — publisher metadata, not
            // OpenAlex's fault, but sorting newest-first puts them on top of every query. That is
            // how a 2045 hospitality paper reached a feed labelled "just now".
            //
            // Substituted here rather than written into the registry, because "today" in a static
            // string is only true on the day it is written.
            var requestUrl = source.Url.Replace("{today}", DateTime.UtcNow.ToString("yyyy-MM-dd"), StringComparison.Ordinal);

            var json = await client.GetStringAsync(requestUrl + suffix, ct);
            using var doc = JsonDocument.Parse(json);

            status = 200;

            if (!doc.RootElement.TryGetProperty("results", out var results))
                return new FetchOutcome(items, status, "The response carried no results array.");

            foreach (var work in results.EnumerateArray())
            {
                var url = work.TryGetProperty("doi", out var doi) && doi.ValueKind != JsonValueKind.Null
                    ? $"https://doi.org/{doi.GetString()}"
                    : work.TryGetProperty("id", out var id) ? id.GetString() ?? string.Empty : string.Empty;

                if (string.IsNullOrWhiteSpace(url)) continue;

                var title = work.TryGetProperty("title", out var t) ? t.GetString() ?? string.Empty : string.Empty;
                var abstract_ = work.TryGetProperty("abstract_inverted_index", out var inv) && inv.ValueKind == JsonValueKind.Object
                    ? ReconstructAbstract(inv) : string.Empty;

                var authors = new List<string>();
                if (work.TryGetProperty("authorships", out var ships))
                    foreach (var ship in ships.EnumerateArray().Take(3))
                        if (ship.TryGetProperty("author", out var auth) && auth.TryGetProperty("display_name", out var dn))
                            authors.Add(dn.GetString() ?? string.Empty);

                DateTime published = DateTime.UtcNow;
                if (work.TryGetProperty("publication_date", out var pd) && DateTime.TryParse(pd.GetString(), out var d))
                    published = d;

                items.Add(new RawFeedItem
                {
                    SourceId    = source.Id,
                    SourceName  = source.Name,
                    Layer       = source.Layer,
                    Tier        = source.Tier,
                    DefaultType = ContentType.ResearchPaper,
                    Topics      = source.Topics,
                    Title       = title,
                    Url         = url,
                    UrlHash     = HashUrl(url),
                    Description = abstract_,
                    PublishedAt = published,
                    Authors     = authors,
                });
            }
        }
        catch (Exception ex)
        {
            error = $"{ex.GetType().Name}: {ex.Message}";
            _log.LogWarning("OpenAlex fetch failed for {Source}: {Message}", source.Name, ex.Message);
        }

        if (error is null && items.Count == 0)
            error = "Fetched successfully but the query returned nothing.";

        return new FetchOutcome(items, status, error);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    public static string HashUrl(string url)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(url.Trim().ToLowerInvariant()));
        return Convert.ToHexString(bytes)[..16];
    }

    /// <summary>
    /// Pulls a representative image from an RSS/Atom entry. Checks media:thumbnail, media:content
    /// (image), and image enclosures — matched by local name so it works regardless of prefix.
    /// </summary>
    private static string? ExtractThumbnail(XElement entry)
    {
        var thumbnail = entry.Elements()
            .FirstOrDefault(e => e.Name.LocalName == "thumbnail" && e.Attribute("url") is not null)
            ?.Attribute("url")?.Value;
        if (!string.IsNullOrWhiteSpace(thumbnail)) return thumbnail.Trim();

        foreach (var content in entry.Elements().Where(e => e.Name.LocalName == "content"))
        {
            var url = content.Attribute("url")?.Value;
            var type = content.Attribute("type")?.Value ?? string.Empty;
            var medium = content.Attribute("medium")?.Value ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(url) &&
                (medium.Equals("image", StringComparison.OrdinalIgnoreCase) ||
                 type.StartsWith("image", StringComparison.OrdinalIgnoreCase)))
                return url.Trim();
        }

        var enclosure = entry.Elements()
            .FirstOrDefault(e => e.Name.LocalName == "enclosure" &&
                (e.Attribute("type")?.Value ?? string.Empty).StartsWith("image", StringComparison.OrdinalIgnoreCase))
            ?.Attribute("url")?.Value;
        if (!string.IsNullOrWhiteSpace(enclosure)) return enclosure.Trim();

        // Last, the picture the publisher put in the body.
        //
        // The three strategies above read the structured places a feed can declare an image, and
        // most publishers do not use any of them. Measured across 918 items from 58 live feeds:
        // 34% carried a picture, and another 33% had one sitting in the description HTML that
        // nothing was looking for. InfoQ had none of fifteen and fifteen in the body; Simon
        // Willison none of thirty and twelve; BusinessDay Nigeria none of five and all five.
        //
        // Worth the reach because the feed is a scrolling wall of cards. A card with no image
        // falls back to a layer gradient, which is deliberate and fine once; two thirds of the
        // way down a feed it is just grey.
        return FirstImageInBody(entry);
    }

    /// <summary>The first real image in a feed entry's HTML body, or null.</summary>
    /// <remarks>
    /// Deliberately narrow about what counts. A body is full of images that are not the article:
    /// tracking pixels a single pixel wide, share buttons, author avatars, "powered by" badges,
    /// and the feed's own logo repeated on every entry. Taking the first img indiscriminately
    /// would give a wall of identical one-pixel gifs.
    ///
    /// So anything whose URL reads like furniture is skipped, and the search stops at the first
    /// plausible candidate rather than collecting them - the first content image in an article
    /// body is almost always the lead image, which is the one the card wants.
    /// </remarks>
    internal static string? FirstImageInBody(XElement entry)
    {
        var body = entry.Elements()
            .Where(e => e.Name.LocalName is "description" or "encoded" or "summary" or "content")
            .Select(e => e.Value)
            .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

        if (string.IsNullOrWhiteSpace(body)) return null;

        foreach (Match match in ImageTag().Matches(body))
        {
            var url = System.Net.WebUtility.HtmlDecode(match.Groups[1].Value).Trim();

            if (url.Length == 0) continue;

            // Relative paths cannot be rendered from another origin, and guessing the base from
            // the feed URL is how you end up hotlinking the wrong site's image.
            if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                && !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) continue;

            if (IsFurniture(url)) continue;

            return url;
        }

        return null;
    }

    /// <summary>Whether a URL reads like a tracking pixel, a badge or a button.</summary>
    /// <remarks>
    /// Matched on the URL because the markup rarely says. Width and height attributes are absent
    /// more often than present, and a tracking pixel is usually styled rather than sized - so the
    /// path is the only reliable signal, and publishers are consistent about naming these.
    /// </remarks>
    private static bool IsFurniture(string url) =>
        Furniture.Any(hint => url.Contains(hint, StringComparison.OrdinalIgnoreCase));

    private static readonly string[] Furniture =
    [
        "pixel", "1x1", "spacer", "blank.gif", "transparent",
        "feedburner", "feedblitz", "feedsportal", "rsspump", "gravatar",
        "avatar", "button", "badge", "icon", "logo", "share", "social",
        "doubleclick", "googleads", "/ads/", "stat?", "track", "beacon",
        "emoji", "smiley",
    ];

    /// <remarks>
    /// Quotes are doubled rather than backslash-escaped: this is a verbatim string, where a
    /// backslash is a literal backslash and only "" means a quote character.
    /// </remarks>
    [GeneratedRegex(@"<img[^>]+src\s*=\s*[""']([^""']+)", RegexOptions.IgnoreCase, 200)]
    private static partial Regex ImageTag();

    private static DateTime ParseDate(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return DateTime.UtcNow;
        return DateTime.TryParse(raw, out var d) ? d.ToUniversalTime() : DateTime.UtcNow;
    }

    // itunes:duration comes as "3721", "62:01" or "1:02:01" depending on the publisher.
    private static int? ParseDuration(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        raw = raw.Trim();
        if (int.TryParse(raw, out var secs)) return secs;
        var parts = raw.Split(':');
        if (parts.Length is 2 or 3 && parts.All(p => int.TryParse(p, out _)))
        {
            var nums = parts.Select(p => int.Parse(p)).ToArray();
            return nums.Length == 2 ? nums[0] * 60 + nums[1]
                                   : nums[0] * 3600 + nums[1] * 60 + nums[2];
        }
        return null;
    }

    private static string StripHtml(string html)
    {
        if (string.IsNullOrWhiteSpace(html)) return string.Empty;
        return System.Text.RegularExpressions.Regex.Replace(html, "<[^>]+>", " ")
               .Replace("&amp;", "&").Replace("&lt;", "<").Replace("&gt;", ">")
               .Replace("&quot;", "\"").Replace("&#39;", "'")
               .Trim();
    }

    private static string ReconstructAbstract(JsonElement invertedIndex)
    {
        var words = new SortedDictionary<int, string>();
        foreach (var prop in invertedIndex.EnumerateObject())
            foreach (var pos in prop.Value.EnumerateArray())
                words[pos.GetInt32()] = prop.Name;
        return string.Join(" ", words.Values);
    }
}

public class RawFeedItem
{
    public string SourceId { get; set; } = string.Empty;
    public string SourceName { get; set; } = string.Empty;
    public ContentLayer Layer { get; set; }
    public int Tier { get; set; }
    public ContentType DefaultType { get; set; }
    public List<string> Topics { get; set; } = [];
    public string Title { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string UrlHash { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateTime PublishedAt { get; set; }
    public string? Author { get; set; }
    public List<string> Authors { get; set; } = [];
    // Podcast-specific
    public string? AudioUrl { get; set; }
    public string? TranscriptText { get; set; }
    public int? DurationSeconds { get; set; }
    public string? PodcastFeedId { get; set; }
    public string? ThumbnailUrl { get; set; }
}
