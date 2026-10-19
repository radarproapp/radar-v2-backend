using System.Net;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using MongoDB.Driver;
using RadarV2.Data;
using RadarV2.Models;

namespace RadarV2.Services.Ingestion;

/// <summary>
/// Fetches opportunities from public RSS/Atom feeds and stores them.
///
/// This is the missing half of the Opportunity Hub. The matching, tiering, daily batching and
/// impression tracking were all built against a collection of thirteen hardcoded seed records that
/// nothing ever added to — so "new opportunities every day" was impossible by construction, and
/// the batch honestly reported zero new items forever.
///
/// Deliberately conservative about what it claims:
///  - the type is inferred from wording and falls back to the feed's own default, never guessed
///    into something specific like "Scholarship" without evidence;
///  - a deadline is only set when a real date is found in the text, because a wrong deadline is
///    worse than none — it makes someone miss a real one or panic about a fake one;
///  - everything links back to the original posting, since Radar is not the source of truth for
///    eligibility.
/// </summary>
public partial class OpportunityIngestionService
{
    private readonly RadarDatabase _db;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<OpportunityIngestionService> _log;

    public OpportunityIngestionService(
        RadarDatabase db,
        IHttpClientFactory httpFactory,
        ILogger<OpportunityIngestionService> log)
    {
        _db = db;
        _httpFactory = httpFactory;
        _log = log;
    }

    private static readonly XNamespace Atom = "http://www.w3.org/2005/Atom";

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex HtmlTags();

    /// <summary>
    /// Deadline phrasing as it actually appears in these posts: "Deadline: 15 December 2026",
    /// "Application Deadline: December 15, 2026", "Closes 15/12/2026".
    /// </summary>
    [GeneratedRegex(@"(?:deadline|closes?|apply by|closing date)\s*[:\-]?\s*((?:\d{1,2}\s+)?[A-Z][a-z]+\s+\d{1,2},?\s+\d{4}|\d{1,2}\s+[A-Z][a-z]+\s+\d{4}|\d{1,2}[/-]\d{1,2}[/-]\d{4})",
        RegexOptions.IgnoreCase)]
    private static partial Regex DeadlinePhrase();

    public async Task<int> RunAsync(CancellationToken ct = default)
    {
        var added = 0;

        foreach (var feed in OpportunitySourceRegistry.All)
        {
            if (ct.IsCancellationRequested) break;

            try
            {
                added += await IngestFeedAsync(feed, ct);
            }
            catch (Exception ex)
            {
                // One dead feed must not stop the others. A source that breaks is a normal event.
                _log.LogWarning(ex, "Opportunity feed {Feed} failed", feed.Name);
            }
        }

        _log.LogInformation("Opportunity ingestion added {Added} new opportunities.", added);
        return added;
    }

    private async Task<int> IngestFeedAsync(OpportunitySourceRegistry.OpportunityFeed feed, CancellationToken ct)
    {
        var client = _httpFactory.CreateClient("Ingestion");
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(30));

        var xml = await client.GetStringAsync(feed.Url, cts.Token);
        var doc = XDocument.Parse(xml);

        var entries = doc.Descendants("item")
            .Concat(doc.Descendants(Atom + "entry"))
            .ToList();

        var added = 0;

        foreach (var entry in entries)
        {
            var title = Clean(Value(entry, "title") ?? Value(entry, Atom + "title"));
            var link = Value(entry, "link")
                ?? entry.Elements(Atom + "link").FirstOrDefault()?.Attribute("href")?.Value;

            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(link)) continue;

            // The link is the identity. Re-running the job must not duplicate anything, and these
            // feeds re-publish the same post across pages.
            var exists = await _db.Opportunities.Find(o => o.Url == link).AnyAsync(cts.Token);
            if (exists) continue;

            var rawBody = Value(entry, "description")
                ?? Value(entry, Atom + "summary")
                ?? Value(entry, "{http://purl.org/rss/1.0/modules/content/}encoded")
                ?? string.Empty;

            var body = Clean(rawBody);

