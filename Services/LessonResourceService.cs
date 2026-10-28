using Microsoft.Extensions.Caching.Memory;
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
    private readonly LessonResourceJudge _judge;
    private readonly IMemoryCache _cache;
    private readonly ILogger<LessonResourceService> _log;

    public LessonResourceService(
        RadarDatabase db,
        YouTubeSearchService youtube,
        IAiEngine ai,
        LessonResourceJudge judge,
        IMemoryCache cache,
        ILogger<LessonResourceService> log)
    {
        _db = db;
        _youtube = youtube;
        _ai = ai;
        _judge = judge;
        _cache = cache;
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
            .Where(l => l.ResourcesJudgedAt is null && !string.IsNullOrWhiteSpace(l.Title))
            .ToList();

        if (lessons.Count == 0) return;

        var subject = !string.IsNullOrWhiteSpace(roadmap.Interest) ? roadmap.Interest : roadmap.Goal;
        var goal = !string.IsNullOrWhiteSpace(roadmap.Goal) ? roadmap.Goal : roadmap.Interest;

        var corpusSize = await CorpusSizeAsync(ct);
        var gathered = new List<Pending>();

        foreach (var lesson in lessons)
        {
            if (ct.IsCancellationRequested) break;

            var videos = await VideoCandidatesAsync(lesson, ct);
            var articles = await ArticleCandidatesAsync(lesson, corpusSize, ct);

            gathered.Add(new Pending(lesson, videos, articles));
        }

        await AdmitAllAsync(gathered, goal, subject, ct);
    }

    /// <summary>What one lesson has to offer, before anything has been allowed through.</summary>
    private sealed record Pending(
        RoadmapLesson Lesson, List<LessonResource> Videos, List<LessonResource> Articles);

    /// <summary>
    /// Puts every lesson's candidates to the judge, and keeps only what it admits.
    /// </summary>
    /// <remarks>
    /// One call per lesson covering its videos and its articles together, run in parallel behind a
    /// single deadline - the same shape the summaries use, and for the same reason: these run
    /// inside the request somebody is waiting on after pressing "Build this section", and six
    /// sequential model calls is how that became a minute.
    ///
    /// Judged before being summarised, so nothing is paid for twice: a dropped video never has a
    /// summary written for it.
    /// </remarks>
    private async Task AdmitAllAsync(
        List<Pending> gathered, string goal, string subject, CancellationToken ct)
    {
        if (gathered.Count == 0) return;

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(JudgementSeconds));

        try
        {
            await Task.WhenAll(gathered.Select(p => AdmitAsync(p, goal, deadline.Token)));
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Some lessons could not have their resources checked.");
        }

        // Anything the deadline caught keeps no resources and says why. Shipping unjudged
        // candidates because the clock ran out would be the original bug with extra steps.
        foreach (var pending in gathered)
        {
            if (pending.Lesson.Resources.Count == 0
                && string.IsNullOrWhiteSpace(pending.Lesson.ResourceNote))
            {
                pending.Lesson.ResourceNote = CouldNotCheck;
            }
        }

        var toSummarise = gathered
            .Where(p => p.Lesson.Resources.Any(r => r.Kind == LessonResourceKind.Video))
            .Take(MaxLessonsSummarisedPerPass)
            .Select(p => (p.Lesson,
                p.Lesson.Resources.Where(r => r.Kind == LessonResourceKind.Video).ToList()))
            .ToList();

        await SummariseAllAsync(toSummarise, subject, ct);
    }

    /// <summary>How long every lesson on a path gets, together, to be checked.</summary>
    private const int JudgementSeconds = 25;

    /// <summary>Radar looked, and the corpus has nothing that teaches this lesson.</summary>
    public const string NothingRelevant =
        "Nothing in Radar's library teaches this lesson yet. Rather than fill the space with "
        + "items that only share a word with the title, it is left empty.";

    /// <summary>Radar could not look. A fault, and it has to read like one.</summary>
    public const string CouldNotCheck =
        "Radar could not check these against the lesson just now, so none are shown. "
        + "They will appear the next time this path is opened.";

    private async Task AdmitAsync(Pending pending, string goal, CancellationToken ct)
    {
        var lesson = pending.Lesson;

        var candidates = pending.Videos
            .Concat(pending.Articles)
            .Select(r => new LessonResourceJudge.Candidate(r.Id, r.Title, r.Source, r.Summary))
            .ToList();

        if (candidates.Count == 0)
        {
            lesson.Resources = [];
            lesson.ResourceNote = NothingRelevant;
            lesson.ResourcesJudgedAt = DateTime.UtcNow;
            return;
        }

        var verdicts = await _judge.JudgeAsync(lesson.Title, lesson.Body, goal, candidates, ct);

        if (verdicts is null)
        {
            // Not an answer. The lesson shows nothing and says the check did not happen, which is
            // a different sentence from the corpus having nothing.
            lesson.Resources = [];
            lesson.ResourceNote = CouldNotCheck;
            return;
        }

        var reasons = verdicts.ToDictionary(v => v.Id, v => v.Reason, StringComparer.Ordinal);

        var videos = Admitted(pending.Videos, reasons).Take(VideosPerLesson).ToList();
        var articles = Admitted(pending.Articles, reasons).Take(ArticlesPerLesson).ToList();

        lesson.Resources = [.. videos, .. articles];
        lesson.ResourceNote = lesson.Resources.Count == 0 ? NothingRelevant : string.Empty;

        // An answer, so it is not asked again. Whatever the old matcher left on this lesson has
        // now been replaced by what the judge admitted, which may well be nothing.
        lesson.ResourcesJudgedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// The resources the judge kept, in the order they were ranked rather than the order it
    /// happened to answer in.
    /// </summary>
    private static IEnumerable<LessonResource> Admitted(
        List<LessonResource> candidates, Dictionary<string, string> reasons) =>
        candidates
            .Where(r => reasons.ContainsKey(r.Id))
            .Select(r =>
            {
                r.WhyRelevant = reasons[r.Id];
                return r;
            });

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

    /// <summary>
    /// How many of each kind go forward to be judged.
    /// </summary>
    /// <remarks>
    /// More than are shown, because the judge drops things and a lesson with five candidates for
    /// five slots can only pass them all or show a gap. Bounded because every candidate is text in
    /// a prompt somebody is waiting on.
    /// </remarks>
    private const int CandidatesPerKind = 8;

    /// <summary>
    /// Videos from the YouTube cache, deduplicated.
    /// </summary>
    /// <remarks>
    /// Still cache-only. The warm is the caller's job; this returns what exists, and a cold cache
    /// shows as a lesson without videos rather than a wait on somebody else's API.
    ///
    /// These carry no summary yet — summaries are written after the judge has chosen, so none is
    /// paid for on a video that gets dropped. The judge therefore sees a video by its title and
    /// channel, which is what a learner scrolling a results page sees too.
    /// </remarks>
    private async Task<List<LessonResource>> VideoCandidatesAsync(
        RoadmapLesson lesson, CancellationToken ct)
    {
        if (!_youtube.IsConfigured) return [];

        // The hint exists because a readable lesson title makes a poor query: "What hospital nurse
        // manager actually involves day to day" finds nothing.
        var query = !string.IsNullOrWhiteSpace(lesson.SearchHint) ? lesson.SearchHint! : lesson.Title;

        var cached = await _youtube.GetCachedAsync(query, ct: ct);
        if (cached.Count == 0) return [];

        var videos = cached.Select(video => new LessonResource
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
        });

        return LessonRelevance.Distinct(videos, v => v.Url, v => v.Title)
            .Take(CandidatesPerKind)
            .ToList();
    }

    /// <summary>
    /// Articles from Radar's own corpus that could plausibly belong to this lesson.
    /// </summary>
    /// <remarks>
    /// What this used to do, and why each part of it is gone:
    ///
    /// It took the first three words of the title over four characters that were not on a filler
    /// list, matched any one of them as a bare substring against article titles, and sorted what
    /// came back by source credibility and then by date. "Understanding Numbers" reduced to
    /// "Numbers", and the most credible recent article containing that word was an IPO report.
    ///
    /// Now: terms anchor to word starts, a match needs either a term the corpus says is rare or
    /// two terms together, and the ranking leads with relevance as section 20 requires.
    ///
    /// The old fallback is gone entirely. When the lesson's own terms found nothing it widened to
    /// any article whose topic matched the subject, newest first — which is not a search for
    /// anything. It is the corpus in publication order under a heading claiming to be a reading
    /// list. An empty lesson is better, and now says so.
    /// </remarks>
    private async Task<List<LessonResource>> ArticleCandidatesAsync(
        RoadmapLesson lesson, long corpusSize, CancellationToken ct)
    {
        var terms = LessonRelevance.TermsOf(lesson.Title, lesson.SearchHint);
        if (terms.Count == 0) return [];

        var distinctive = await DistinctiveTermsAsync(terms, corpusSize, ct);

        var readable = Builders<ContentItem>.Filter.And(
            Builders<ContentItem>.Filter.In(c => c.Type, new[] { ContentType.Article, ContentType.Essay }),
            Builders<ContentItem>.Filter.Ne(c => c.Url, null),
            Builders<ContentItem>.Filter.Ne(c => c.Url, string.Empty));

        // Wide on purpose: the database finds anything that mentions a term, and the rules below
        // decide which of those is evidence. Doing it the other way — a query strict enough to
        // need no filtering — would mean expressing the rules as Mongo filters, where they can be
        // neither read nor tested.
        var mentionsATerm = Builders<ContentItem>.Filter.Or(
            terms.Select(term => Builders<ContentItem>.Filter.Regex(
                c => c.Title,
                new MongoDB.Bson.BsonRegularExpression(LessonRelevance.WordStartPattern(term), "i"))));

        var found = await _db.ContentItems
            .Find(readable & mentionsATerm)
            .Limit(CandidateScanLimit)
            .ToListAsync(ct);

        var worth = found.Where(item => LessonRelevance.WorthJudging(
            item.Title + " " + FirstNonEmpty(item.WhatHappened, item.AiSummary), terms, distinctive));

        var ranked = LessonRelevance.Rank(
            worth,
            item => item.Title,
            item => item.CredibilityTier,
            item => item.PublishedAt,
            terms,
            distinctive);

        return LessonRelevance.Distinct(ranked, i => i.Url, i => i.Title)
            .Take(CandidatesPerKind)
            .Select(item => new LessonResource
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
            })
            .ToList();
    }

    /// <summary>
    /// How many matching articles are pulled back before the rules are applied.
    /// </summary>
    /// <remarks>
    /// A ceiling on one query, not a quality setting. A term like "numbers" can match a large part
    /// of a general corpus, and the ranking happens here rather than in the database because
    /// relevance is not something a Mongo sort can express.
    /// </remarks>
    private const int CandidateScanLimit = 200;

    /// <summary>
    /// Which of these terms are rare enough in the corpus to mean something on their own.
    /// </summary>
    /// <remarks>
    /// One count per term, cached for a day. The corpus grows slowly and the same handful of
    /// lesson titles are asked about repeatedly, so this costs one count per new term per day
    /// rather than one per lesson per build.
    ///
    /// This is what replaced the hand-written list of weak words, which by its own admission could
    /// never be complete. A word is weak here because the corpus says so.
    /// </remarks>
    private async Task<List<string>> DistinctiveTermsAsync(
        List<string> terms, long corpusSize, CancellationToken ct)
    {
        if (corpusSize <= 0) return [];

        var distinctive = new List<string>();

        foreach (var term in terms)
        {
            var key = "lesson-term-frequency:" + term.ToLowerInvariant();

            if (!_cache.TryGetValue(key, out long matches))
            {
                matches = await _db.ContentItems.CountDocumentsAsync(
                    Builders<ContentItem>.Filter.Regex(
                        c => c.Title,
                        new MongoDB.Bson.BsonRegularExpression(
                            LessonRelevance.WordStartPattern(term), "i")),
                    cancellationToken: ct);

                _cache.Set(key, matches, TimeSpan.FromDays(1));
            }

            if (LessonRelevance.IsDistinctive(matches, corpusSize)) distinctive.Add(term);
        }

        return distinctive;
    }

    /// <summary>How much there is to be rare within. Cached for the same reason.</summary>
    private async Task<long> CorpusSizeAsync(CancellationToken ct)
    {
        if (_cache.TryGetValue("lesson-corpus-size", out long size)) return size;

        size = await _db.ContentItems.EstimatedDocumentCountAsync(cancellationToken: ct);
        _cache.Set("lesson-corpus-size", size, TimeSpan.FromDays(1));

        return size;
    }

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

    private static string FirstNonEmpty(params string?[] candidates) =>
        candidates.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c))?.Trim() ?? string.Empty;
}
