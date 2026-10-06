using System.Text.RegularExpressions;
using System.Xml.Linq;
using RadarV2.Models;

namespace RadarV2.Services.Ingestion;

/// <summary>
/// Fetches YouTube channel videos through the public Atom feed
/// (youtube.com/feeds/videos.xml?channel_id=…). No API key is required. Channels configured by
/// handle are resolved to a channel id from the channel page and cached by the caller.
/// </summary>
public class YouTubeClient
{
    private static readonly XNamespace Atom = "http://www.w3.org/2005/Atom";
    private static readonly XNamespace Yt = "http://www.youtube.com/xml/schemas/2015";
    private static readonly XNamespace Media = "http://search.yahoo.com/mrss/";

    private readonly HttpClient _http;

    public YouTubeClient(IHttpClientFactory httpFactory) => _http = httpFactory.CreateClient("YouTube");

    /// <summary>Resolves a @handle (or channel URL) to its UC… channel id via the channel page.</summary>
    public async Task<string?> ResolveChannelIdAsync(string handleOrUrl, CancellationToken ct)
    {
        var url = handleOrUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? handleOrUrl
            : $"https://www.youtube.com/{handleOrUrl.TrimStart('/')}";

        var html = await _http.GetStringAsync(url, ct);

        // The channel page embeds "channelId":"UC…"; the meta/externalId forms are fallbacks.
        foreach (var pattern in new[]
        {
            "\"channelId\":\"(UC[\\w-]{20,})\"",
            "\"externalId\":\"(UC[\\w-]{20,})\"",
            "<meta[^>]+itemprop=\"identifier\"[^>]+content=\"(UC[\\w-]{20,})\"",
        })
        {
            var match = Regex.Match(html, pattern);
            if (match.Success) return match.Groups[1].Value;
        }
        return null;
    }

    /// <summary>Parses a channel's recent videos into feed items.</summary>
    public async Task<List<RawFeedItem>> FetchChannelFeedAsync(YouTubeSource source, string channelId, CancellationToken ct)
    {
        var xml = await _http.GetStringAsync($"https://www.youtube.com/feeds/videos.xml?channel_id={channelId}", ct);
        var doc = XDocument.Parse(xml);

        var items = new List<RawFeedItem>();
        foreach (var entry in doc.Descendants(Atom + "entry").Take(15))
        {
            var videoId = entry.Element(Yt + "videoId")?.Value;
            var link = entry.Element(Atom + "link")?.Attribute("href")?.Value
                       ?? (string.IsNullOrWhiteSpace(videoId) ? null : $"https://www.youtube.com/watch?v={videoId}");
            if (string.IsNullOrWhiteSpace(link)) continue;

            var group = entry.Element(Media + "group");
            var description = group?.Element(Media + "description")?.Value ?? string.Empty;
            var thumbnail = group?.Element(Media + "thumbnail")?.Attribute("url")?.Value;

            items.Add(new RawFeedItem
            {
                SourceId = source.Id,
                SourceName = source.Name,
                Layer = source.Layer,
                Tier = 2,
                DefaultType = ContentType.Video,
                Topics = [.. source.Topics],
                Title = entry.Element(Atom + "title")?.Value?.Trim() ?? string.Empty,
                Url = link,
                UrlHash = RssIngestionService.HashUrl(link),
                Description = Truncate(description, 1500),
                PublishedAt = DateTime.TryParse(entry.Element(Atom + "published")?.Value, out var published) ? published : DateTime.UtcNow,
                ThumbnailUrl = thumbnail,
            });
        }
        return items;
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}