            await _db.Opportunities.InsertOneAsync(new Opportunity
            {
                Type = InferType(title, body, feed.DefaultType),
                Title = Shorten(title, 160),
                Organisation = InferOrganisation(title, feed.Name),
                Description = Shorten(body, 600),
                Url = link,
                RegionTag = InferRegion(title, body, feed.RegionTag),
                GoalTag = feed.GoalTag,
                Deadline = FindDeadline($"{title} {body}"),
                IsRemote = feed.RegionTag == "Remote"
                    || body.Contains("remote", StringComparison.OrdinalIgnoreCase),
                Location = feed.RegionTag == "Remote" ? "Remote" : null,
                DiscoveredAt = DateTime.UtcNow,
                // Left empty on purpose. Eligibility, benefits and selection process are specific
                // claims; inventing them from a summary is how people end up applying for things
                // they do not qualify for. The detail page links to the original instead.
                Benefits = string.Empty,
                WhoIsEligible = [],
            }, cancellationToken: cts.Token);

            added++;
        }

        _log.LogInformation("Opportunity feed {Feed}: {Added} new of {Total} entries.", feed.Name, added, entries.Count);
        return added;
    }

    /// <summary>
    /// Reads the type from how the post describes itself. Falls back to the feed's default rather
    /// than guessing, because calling a job a "Scholarship" is worse than being vague.
    /// </summary>
    private static OpportunityType InferType(string title, string body, OpportunityType fallback)
    {
        var text = $"{title} {body}".ToLowerInvariant();

        if (text.Contains("scholarship") || text.Contains("tuition")) return OpportunityType.Scholarship;
        if (text.Contains("fellowship")) return OpportunityType.Fellowship;
        if (text.Contains("internship") || text.Contains("intern ")) return OpportunityType.Internship;
        if (text.Contains("grant") || text.Contains("funding")) return OpportunityType.ResearchGrant;
        if (text.Contains("accelerator") || text.Contains("incubator")) return OpportunityType.StartupAccelerator;
        if (text.Contains("competition") || text.Contains("challenge") || text.Contains("hackathon")) return OpportunityType.Competition;
        if (text.Contains("conference") || text.Contains("summit")) return OpportunityType.Conference;
        if (text.Contains("workshop") || text.Contains("bootcamp")) return OpportunityType.Workshop;
        if (text.Contains("training") || text.Contains("course")) return OpportunityType.Training;

        return fallback;
    }

    /// <summary>
    /// Job feeds title posts as "Company: Role" or "Role at Company". Anything else falls back to
    /// the publication, which is at least true.
    /// </summary>
    private static string InferOrganisation(string title, string feedName)
    {
        var colon = title.IndexOf(':');
        if (colon is > 1 and < 48) return title[..colon].Trim();

        var at = title.LastIndexOf(" at ", StringComparison.OrdinalIgnoreCase);
        if (at > 0 && title.Length - at < 60) return title[(at + 4)..].Trim();

        return feedName;
    }

    private static string InferRegion(string title, string body, string fallback)
    {
        var text = $"{title} {body}".ToLowerInvariant();
        if (text.Contains("nigeria")) return "Nigeria";
        if (text.Contains("ghana")) return "Ghana";
        if (text.Contains("kenya") || text.Contains("africa")) return "Africa";
        return fallback;
    }

    /// <summary>
    /// Only returns a date it actually found, and only a future one. A wrong deadline makes someone
    /// miss a real chance or panic over a fake one, so no date beats a guessed date.
    /// </summary>
    private static DateTime? FindDeadline(string text)
    {
        var match = DeadlinePhrase().Match(text);
        if (!match.Success) return null;

        var candidate = match.Groups[1].Value.Replace(",", " ").Trim();
        if (!DateTime.TryParse(candidate, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
                out var parsed))
            return null;

        return parsed > DateTime.UtcNow && parsed < DateTime.UtcNow.AddYears(3) ? parsed : null;
    }

    private static string? Value(XElement element, XName name) => element.Element(name)?.Value;

    private static string Clean(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : WebUtility.HtmlDecode(HtmlTags().Replace(value, " "))
                .Replace('\n', ' ').Replace('\r', ' ')
                .Replace("  ", " ")
                .Trim();

    private static string Shorten(string value, int max) =>
        value.Length <= max ? value : value[..max].TrimEnd() + "…";
}
