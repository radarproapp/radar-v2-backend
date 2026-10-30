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
    /// Deadline phrasing as it actually appears in these posts.
    /// </summary>
    /// <remarks>
    /// "Deadline: 15 December 2026", "Application Deadline: December 15, 2026",
    /// "Closes 15/12/2026" - and, on the Nigerian boards, overwhelmingly
    /// "Application Closing Date: 30th November, 2026".
    ///
    /// The ordinal suffix is what this missed. Both date shapes began with digits followed by
    /// whitespace, so "30th" stopped the match dead and almost every job on those feeds stored no
    /// deadline at all. A missing deadline reads as "never closes": no urgency on a posting that
    /// shuts in three days, and a row that never expires out of the scoring window.
    /// </remarks>
    [GeneratedRegex(@"(?:deadline|closes?|apply by|closing date)\s*[:\-]?\s*((?:\d{1,2}(?:st|nd|rd|th)?\s+)?[A-Z][a-z]+\s+\d{1,2}(?:st|nd|rd|th)?,?\s+\d{4}|\d{1,2}(?:st|nd|rd|th)?\s+[A-Z][a-z]+,?\s+\d{4}|\d{1,2}[/-]\d{1,2}[/-]\d{4})",
        RegexOptions.IgnoreCase)]
    private static partial Regex DeadlinePhrase();

    /// <summary>A numeric date, so its two leading components can be compared.</summary>
    [GeneratedRegex(@"^(\d{1,2})[/-](\d{1,2})[/-](\d{4})$")]
    private static partial Regex Numeric();

    /// <summary>The ordinal on a day, which no date parser accepts.</summary>
    [GeneratedRegex(@"(?<=\d)(?:st|nd|rd|th)", RegexOptions.IgnoreCase)]
    private static partial Regex Ordinal();

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

    /// <summary>
    /// How much of one feed is taken in a single run.
    /// </summary>
    /// <remarks>
    /// Enough that a source with a daily handful is fully read every time, and small enough that
    /// a source with hundreds cannot crowd every other source out of the scoring window.
    /// </remarks>
    public const int MostPerFeedPerRun = 40;

    /// <summary>
    /// When an entry says it was published, for ordering before the cap.
    /// </summary>
    /// <remarks>
    /// The cap used to take the first forty in document order and trust the feed to be newest
    /// first. For a feed that is not, that freezes the source on its oldest forty for ever: the
    /// next run takes the same forty, finds them all stored, and adds nothing - logged as "0 new
    /// of 40", which is also what a quiet day looks like.
    ///
    /// Undated entries sort last rather than first, so a feed with no dates degrades to document
    /// order rather than putting its unknowns ahead of real postings.
    /// </remarks>
    private static DateTime PublishedAt(XElement entry)
    {
        var raw = Value(entry, "pubDate")
                  ?? Value(entry, Atom + "published")
                  ?? Value(entry, Atom + "updated")
                  ?? Value(entry, "{http://purl.org/dc/elements/1.1/}date");

        return DateTime.TryParse(raw, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
            out var parsed)
            ? parsed
            : DateTime.MinValue;
    }

    private async Task<int> IngestFeedAsync(OpportunitySourceRegistry.OpportunityFeed feed, CancellationToken ct)
    {
        var client = _httpFactory.CreateClient("Ingestion");
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(30));

        var xml = await client.GetStringAsync(feed.Url, cts.Token);
        var doc = XDocument.Parse(xml);

        // Newest first, and only so many from any one source per run.
        //
        // Nothing capped this, and one of these feeds publishes close to six hundred entries. A
        // single run took the collection from a few dozen to several hundred, all from one
        // source - and the scoring window is five hundred documents, so that one feed could fill
        // it and keep everything ingested afterwards from ever being scored. Content ingestion
        // has had a per-source cap and a round robin for this reason; the opportunity path never
        // got one.
        //
        // The feeds are in reverse-chronological order, so taking from the front takes the most
        // recent postings. The rest arrive over the following runs.
        var entries = doc.Descendants("item")
            .Concat(doc.Descendants(Atom + "entry"))
            .OrderByDescending(PublishedAt)
            .Take(MostPerFeedPerRun)
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
    /// <remarks>Internal so the inference can be tested; it is the part that is easiest to get
    /// quietly wrong and the hardest to notice from a feed.</remarks>
    internal static OpportunityType InferType(string title, string body, OpportunityType fallback)
    {
        var heading = title.ToLowerInvariant();

        // The title, when it says outright what the posting is.
        //
        // The body used to decide, and on a job board the body is a list of requirements:
        // "a B.Sc Degree in any course of study" is in close to every Nigerian job advert, and
        // "course" matched the Training keyword. So a large share of a job feed was filed as
        // Training - a different thing, that a learner is looking for on a different day.
        //
        // A headline that says Job Recruitment is not ambiguous, and nothing in a requirements
        // list should overrule it.
        // What the headline says it is, when it says something more specific than "a job".
        //
        // The recruitment markers are an aggregator's house style and sit on everything,
        // including "Internship Recruitment (4 Positions)" and "Graduate Trainee Recruitment (5
        // Positions)". Short-circuiting on them alone filed both as ordinary jobs, which makes
        // them invisible to the filter a learner would actually use to find them.
        if (heading.Contains("internship") || heading.Contains("industrial attachment"))
            return OpportunityType.Internship;

        if (heading.Contains("graduate trainee")
            || heading.Contains("graduate programme")
            || heading.Contains("graduate program")
            || heading.Contains("management trainee")
            || heading.Contains("trainee programme"))
        {
            return OpportunityType.GraduateProgramme;
        }

        if (heading.Contains("scholarship")) return OpportunityType.Scholarship;
        if (heading.Contains("fellowship")) return OpportunityType.Fellowship;

        if (heading.Contains("job recruitment")
            || heading.Contains("job vacanc")
            || heading.Contains("vacancies")
            || heading.Contains("recruitment ("))
        {
            return OpportunityType.Job;
        }

        var text = $"{title} {body}".ToLowerInvariant();

        if (text.Contains("scholarship") || text.Contains("tuition")) return OpportunityType.Scholarship;
        if (text.Contains("fellowship")) return OpportunityType.Fellowship;
        if (text.Contains("internship") || text.Contains("intern ")) return OpportunityType.Internship;
        if (text.Contains("grant") || text.Contains("funding")) return OpportunityType.ResearchGrant;
        if (text.Contains("accelerator") || text.Contains("incubator")) return OpportunityType.StartupAccelerator;
        if (text.Contains("competition") || text.Contains("challenge") || text.Contains("hackathon")) return OpportunityType.Competition;
        if (text.Contains("conference") || text.Contains("summit")) return OpportunityType.Conference;
        if (text.Contains("workshop") || text.Contains("bootcamp")) return OpportunityType.Workshop;
        // Last, and only when the posting is about the training rather than mentioning it. "On
        // the job training is provided" and "any course of study" are lines in a job advert, not
        // a description of a training programme.
        if (heading.Contains("training") || heading.Contains("course")) return OpportunityType.Training;

        return fallback;
    }

    /// <summary>
    /// Who is actually offering this.
    /// </summary>
    /// <remarks>
    /// Job feeds title posts as "Company: Role", "Role at Company", or - on the aggregators -
    /// "Company Job Recruitment (3 Positions)". That last shape has no colon and no " at ", so
    /// it fell through to the feed's own name: every job carried "Hot Nigerian Jobs" as the
    /// organisation while the employer sat in plain sight at the front of the title. It is the
    /// line a learner reads first when deciding whether to apply.
    ///
    /// The feed name is still the last resort, because naming the board is at least true.
    /// </remarks>
    internal static string InferOrganisation(string title, string feedName)
    {
        var colon = title.IndexOf(':');
        if (colon is > 1 and < 48) return title[..colon].Trim();

        var at = title.LastIndexOf(" at ", StringComparison.OrdinalIgnoreCase);
        if (at > 0 && title.Length - at < 60) return title[(at + 4)..].Trim();

        // "<Employer> Job Recruitment (3 Positions)" and its variants. Everything before the
        // phrase that describes the advert is the employer.
        // The earliest marker in the title, not the first one in this list.
        //
        // Returning on list order meant "Sahara Group Vacancies - Graduate Job Recruitment" cut
        // at " job recruitment" because that marker is checked first, giving "Sahara Group
        // Vacancies - Graduate" as the employer. The employer is whatever comes before the
        // earliest of them.
        var earliest = Advertised
            .Select(marker => title.IndexOf(marker, StringComparison.OrdinalIgnoreCase))
            .Where(found => found > 1)
            .DefaultIfEmpty(-1)
            .Min();

        if (earliest > 1) return title[..earliest].Trim().TrimEnd(',', '-', '–');

        return feedName;
    }

    /// <summary>The phrases an aggregator appends after the employer's name.</summary>
    private static readonly string[] Advertised =
    [
        " job recruitment", " job vacanc", " vacancies", " recruitment (", " recruits ", " is recruiting",
    ];

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
    internal static DateTime? FindDeadline(string text)
    {
        var match = DeadlinePhrase().Match(text);
        if (!match.Success) return null;

        var candidate = Ordinal().Replace(match.Groups[1].Value, string.Empty)
            .Replace(",", " ")
            .Trim();

        // A numeric date is read the way it can only have been meant.
        //
        // InvariantCulture read 03/12/2026 as the twelfth of March - American order, nine months
        // early in a market that writes the day first, usually already past, and then dropped by
        // the sanity check below without a word. Switching the whole thing to day-first fixed
        // that and broke the other direction: 12/25/2026 became month 25 and parsed as nothing
        // at all, so a US-written posting lost a deadline it previously had right.
        //
        // Most numeric dates decide themselves. A first component above twelve can only be a
        // day; a second above twelve can only be a month. Only 1-12 against 1-12 is genuinely
        // ambiguous, and there the market Radar serves writes the day first.
        //
        // The word shapes - "30 November 2026", "November 30 2026" - parse the same under either
        // culture, so none of this touches them.
        var invariant = System.Globalization.CultureInfo.InvariantCulture;
        var dayFirst = System.Globalization.CultureInfo.GetCultureInfo("en-GB");
        var styles = System.Globalization.DateTimeStyles.AssumeUniversal
                     | System.Globalization.DateTimeStyles.AdjustToUniversal;

        var order = Numeric().Match(candidate) is { Success: true } numeric
                    && int.Parse(numeric.Groups[2].Value) > 12
            ? invariant
            : dayFirst;

        if (!DateTime.TryParse(candidate, order, styles, out var parsed)
            && !DateTime.TryParse(candidate, order == dayFirst ? invariant : dayFirst, styles, out parsed))
        {
            return null;
        }

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
