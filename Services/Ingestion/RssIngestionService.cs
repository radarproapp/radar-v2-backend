using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using RadarV2.Models;

namespace RadarV2.Services.Ingestion;

public class RssIngestionService
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

    public async Task<List<RawFeedItem>> FetchAsync(FeedSource source, CancellationToken ct)
    {
        return source.SourceType switch
        {
            FeedSourceType.OpenAlexApi => await FetchOpenAlexAsync(source, ct),
            _                          => await FetchXmlFeedAsync(source, ct)
        };
    }

    // ── RSS 2.0 + Atom ────────────────────────────────────────────────────────

    private async Task<List<RawFeedItem>> FetchXmlFeedAsync(FeedSource source, CancellationToken ct)
    {
        var items = new List<RawFeedItem>();
        try
        {
            var client = _httpFactory.CreateClient("Ingestion");
            using var response = await client.GetAsync(source.Url, ct);
            if (!response.IsSuccessStatusCode) return items;

            var xml = await response.Content.ReadAsStringAsync(ct);
            var doc = XDocument.Parse(xml);

            // Detect Atom vs RSS
            if (doc.Root?.Name.LocalName == "feed")
                items = ParseAtom(doc, source);
            else
                items = ParseRss(doc, source);
        }
        catch (Exception ex)
        {
            _log.LogWarning("Feed fetch failed for {Source}: {Message}", source.Name, ex.Message);
        }
        return items;
    }

    private static List<RawFeedItem> ParseRss(XDocument doc, FeedSource source)
    {
        return doc.Descendants("item")
            .Take(15)
            .Select(item =>
            {
                var url = item.Element("link")?.Value?.Trim() ?? string.Empty;
                return new RawFeedItem
                {
                    SourceId      = source.Id,
                    SourceName    = source.Name,
                    Layer         = source.Layer,
                    Tier          = source.Tier,
                    DefaultType   = source.DefaultContentType,
                    Topics        = source.Topics,
                    Title         = item.Element("title")?.Value?.Trim() ?? string.Empty,
                    Url           = url,
                    UrlHash       = HashUrl(url),
                    Description   = StripHtml(item.Element("description")?.Value ?? string.Empty),
                    PublishedAt   = ParseDate(item.Element("pubDate")?.Value
                                          ?? item.Element(Dc + "date")?.Value),
                    Author        = item.Element("author")?.Value?.Trim()
                                 ?? item.Element(Dc + "creator")?.Value?.Trim(),
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
                    Title       = entry.Element(Atom + "title")?.Value?.Trim() ?? string.Empty,
                    Url         = url,
                    UrlHash     = HashUrl(url),
                    Description = StripHtml(entry.Element(Atom + "summary")?.Value
                                         ?? entry.Element(Atom + "content")?.Value
                                         ?? string.Empty),
                    PublishedAt = ParseDate(entry.Element(Atom + "published")?.Value
                                         ?? entry.Element(Atom + "updated")?.Value),
                    Author      = entry.Element(Atom + "author")?.Element(Atom + "name")?.Value?.Trim(),
                };
            })
            .Where(i => !string.IsNullOrWhiteSpace(i.Url))
            .ToList();
    }

    // ── OpenAlex REST API ─────────────────────────────────────────────────────

    private async Task<List<RawFeedItem>> FetchOpenAlexAsync(FeedSource source, CancellationToken ct)
    {
        var items = new List<RawFeedItem>();
        try
        {
            var client = _httpFactory.CreateClient("Ingestion");
            var apiKey = _config["OpenAlex:ApiKey"];
            var suffix = string.IsNullOrWhiteSpace(apiKey) ? string.Empty : $"&api_key={apiKey}";
            var json = await client.GetStringAsync(source.Url + suffix, ct);
            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("results", out var results)) return items;

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
            _log.LogWarning("OpenAlex fetch failed for {Source}: {Message}", source.Name, ex.Message);
        }
        return items;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    public static string HashUrl(string url)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(url.Trim().ToLowerInvariant()));
        return Convert.ToHexString(bytes)[..16];
    }

    private static DateTime ParseDate(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return DateTime.UtcNow;
        return DateTime.TryParse(raw, out var d) ? d.ToUniversalTime() : DateTime.UtcNow;
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
}
