using System.Text;
using System.Text.Json;

namespace RadarV2.Services.Ingestion;

/// <summary>
/// Taddy GraphQL client for fetching podcast transcripts.
/// Pro plan ($75/mo). Optional — skipped if credentials are absent.
/// Rate limit: 100 req/hour. Use for transcript enrichment only.
/// </summary>
public class TaddyClient
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly IConfiguration     _config;
    private readonly ILogger<TaddyClient> _log;

    public TaddyClient(IHttpClientFactory httpFactory, IConfiguration config, ILogger<TaddyClient> log)
    {
        _httpFactory = httpFactory;
        _config      = config;
        _log         = log;
    }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_config["Taddy:UserId"]) &&
        !string.IsNullOrWhiteSpace(_config["Taddy:ApiKey"]);

    /// <summary>
    /// Fetches the transcript for a specific episode by its Taddy UUID or RSS episode URL.
    /// Returns null if not found or not available.
    /// </summary>
    public async Task<string?> GetEpisodeTranscriptAsync(string episodeIdentifier, bool isUrl, CancellationToken ct)
    {
        if (!IsConfigured) return null;

        try
        {
            string query;
            if (isUrl)
            {
                query = $$"""
                    {
                      getEpisodesByUrl(url: "{{Escape(episodeIdentifier)}}") {
                        uuid name transcript { words { text } }
                      }
                    }
                    """;
            }
            else
            {
                query = $$"""
                    {
                      getEpisode(uuid: "{{Escape(episodeIdentifier)}}") {
                        uuid name transcript { words { text } }
                      }
                    }
                    """;
            }

            var result = await ExecuteGraphQlAsync(query, ct);
            if (result is null) return null;

            // Try both response shapes
            JsonElement? episodeEl = null;
            if (result.Value.TryGetProperty("getEpisodesByUrl", out var byUrl) && byUrl.ValueKind != JsonValueKind.Null)
                episodeEl = byUrl;
            else if (result.Value.TryGetProperty("getEpisode", out var byId) && byId.ValueKind != JsonValueKind.Null)
                episodeEl = byId;

            if (episodeEl is null) return null;
            return ExtractTranscript(episodeEl.Value);
        }
        catch (Exception ex)
        {
            _log.LogWarning("Taddy transcript fetch failed for '{Id}': {Message}", episodeIdentifier, ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Uses searchForTerm to find podcast episodes matching a query — returns episodes that include
    /// transcripts when available. Complements PodcastIndex discovery with transcript-first content.
    /// </summary>
    public async Task<List<TaddyEpisode>> SearchEpisodesAsync(string term, int maxResults = 10, CancellationToken ct = default)
    {
        if (!IsConfigured) return [];

        var query = $$"""
            {
              searchForTerm(term: "{{Escape(term)}}", filterForTypes: PODCASTEPISODE, limitPerPage: {{maxResults}}) {
                searchResults {
                  ... on PodcastEpisode {
                    uuid name datePublished duration audioUrl
                    transcript { words { text } }
                    podcastSeries { name rssUrl }
                  }
                }
              }
            }
            """;

        try
        {
            var result = await ExecuteGraphQlAsync(query, ct);
            if (result is null) return [];

            if (!result.Value.TryGetProperty("searchForTerm", out var search)) return [];
            if (!search.TryGetProperty("searchResults", out var results)) return [];

            var episodes = new List<TaddyEpisode>();
            foreach (var ep in results.EnumerateArray())
            {
                var uuid     = ep.TryGetProperty("uuid",          out var u) ? u.GetString()   ?? string.Empty : string.Empty;
                var name     = ep.TryGetProperty("name",          out var n) ? n.GetString()   ?? string.Empty : string.Empty;
                var audioUrl = ep.TryGetProperty("audioUrl",      out var a) ? a.GetString()   : null;
                var duration = ep.TryGetProperty("duration",      out var d) ? d.GetInt32()    : (int?)null;
                var transcript = ExtractTranscript(ep);

                DateTime published = DateTime.UtcNow;
                if (ep.TryGetProperty("datePublished", out var dp))
                    published = DateTimeOffset.FromUnixTimeSeconds(dp.GetInt64()).UtcDateTime;

                string podcastName = string.Empty;
                string? rssUrl     = null;
                if (ep.TryGetProperty("podcastSeries", out var series))
                {
                    podcastName = series.TryGetProperty("name",   out var pn) ? pn.GetString() ?? string.Empty : string.Empty;
                    rssUrl      = series.TryGetProperty("rssUrl", out var ru) ? ru.GetString()  : null;
                }

                if (!string.IsNullOrWhiteSpace(uuid))
                    episodes.Add(new TaddyEpisode(uuid, name, audioUrl, transcript, published, duration, podcastName, rssUrl));
            }
            return episodes;
        }
        catch (Exception ex)
        {
            _log.LogWarning("Taddy searchForTerm '{Term}' failed: {Message}", term, ex.Message);
            return [];
        }
    }

    /// <summary>
    /// Searches for a podcast series by name and returns its RSS feed URL.
    /// </summary>
    public async Task<string?> GetPodcastRssUrlAsync(string podcastName, CancellationToken ct)
    {
        if (!IsConfigured) return null;

        var query = $$"""
            {
              getPodcastSeries(name: "{{Escape(podcastName)}}") {
                uuid rssUrl
              }
            }
            """;

        try
        {
            var result = await ExecuteGraphQlAsync(query, ct);
            if (result is null) return null;
            if (!result.Value.TryGetProperty("getPodcastSeries", out var series)) return null;
            return series.TryGetProperty("rssUrl", out var rss) ? rss.GetString() : null;
        }
        catch (Exception ex)
        {
            _log.LogWarning("Taddy RSS lookup failed for '{Podcast}': {Message}", podcastName, ex.Message);
            return null;
        }
    }

    // ── Internal ──────────────────────────────────────────────────────────────

    private async Task<JsonElement?> ExecuteGraphQlAsync(string query, CancellationToken ct)
    {
        var body    = JsonSerializer.Serialize(new { query });
        var client  = _httpFactory.CreateClient("Taddy");
        var request = new HttpRequestMessage(HttpMethod.Post, "")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

        using var response = await client.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) return null;

        var json = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);

        if (!doc.RootElement.TryGetProperty("data", out var data)) return null;
        // Clone so it survives doc disposal
        return JsonDocument.Parse(data.GetRawText()).RootElement;
    }

    private static string ExtractTranscript(JsonElement episode)
    {
        if (!episode.TryGetProperty("transcript", out var transcript)) return string.Empty;
        if (transcript.ValueKind == JsonValueKind.Null) return string.Empty;

        if (!transcript.TryGetProperty("words", out var words)) return string.Empty;
        var sb = new StringBuilder();
        foreach (var word in words.EnumerateArray())
            if (word.TryGetProperty("text", out var text))
                sb.Append(text.GetString()).Append(' ');

        return sb.ToString().Trim();
    }

    private static string Escape(string s) => s.Replace("\"", "\\\"");
}

public record TaddyEpisode(
    string   Uuid,
    string   Name,
    string?  AudioUrl,
    string   Transcript,
    DateTime PublishedAt,
    int?     DurationSeconds,
    string   PodcastName,
    string?  PodcastRssUrl);
