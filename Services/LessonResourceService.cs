using MongoDB.Driver;
using RadarV2.Data;
using RadarV2.Models;
using RadarV2.Services.Interfaces;

namespace RadarV2.Services;

/// <summary>
/// Fills each lesson with five videos and two articles, summarised.
/// </summary>
/// <remarks>
/// Resources used to attach to the module, and when none matched the module's level the code
/// handed it every resource on the path — so a lesson on ARP spoofing could show the same list as
/// a lesson on VLAN hopping. One video per lesson was resolved and the rest of the search results
/// were discarded.
///
/// Two different sources, for a reason:
///
/// Videos come from the YouTube cache, which already holds several results per query — the old
/// code took <c>[0]</c> and dropped the rest. A search costs 100 quota units whatever its size, so
/// asking for five instead of three costs nothing extra.
///
/// Articles come from Radar's own ingested corpus rather than a web search. They are already
/// enriched, so they arrive with a summary somebody's pipeline already paid for, they are from
/// sources the registry vouches for, and it adds no new external dependency. An article Radar has
/// not ingested is one it cannot say anything about.
/// </remarks>
public class LessonResourceService
{
    private readonly RadarDatabase _db;
    private readonly YouTubeSearchService _youtube;
    private readonly IAiEngine _ai;
    private readonly ILogger<LessonResourceService> _log;

    public LessonResourceService(
        RadarDatabase db,
        YouTubeSearchService youtube,
        IAiEngine ai,
        ILogger<LessonResourceService> log)
    {
        _db = db;
        _youtube = youtube;
        _ai = ai;
        _log = log;
    }

    private const int VideosPerLesson = 5;
    private const int ArticlesPerLesson = 2;

    /// <summary>
    /// How many lessons get summaries in one pass.
    /// </summary>
    /// <remarks>
    /// One model call per lesson, each summarising that lesson's five videos together rather than
    /// one call per video. A twenty-lesson path would otherwise be a hundred calls on a single
    /// page load.
    /// </remarks>
    private const int MaxLessonsSummarisedPerPass = 6;

    private sealed class SummariesResponse
    {
        public List<VideoSummary> Summaries { get; set; } = [];
    }

    private sealed class VideoSummary
    {
        /// <summary>Echoed back so a summary cannot be attached to the wrong video.</summary>
        public string? VideoId { get; set; }
        public string? Summary { get; set; }
    }

    /// <summary>
    /// Attaches resources to every lesson that has none yet.
    /// </summary>
    /// <remarks>
    /// Skips lessons already filled, so this is cheap on every visit after the first and does not
    /// re-summarise what it summarised yesterday.
    /// </remarks>
    public async Task FillAsync(GrowthRoadmap roadmap, CancellationToken ct = default)
    {
        var lessons = roadmap.Modules
            .SelectMany(m => m.Lessons ?? [])
            .Where(l => l.Resources.Count == 0 && !string.IsNullOrWhiteSpace(l.Title))
            .ToList();

        if (lessons.Count == 0) return;

        var subject = !string.IsNullOrWhiteSpace(roadmap.Interest) ? roadmap.Interest : roadmap.Goal;
        var summarised = 0;
        var pending = new List<(RoadmapLesson Lesson, List<LessonResource> Videos)>();

        foreach (var lesson in lessons)
        {
            if (ct.IsCancellationRequested) break;

            var videos = await VideosForAsync(lesson, ct);
            var articles = await ArticlesForAsync(lesson, subject, ct);

            lesson.Resources = [.. videos, .. articles];

            if (videos.Count > 0 && summarised < MaxLessonsSummarisedPerPass)
            {
                pending.Add((lesson, videos));
                summarised++;
            }
        }

        await SummariseAllAsync(pending, subject, ct);
    }

    /// <summary>
    /// Summarises every lesson's videos at once, with a deadline.
    /// </summary>
    /// <remarks>
    /// These were written one lesson at a time, inside the request that built the section — six
    /// model calls in sequence, each allowed up to its own budget, on the request a learner is
    /// waiting on after pressing "Build this section". Run together and bounded, so a slow
    /// provider costs the page a few seconds rather than a minute, and the lessons keep their
    /// videos either way.
    /// </remarks>
    private async Task SummariseAllAsync(
        List<(RoadmapLesson Lesson, List<LessonResource> Videos)> pending,
        string subject,
        CancellationToken ct)
    {
        if (pending.Count == 0) return;

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(25));

