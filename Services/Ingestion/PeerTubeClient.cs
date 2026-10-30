using System.Text.Json;
using RadarV2.Models;

namespace RadarV2.Services.Ingestion;

/// <summary>
/// Videos from the open federated network, searched through its public index.
/// </summary>
/// <remarks>
/// A second video source, and the only one Radar can read the words of.
///
/// Everything a lesson shows came from YouTube, which has one API key, one quota, and no
/// legitimate route to a transcript for somebody else's video. PeerTube publishes its search
/// index without a key, states each video's licence as data, and serves caption tracks through
/// the API when the uploader has added them. Measured across five subjects it holds between five
/// hundred and six thousand videos each.
///
/// What it does not solve: a third of those videos have captions, and most of the rest have no
/// licence set, so a video that is both openly licensed and captioned is roughly one in fifteen.
/// This widens where lessons can look and makes a real transcript possible where the author
/// permitted one. It does not make transcripts the normal case.
/// </remarks>
public class PeerTubeClient
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<PeerTubeClient> _log;

    public PeerTubeClient(IHttpClientFactory httpFactory, ILogger<PeerTubeClient> log)
    {
        _httpFactory = httpFactory;
        _log = log;
    }

    /// <summary>
    /// The federated search index, which covers every instance that has opted into it.
    /// </summary>
    /// <remarks>
    /// Searching one instance would return one community's uploads. No key, which is the other
    /// difference from YouTube: there is no quota to spend and no secret to leak.
    /// </remarks>
    public const string SearchIndex = "https://search.joinpeertube.org/api/v1/search/videos";

    /// <summary>How long to wait on a public index that owes Radar nothing.</summary>
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(12);

    /// <summary>Videos matching a query, newest and most relevant as the index ranks them.</summary>
    public async Task<List<OpenVideoRef>> SearchAsync(string query, int count = 10, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];

        try
        {
            using var client = _httpFactory.CreateClient("Ingestion");
            client.Timeout = Deadline;

            var url = $"{SearchIndex}?search={Uri.EscapeDataString(query)}&count={Math.Clamp(count, 1, 25)}";
            var json = await client.GetStringAsync(url, ct);

            using var document = JsonDocument.Parse(json);

            if (!document.RootElement.TryGetProperty("data", out var data)) return [];

            return [.. data.EnumerateArray().Select(Map).Where(v => v is not null).Select(v => v!)];
        }
        catch (Exception ex)
        {
            // A second source that is down is a lesson with fewer videos, not a broken lesson.
            _log.LogWarning(ex, "PeerTube search for {Query} did not complete.", query);
            return [];
        }
    }

    /// <summary>
    /// What a video says, when its author licensed it to be reused and wrote captions for it.
    /// </summary>
    /// <remarks>
    /// Refused outright for anything else. A video with no licence set has granted nothing, which
    /// is the most common state on every platform — see <see cref="OpenVideoLicence"/>.
    /// </remarks>
    public async Task<string?> TranscriptAsync(OpenVideoRef video, CancellationToken ct = default)
    {
        if (!OpenVideoLicence.PermitsReading(video.LicenceId)) return null;
        if (string.IsNullOrWhiteSpace(video.Host) || string.IsNullOrWhiteSpace(video.Uuid)) return null;

        try
        {
            using var client = _httpFactory.CreateClient("Ingestion");
            client.Timeout = Deadline;

            var listed = await client.GetStringAsync(
                $"https://{video.Host}/api/v1/videos/{video.Uuid}/captions", ct);

            using var document = JsonDocument.Parse(listed);

            if (!document.RootElement.TryGetProperty("data", out var tracks)) return null;

            // English where there is one, otherwise whatever the author provided: a summary in
            // plain English can be written from a transcript in another language, and a lesson
            // that only offers one track is better served than not served.
            var chosen = tracks.EnumerateArray()
                .OrderByDescending(t => Language(t).StartsWith("en", StringComparison.OrdinalIgnoreCase))
                .FirstOrDefault();

            if (chosen.ValueKind != JsonValueKind.Object) return null;

            var path = Text(chosen, "captionPath");
            if (string.IsNullOrWhiteSpace(path)) return null;

            var subtitles = await client.GetStringAsync($"https://{video.Host}{path}", ct);
            return Spoken(subtitles);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "PeerTube captions for {Uuid} could not be read.", video.Uuid);
            return null;
        }
    }

    /// <summary>
    /// The words out of a subtitle file, without the timings.
    /// </summary>
    /// <remarks>
    /// WebVTT and SubRip both interleave cue numbers and timestamps with the words. Left in, a
    /// model summarising the file spends its attention on arrows and clock times.
    ///
    /// Consecutive duplicates are dropped because rolling captions repeat each line as the next
    /// one scrolls in, which triples the length of the text for no added meaning.
    /// </remarks>
    public static string Spoken(string? subtitles)
    {
        if (string.IsNullOrWhiteSpace(subtitles)) return string.Empty;

        var lines = new List<string>();

        foreach (var raw in subtitles.Split('\n'))
        {
            var line = raw.Trim().Replace("\r", string.Empty);

            if (line.Length == 0) continue;
            if (line.StartsWith("WEBVTT", StringComparison.OrdinalIgnoreCase)) continue;
            if (line.Contains("-->", StringComparison.Ordinal)) continue;
            if (line.StartsWith("NOTE", StringComparison.Ordinal)) continue;

            // A bare number is a SubRip cue index.
            if (int.TryParse(line, out _)) continue;

            // Cue settings and speaker markup, which are not speech.
            line = System.Text.RegularExpressions.Regex.Replace(line, "<[^>]+>", string.Empty).Trim();
            if (line.Length == 0) continue;

            if (lines.Count > 0 && lines[^1].Equals(line, StringComparison.OrdinalIgnoreCase)) continue;

            lines.Add(line);
        }

        return string.Join(" ", lines).Trim();
    }

    private static OpenVideoRef? Map(JsonElement video)
    {
        var uuid = Text(video, "uuid");
        var url = Text(video, "url");

        if (string.IsNullOrWhiteSpace(uuid) || string.IsNullOrWhiteSpace(url)) return null;

        // Live streams and anything flagged are not lesson material.
        if (video.TryGetProperty("isLive", out var live) && live.ValueKind == JsonValueKind.True) return null;
        if (video.TryGetProperty("nsfw", out var nsfw) && nsfw.ValueKind == JsonValueKind.True) return null;

        int? licenceId = null;
        if (video.TryGetProperty("licence", out var licence)
            && licence.ValueKind == JsonValueKind.Object
            && licence.TryGetProperty("id", out var licenceEl)
            && licenceEl.TryGetInt32(out var parsedLicence))
        {
            licenceId = parsedLicence;
        }

        return new OpenVideoRef
        {
            Uuid = uuid,
            Host = new Uri(url).Host,
            Title = Text(video, "name"),
            Description = Text(video, "description"),
            ChannelTitle = Nested(video, "channel", "displayName") ?? Nested(video, "account", "displayName") ?? string.Empty,
            Url = url,
            EmbedUrl = Text(video, "embedUrl"),
            ThumbnailUrl = Text(video, "thumbnailUrl"),
            PublishedAt = video.TryGetProperty("publishedAt", out var published)
                          && published.TryGetDateTime(out var at) ? at : null,
            DurationSeconds = video.TryGetProperty("duration", out var duration)
                              && duration.TryGetInt32(out var seconds) ? seconds : null,
            LicenceId = licenceId,
        };
    }

    private static string Language(JsonElement track) =>
        Nested(track, "language", "id") ?? Nested(track, "language", "label") ?? string.Empty;

    private static string Text(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static string? Nested(JsonElement element, string property, string child) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Object
            ? Text(value, child) is { Length: > 0 } text ? text : null
            : null;
}