        try
        {
            await Task.WhenAll(pending.Select(p =>
                SummariseVideosAsync(p.Lesson, p.Videos, subject, deadline.Token)));
        }
        catch (Exception ex)
        {
            // Every lesson keeps its videos. A summary that did not arrive is a blank line, and
            // the next visit fills it.
            _log.LogWarning(ex, "Some lesson video summaries did not complete.");
        }
    }

    private async Task<List<LessonResource>> VideosForAsync(RoadmapLesson lesson, CancellationToken ct)
    {
        if (!_youtube.IsConfigured) return [];

        // The hint exists because a readable lesson title makes a poor query: "What hospital nurse
        // manager actually involves day to day" finds nothing.
        var query = !string.IsNullOrWhiteSpace(lesson.SearchHint) ? lesson.SearchHint! : lesson.Title;

        var cached = await _youtube.GetCachedAsync(query, ct: ct);
        if (cached.Count == 0)
        {
            // Nothing invented in the meantime. The warm is the caller's job — this returns what
            // exists, and an empty list shows as a lesson without videos yet.
            return [];
        }

        return cached.Take(VideosPerLesson).Select(video => new LessonResource
        {
            Kind = LessonResourceKind.Video,
            Title = video.Title,
            Url = video.Url,
            EmbedUrl = video.EmbedUrl,
            ThumbnailUrl = video.ThumbnailUrl,
            Source = video.ChannelTitle,
            PublishedAt = video.PublishedAt,
            // Set when a summary is written. There are no transcripts — the transcription keys are
            // not configured — so this is as good as it gets today, and it says so.
            SummarySource = SummarySource.Description,
        }).ToList();
    }

    /// <summary>
    /// Two articles from Radar's own corpus, matched on the lesson and its subject.
    /// </summary>
    /// <remarks>
    /// Text search on the title first, falling back to the subject's topic, because a lesson title
    /// is specific enough to miss entirely. Credibility tier decides ties: if two articles both
    /// match, the one from a source the registry rates higher wins.
    /// </remarks>
    private async Task<List<LessonResource>> ArticlesForAsync(
        RoadmapLesson lesson, string subject, CancellationToken ct)
    {
        var readable = Builders<ContentItem>.Filter.And(
            Builders<ContentItem>.Filter.In(c => c.Type, new[] { ContentType.Article, ContentType.Essay }),
            Builders<ContentItem>.Filter.Ne(c => c.Url, null),
            Builders<ContentItem>.Filter.Ne(c => c.Url, string.Empty));

        var terms = Keywords(lesson.Title).ToList();
        var items = new List<ContentItem>();

        if (terms.Count > 0)
        {
            var matchesLesson = Builders<ContentItem>.Filter.Or(
                terms.Select(term => Builders<ContentItem>.Filter.Regex(
                    c => c.Title, new MongoDB.Bson.BsonRegularExpression(System.Text.RegularExpressions.Regex.Escape(term), "i"))));

            // Sorted on the field, not on a comparison of it.
            //
            // This was SortByDescending(c => c.CredibilityTier == 1). The driver's sort builder
            // needs a field to render a sort document from, and a boolean expression is not one,
            // so it threw at query-build time — on every call. Which meant every "Build this
            // section" returned a 500, because AddSectionAsync enriches resources on the way out.
            //
            // Tier 1 is the best tier, so ascending puts the most credible first.
            items = await _db.ContentItems
                .Find(readable & matchesLesson)
                .SortBy(c => c.CredibilityTier)
                .ThenByDescending(c => c.PublishedAt)
                .Limit(ArticlesPerLesson)
                .ToListAsync(ct);
        }

        // Nothing matched the lesson itself, so widen to the subject rather than show nothing.
        if (items.Count < ArticlesPerLesson && !string.IsNullOrWhiteSpace(subject))
        {
            var seen = items.Select(i => i.Id).ToHashSet(StringComparer.Ordinal);

            var matchesSubject = Builders<ContentItem>.Filter.Regex(
                c => c.Topic, new MongoDB.Bson.BsonRegularExpression(System.Text.RegularExpressions.Regex.Escape(subject), "i"));

            var extra = await _db.ContentItems
                .Find(readable & matchesSubject)
                .SortByDescending(c => c.PublishedAt)
                .Limit(ArticlesPerLesson * 2)
                .ToListAsync(ct);

            items.AddRange(extra.Where(e => seen.Add(e.Id)).Take(ArticlesPerLesson - items.Count));
        }

        return items.Select(item => new LessonResource
        {
            Kind = LessonResourceKind.Article,
            ContentItemId = item.Id,
            Title = item.Title,
            Url = item.Url ?? string.Empty,
            ThumbnailUrl = item.ThumbnailUrl,
            Source = item.Source ?? string.Empty,
            Duration = item.EstimatedReadTime,
            PublishedAt = item.PublishedAt,

            // Already written, when the item was enriched. Re-summarising something that has a
            // summary is paying twice for the same sentence.
            Summary = FirstNonEmpty(item.WhatHappened, item.AiSummary, item.WhyItMatters),
            SummarySource = SummarySource.Publisher,
        }).ToList();
    }

    /// <summary>
    /// One call per lesson, summarising its videos together.
    /// </summary>
    /// <remarks>
    /// Each summary is matched back by video id rather than by position, because a model that
    /// returns four summaries for five videos would otherwise shift them all up by one and
    /// describe the wrong video — which is worse than no summary, and undetectable on screen.
    /// </remarks>
    private async Task SummariseVideosAsync(
        RoadmapLesson lesson, List<LessonResource> videos, string subject, CancellationToken ct)
    {
        var listed = string.Join("\n\n", videos.Select((v, i) =>
            $"""
            [{i + 1}] videoId: {VideoIdOf(v)}
            title: {v.Title}
            channel: {v.Source}
            """));

        var prompt = $$"""
            A learner is working through the lesson "{{lesson.Title}}" as part of learning
            {{subject}}. Below are videos found for it.

            For each one, write two or three sentences on what it covers and what the learner
            would get from it for this lesson specifically.

            You are working from the title and channel only — you have not watched them. So:
            - Say what the title indicates it covers. Do not assert details you cannot know.
            - If a title is too vague to tell, return an empty summary for it rather than guessing.
            - Do not rank them, do not say "this is the best one", and do not pad.

            Videos:
            {{listed}}

            Return JSON, echoing each videoId so summaries cannot be mismatched:
            {"summaries": [{"videoId": "abc123", "summary": "Covers ... Useful here because ..."}]}
            """;

        try
        {
            var response = await _ai.GenerateJsonAsync<SummariesResponse>(
                "lesson-video-summaries",
                "You summarise what a video covers from its title and channel. You never claim to have watched it.",
                prompt,
                ct);

            if (response is null) return;

            foreach (var video in videos)
            {
                var id = VideoIdOf(video);
                var match = response.Summaries
                    .FirstOrDefault(s => string.Equals(s.VideoId, id, StringComparison.OrdinalIgnoreCase));

                if (!string.IsNullOrWhiteSpace(match?.Summary))
                    video.Summary = match!.Summary!.Trim();
            }
        }
        catch (Exception ex)
        {
            // The lesson keeps its videos. A failed summary is a blank line, not a missing list.
            _log.LogWarning(ex, "Could not summarise videos for lesson {Lesson}", lesson.Title);
        }
    }

    /// <summary>The YouTube id, taken off the embed url the ref built.</summary>
    private static string VideoIdOf(LessonResource video) =>
        video.EmbedUrl?.Split('/').LastOrDefault() ?? video.Url.Split("v=").LastOrDefault() ?? video.Id;

    /// <summary>
    /// Words from a lesson title worth searching on.
    /// </summary>
    /// <remarks>
    /// Short words and the filler that makes a title readable are dropped: matching an article on
    /// "the" or "understand" returns the corpus in publication order under a heading that says
    /// these are the readings for this lesson.
    ///
    /// The list is not exhaustive and cannot be — "need" was only in it after a test caught a
    /// title that reduced to nothing but that word. The subject fallback is the backstop: when
    /// the lesson's own terms find nothing, the query widens to the subject rather than accepting
    /// whatever a weak term matched.
    /// </remarks>
    private static IEnumerable<string> Keywords(string title)
    {
        string[] filler =
        [
            "understand", "understanding", "introduction", "intro", "basics", "overview",
            "getting", "started", "learn", "learning", "what", "why", "how", "when", "where",
            "your", "yours", "with", "from", "that", "this", "the", "and", "for", "you",
            "actually", "really", "detect", "detecting", "recognise", "recognize",
            "need", "needs", "essential", "essentials", "important", "guide", "about",
            "into", "using", "make", "making", "work", "works", "know", "knowing",
        ];

        return title
            .Split([' ', ',', ':', '-', '—', '(', ')', '/', '.'], StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.Trim())
            .Where(w => w.Length > 3 && !filler.Contains(w.ToLowerInvariant()))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(3);
    }

    private static string FirstNonEmpty(params string?[] candidates) =>
        candidates.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c))?.Trim() ?? string.Empty;
}
