using Microsoft.Extensions.Caching.Memory;
using MongoDB.Driver;
using RadarV2.Data;
using RadarV2.Models;
using RadarV2.Services.Interfaces;

using RadarV2.Services.Ingestion;

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
    private readonly PeerTubeClient _peerTube;
    private readonly IAiEngine _ai;
    private readonly LessonResourceJudge _judge;
    private readonly IMemoryCache _cache;
    private readonly ILogger<LessonResourceService> _log;

    public LessonResourceService(
        RadarDatabase db,
        YouTubeSearchService youtube,
        PeerTubeClient peerTube,
        IAiEngine ai,
        LessonResourceJudge judge,
        IMemoryCache cache,
        ILogger<LessonResourceService> log)
    {
        _db = db;
        _youtube = youtube;
        _peerTube = peerTube;
        _ai = ai;
        _judge = judge;
        _cache = cache;
        _log = log;
    }

    private const int VideosPerLesson = 5;
    private const int ArticlesPerLesson = 2;

    /// <summary>
    /// Attaches resources to every lesson that has none yet.
    /// </summary>
    /// <remarks>
    /// Skips lessons already filled, so this is cheap on every visit after the first and does not
    /// re-summarise what it summarised yesterday.
    /// </remarks>
    public async Task FillAsync(GrowthRoadmap roadmap, CancellationToken ct = default)
    {
        var all = roadmap.Modules
            .SelectMany(m => m.Lessons ?? [])
            .Where(l => !string.IsNullOrWhiteSpace(l.Title))
            .ToList();

        // Lessons that settled with no videos, before anything had looked for any.
        //
        // The wait count used to advance whether or not a lesson's own query had been searched,
        // and the warm reaches a few lessons per read - so one near the end of a long path could
        // spend all its waits queueing and then settle as "nothing found". Seven lessons on
        // production did. A settled lesson is never refilled and the warm skips it, so they would
        // have stayed empty for the life of the roadmap.
        //
        // Fixing the rule does not reach the rows the old rule already wrote, which has been the
        // shape of most of this week. They are picked back up here, and only them: a lesson that
        // was genuinely searched for and found nothing stays settled.
        foreach (var lesson in all.Where(l => l.ResourcesJudgedAt is not null
                                              && !l.Resources.Any(r => r.Kind == LessonResourceKind.Video)))
        {
            if (await VideosSearchedForAsync(lesson, ct)) continue;

            lesson.ResourcesJudgedAt = null;
            lesson.ResourceAttempts = 0;
        }

        var lessons = all.Where(l => l.ResourcesJudgedAt is null).ToList();


        if (lessons.Count == 0) return;

        var subject = !string.IsNullOrWhiteSpace(roadmap.Interest) ? roadmap.Interest : roadmap.Goal;
        var goal = !string.IsNullOrWhiteSpace(roadmap.Goal) ? roadmap.Goal : roadmap.Interest;

        var corpusSize = await CorpusSizeAsync(ct);

        // Every term on the path, counted once.
        //
        // This was done per lesson, so five lessons on one subject counted the same words five
        // times — and each count is a full collection scan, because a case-insensitive regex
        // cannot use an index even where one exists. Lessons in a section share most of their
        // vocabulary, which is what made the duplication expensive and what makes removing it
        // cheap.
        var terms = lessons
            .SelectMany(l => LessonRelevance.TermsOf(l.Title, l.SearchHint))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var distinctive = await DistinctiveTermsAsync(terms, corpusSize, ct);

        // And the per-lesson reads together rather than one after another. They are independent
        // queries against the same collection, so running them in sequence meant the build waited
        // for the sum of them while a learner watched a spinner.
        // What the rest of the path is about. A lesson title can belong to more than one field;
        // the lessons around it say which one the learner means.
        var allTitles = roadmap.Modules
            .SelectMany(m => m.Lessons ?? [])
            .Select(l => l.Title)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .ToList();

        var gathered = (await Task.WhenAll(lessons.Select(async lesson =>
        {
            var videos = await VideoCandidatesAsync(lesson, ct);
            var articles = await ArticleCandidatesAsync(lesson, distinctive, ct);

            var siblings = allTitles
                .Where(t => !string.Equals(t, lesson.Title, StringComparison.OrdinalIgnoreCase))
                .ToList();

            // Whether anything has ever looked for this lesson's videos, which is not the same
            // question as whether it has any.
            var searched = videos.Count > 0 || await VideosSearchedForAsync(lesson, ct);

            return new Pending(lesson, videos, articles, siblings, searched);
        }))).ToList();

        await AdmitAllAsync(gathered, goal, ct);
    }

    /// <summary>What one lesson has to offer, before anything has been allowed through.</summary>
    private sealed record Pending(
        RoadmapLesson Lesson,
        List<LessonResource> Videos,
        List<LessonResource> Articles,
        IReadOnlyList<string> SiblingTitles,
        bool VideosSearched);

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
    private async Task AdmitAllAsync(List<Pending> gathered, string goal, CancellationToken ct)
    {
        if (gathered.Count == 0) return;

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(JudgementSeconds));

        // One call per lesson decides what belongs and writes what a learner reads about it.
        //
        // Judging and summarising used to be separate work with a budget between them, so only
        // the first few lessons on a path got summaries at all and the rest showed a title and a
        // source. But the same call already has the lesson, the goal and every candidate in front
        // of it - asking it to explain what it just chose costs one more field per kept item, not
        // another pass. The cap is gone, and every admitted resource arrives explained.
        try
        {
            await Task.WhenAll(gathered.Select(pending => AdmitAsync(pending, goal, deadline.Token)));
        }
        catch (Exception ex)
        {
            // Every lesson keeps whatever it reached. A line that did not arrive is a blank line,
            // and the next visit fills it.
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

    }

    /// <summary>
    /// How long every lesson on a path gets, together, to be checked and summarised.
    /// </summary>
    /// <remarks>
    /// One budget, not two. Judging and summarising were separate phases with 25 seconds each, so
    /// the worst case was a learner waiting out both — and a measured build of a five-lesson
    /// section took the better part of a minute. They overlap now, so this is the ceiling for the
    /// whole thing rather than for half of it.
    /// </remarks>
    private const int JudgementSeconds = 30;

    /// <summary>Radar looked, and the corpus has nothing that teaches this lesson.</summary>
    public const string NothingRelevant =
        "Nothing in Radar's library teaches this lesson yet. Rather than fill the space with "
        + "items that only share a word with the title, it is left empty.";

    /// <summary>
    /// How many passes a lesson may wait for its videos before settling without them.
    /// </summary>
    /// <remarks>
    /// Three. Enough for a background warm to land across a couple of visits, few enough that a
    /// lesson whose search genuinely returns nothing — or whose quota is spent — stops asking.
    /// </remarks>
    private const int MaxVideoWaits = 3;

    /// <summary>
    /// How long a lesson waits when nothing has searched for it yet.
    /// </summary>
    /// <remarks>
    /// Longer than <see cref="MaxVideoWaits"/>, because the thing it is waiting for is a queue
    /// rather than an answer: the warm covers a few lessons per read, so a twenty-lesson path
    /// takes several reads to reach the end of itself. Still finite, so a key that has been
    /// revoked cannot leave every lesson asking for ever.
    /// </remarks>
    private const int MaxWaitsUnsearched = 12;

    /// <summary>Still collecting, and nothing has arrived yet.</summary>
    public const string StillGatheringNothingYet =
        "Radar is still looking for material for this lesson. Open it again shortly.";

    /// <summary>
    /// Still collecting, with some of it already on screen.
    /// </summary>
    /// <remarks>
    /// Two sentences for one state, because one of them was a lie on production: a lesson with no
    /// resources at all displayed "what it has found so far is below" above an empty space. A note
    /// about what is on the screen has to agree with what is on the screen.
    /// </remarks>
    public const string StillGatheringSomeFound =
        "Radar is still looking for videos for this lesson. What it has found so far is below; "
        + "open this again shortly for the rest.";

    /// <summary>
    /// Radar could not look. A fault, and it has to read like one.
    /// </summary>
    /// <remarks>
    /// It does not claim nothing is shown. A failed check leaves whatever the lesson already had
    /// in place rather than wiping it on the strength of a call that did not complete, so the
    /// earlier wording contradicted the behaviour directly beneath it.
    /// </remarks>
    public const string CouldNotCheck =
        "Radar could not check this lesson's material just now. Anything below is from an earlier "
        + "check; the rest will follow the next time this path is opened.";

    private async Task AdmitAsync(Pending pending, string goal, CancellationToken ct)
    {
        var lesson = pending.Lesson;

        var candidates = pending.Videos
            .Concat(pending.Articles)
            .Select(r => new LessonResourceJudge.Candidate(
                r.Id, r.Title, r.Source, r.Summary,
                FromTranscript: r.SummarySource == SummarySource.Transcript))
            .ToList();

        if (candidates.Count == 0)
        {
            lesson.ResourceAttempts++;
            lesson.Resources = [];

            // Nothing to ask about yet is not the same as nothing existing, while the video cache
            // is still being filled.
            var waiting = _youtube.IsConfigured && lesson.ResourceAttempts < MaxVideoWaits;

            lesson.ResourceNote = waiting ? StillGatheringNothingYet : NothingRelevant;
            if (!waiting) lesson.ResourcesJudgedAt = DateTime.UtcNow;
            return;
        }

        var verdicts = await _judge.JudgeAsync(
            lesson.Title, lesson.Body, goal, pending.SiblingTitles, candidates, ct);

        lesson.ResourceAttempts++;

        if (verdicts is null)
        {
            // Not an answer. Nothing is kept and nothing is settled: a provider that is down, out
            // of credit or past its deadline must be retried, not written down as a finding about
            // the corpus. Whatever the lesson already had is left alone rather than wiped on the
            // strength of a failed call.
            lesson.ResourceNote = CouldNotCheck;
            return;
        }

        var kept = verdicts.ToDictionary(v => v.Id, v => v, StringComparer.Ordinal);

        var videos = Admitted(pending.Videos, kept).Take(VideosPerLesson).ToList();
        var articles = Admitted(pending.Articles, kept).Take(ArticlesPerLesson).ToList();

        lesson.Resources = [.. videos, .. articles];

        // Videos are read from a cache something else fills in the background, so a cold cache is
        // not an answer about videos — it is an answer that has not arrived. Settling here would
        // mean no lesson built after this ever gets one, because the first pass is the only pass.
        // A lesson spends a wait only once something has actually looked for its videos.
        //
        // The count used to advance on every pass regardless, and the warm reaches a handful of
        // lessons per read - so a lesson near the end of a long path could spend all three waits
        // queueing and then settle as "nothing found", having never been searched for. Seven
        // lessons on production settled that way, and because a settled lesson is never refilled,
        // they would have stayed empty for the life of the roadmap.
        //
        // The ceiling is still there for the case where searching never succeeds at all - a
        // revoked key, an exhausted quota that never resets - so this cannot wait for ever.
        var waitingForVideos =
            _youtube.IsConfigured
            && pending.Videos.Count == 0
            && lesson.ResourceAttempts < (pending.VideosSearched ? MaxVideoWaits : MaxWaitsUnsearched);

        lesson.ResourceNote =
            waitingForVideos
                ? lesson.Resources.Count > 0 ? StillGatheringSomeFound : StillGatheringNothingYet
                : lesson.Resources.Count == 0 ? NothingRelevant
                : string.Empty;

        // Settled only when there was nothing further to wait for. Whatever the old matcher left
        // on this lesson has now been replaced by what the judge admitted, which may be nothing.
        if (!waitingForVideos) lesson.ResourcesJudgedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// The resources the judge kept, in the order they were ranked rather than the order it
    /// happened to answer in.
    /// </summary>
    private static IEnumerable<LessonResource> Admitted(
        List<LessonResource> candidates,
        Dictionary<string, LessonResourceJudge.Verdict> kept) =>
        candidates
            .Where(r => kept.ContainsKey(r.Id))
            .Select(r =>
            {
                var verdict = kept[r.Id];

                r.WhyRelevant = verdict.Why;

                // An article already carries the publisher's own account of itself, written when
                // it was ingested. Replacing that would be paying a model to paraphrase something
                // better than its paraphrase.
                //
                // A video carries only the raw YouTube description, which is a blurb with links
                // and timestamps in it rather than anything a learner wants to read. That is what
                // the written version replaces. When there was too little to work from it comes
                // back empty, and the card shows the title and channel alone rather than a
                // sentence restating the title.
                if (r.Kind == LessonResourceKind.Video)
                {
                    r.Summary = verdict.Covers;
                }

                return r;
            });

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
    /// They carry YouTube's own description, which is what the judge writes the learner-facing
    /// summary from. Nothing is written for a video that gets dropped: one call decides what to
    /// keep and explains only what it kept.
    /// </remarks>
    private async Task<List<LessonResource>> VideoCandidatesAsync(
        RoadmapLesson lesson, CancellationToken ct)
    {
        // The hint exists because a readable lesson title makes a poor query: "What hospital nurse
        // manager actually involves day to day" finds nothing.
        var query = !string.IsNullOrWhiteSpace(lesson.SearchHint) ? lesson.SearchHint! : lesson.Title;

        var videos = new List<LessonResource>();

        if (_youtube.IsConfigured)
        {
            var cached = await _youtube.GetCachedAsync(query, ct: ct);
            videos.AddRange(cached.Select(VideoResource));
        }

        // A second place to look, with no key and no quota.
        //
        // Everything a lesson showed came from one source, so a subject YouTube ranks poorly for
        // had no videos at all - and a lesson with no videos is the complaint this whole path
        // exists to answer. The open network holds hundreds to thousands per subject, and states
        // each video's licence, which is the only route Radar has to a real transcript.
        //
        // Asked for fewer than YouTube: it is the second source, and the judge ranks what comes
        // back from both together.
        var open = await OpenVideosAsync(query, ct);

        // Counted here rather than on the service: lessons are built concurrently, so a field
        // would be a budget shared between whichever lessons happened to overlap.
        var attempted = 0;

        foreach (var video in open)
        {
            // Attempts, not successes.
            //
            // Counting successes meant a licensed video with no caption track cost a request and
            // did not spend the budget, so a lesson kept trying - and most licensed videos have
            // no captions. Eleven serial requests to somebody else's server, each with a twelve
            // second deadline, is a Radar Learn that hangs for two minutes and then shows the
            // same thing it would have shown immediately.
            var mayRead = attempted < TranscriptsPerLesson
                          && OpenVideoLicence.PermitsReading(video.LicenceId);

            if (mayRead) attempted++;

            videos.Add(mayRead ? await WithTranscriptAsync(video, ct) : VideoResource(video));
        }

        if (videos.Count == 0) return [];

        return LessonRelevance.Distinct(videos, v => v.Url, v => v.Title)
            .Take(CandidatesPerKind)
            .ToList();
    }

    /// <summary>
    /// A cached YouTube result as a candidate resource.
    /// </summary>
    /// <remarks>
    /// Carries the description. YouTubeSearchService parses it and the cache stores it, and it
    /// was not being passed on - so the judge was handed a title and a channel name and asked to
    /// describe the video from them, which is how a summary came out as the headline said back.
    ///
    /// Separate from the query so it can be tested. The line that passes the description was
    /// written once, lost when the edit that contained it aborted, and committed under a message
    /// saying it was there. Nothing noticed, because the only tests of this went through the
    /// judge with a candidate built by hand.
    /// </remarks>
    /// <summary>
    /// How long a search of the open network is worth keeping.
    /// </summary>
    /// <remarks>
    /// YouTube results are cached because the API is metered. These were not cached at all, so
    /// every read of a path that still had unsettled lessons searched a public index again -
    /// fifteen lessons, fifteen searches, on a service that owes Radar nothing and has every
    /// reason to rate-limit it. Freshness is worth little here: the subject of a lesson does not
    /// change, and neither does what the network holds on it hour to hour.
    /// </remarks>
    private static readonly TimeSpan OpenVideosKeptFor = TimeSpan.FromHours(12);

    /// <summary>Videos from the open network, searched once per subject per half day.</summary>
    private async Task<List<OpenVideoRef>> OpenVideosAsync(string query, CancellationToken ct)
    {
        var key = $"peertube:{query.ToLowerInvariant()}";

        if (_cache.TryGetValue(key, out List<OpenVideoRef>? cached) && cached is not null)
        {
            return cached;
        }

        var found = await _peerTube.SearchAsync(query, count: CandidatesPerKind, ct: ct);

        // Kept even when empty. A subject the network holds nothing on is the case worth not
        // asking about again, and it is the common one.
        _cache.Set(key, found, OpenVideosKeptFor);
        return found;
    }

    /// <summary>
    /// How many videos in one lesson are worth fetching captions for.
    /// </summary>
    /// <remarks>
    /// Each is two requests to somebody else's server, and a lesson shows five videos. Two is
    /// enough that a lesson with any openly licensed material gets at least one summary written
    /// from what was actually said, without making a lesson build wait on a queue of downloads.
    /// </remarks>
    private const int TranscriptsPerLesson = 2;

    /// <summary>
    /// A candidate whose summary is written from the video's own words, where that is allowed.
    /// </summary>
    /// <remarks>
    /// The difference this makes is the one the SummarySource enum was added to record: a
    /// summary written from a forty-minute transcript and one written from a two-line description
    /// read identically on screen, and only one of them describes the thing you are about to
    /// watch.
    ///
    /// The licence is checked by the caller, which is also where the per-lesson budget is kept.
    /// Falls back to the description silently - a video without a readable transcript is still
    /// worth watching, and the card already says which the summary came from.
    /// </remarks>
    private async Task<LessonResource> WithTranscriptAsync(OpenVideoRef video, CancellationToken ct)
    {
        var resource = VideoResource(video);

        var spoken = await _peerTube.TranscriptAsync(video, ct);
        if (string.IsNullOrWhiteSpace(spoken)) return resource;

        resource.Summary = Representative(spoken);
        resource.SummarySource = SummarySource.Transcript;
        return resource;
    }

    /// <summary>How much of a transcript the judge is given.</summary>
    private const int MostTranscriptForAJudge = 2400;

    /// <summary>
    /// The part of a talk that is actually about the subject.
    /// </summary>
    /// <remarks>
    /// Taken from a little way in rather than from the start. A video opens with a greeting, an
    /// ask to subscribe and a word from a sponsor, so the first paragraph of a transcript
    /// describes the channel and not the lesson - and a summary written from it says the video
    /// is about welcoming you back.
    ///
    /// A twelfth of the way in, which clears a typical intro without reaching the sign-off.
    /// </remarks>
    private static string Representative(string spoken)
    {
        if (spoken.Length <= MostTranscriptForAJudge) return spoken;

        var start = Math.Min(spoken.Length / 12, spoken.Length - MostTranscriptForAJudge);

        // From a word boundary, so the excerpt does not open mid-syllable.
        var space = spoken.IndexOf(' ', start);
        if (space > 0 && space - start < 40) start = space + 1;

        return spoken.Substring(start, Math.Min(MostTranscriptForAJudge, spoken.Length - start));
    }

    /// <summary>
    /// A video from an open platform as a candidate resource.
    /// </summary>
    /// <remarks>
    /// The licence is carried because it decides what Radar may do next: a Creative Commons
    /// video can have its captions read and a real summary written from them, and one with no
    /// licence set cannot. The summary still starts as the description either way - the judge
    /// rewrites it, and SummarySource says which it was written from.
    /// </remarks>
    public static LessonResource VideoResource(OpenVideoRef video) => new()
    {
        Kind = LessonResourceKind.Video,
        Title = video.Title,
        Url = video.Url,
        EmbedUrl = video.EmbedUrl,
        ThumbnailUrl = video.ThumbnailUrl,
        Source = video.ChannelTitle,
        PublishedAt = video.PublishedAt,
        Summary = video.Description,
        SummarySource = SummarySource.Description,

        // Named where there is one. It is what permits reading the captions, and every one of
        // these licences asks for attribution in return.
        Licence = OpenVideoLicence.Name(video.LicenceId),
    };

    public static LessonResource VideoResource(YouTubeVideoRef video) => new()
    {
        Kind = LessonResourceKind.Video,
        Title = video.Title,
        Url = video.Url,
        EmbedUrl = video.EmbedUrl,
        ThumbnailUrl = video.ThumbnailUrl,
        Source = video.ChannelTitle,
        PublishedAt = video.PublishedAt,

        // What YouTube says the video is about. Held here until the judge rewrites it into
        // something a learner reads; if it cannot, this is replaced with nothing rather than
        // shown raw, because a description is a blurb with links and timestamps in it.
        Summary = video.Description ?? string.Empty,

        // There are no transcripts - the transcription keys are not configured - so a summary is
        // written from the description, and the card says so.
        SummarySource = SummarySource.Description,
    };

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
        RoadmapLesson lesson, IReadOnlyCollection<string> pathDistinctive, CancellationToken ct)
    {
        var terms = LessonRelevance.TermsOf(lesson.Title, lesson.SearchHint);
        if (terms.Count == 0) return [];

        // Narrowed from the path's set to this lesson's own, so a word that is rare in the corpus
        // still only counts for the lessons that actually use it.
        var distinctive = terms
            .Where(t => pathDistinctive.Contains(t, StringComparer.OrdinalIgnoreCase))
            .ToList();

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

                // Already written, when the item was enriched. Re-summarising something that has
                // a summary is paying twice for the same sentence.
                //
                // WhyItMatters is deliberately not in this chain any more. It is a different
                // sentence - why the item is worth attention, not what is in it - and for a paper
                // ingested from OpenAlex without an abstract it is a template: "Cited 0 times in
                // peer-reviewed scholarship. Published in ." That reached a learner as the
                // summary of a lesson resource, labelled as the publisher's own words. An empty
                // summary is better than a filled-in form.
                Summary = FirstNonEmpty(item.WhatHappened, item.AiSummary),
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
        if (corpusSize <= 0 || terms.Count == 0) return [];

        // Several at a time, but not all. Each is a collection scan, and a twenty-term path
        // firing twenty at once would trade a slow page for a struggling database.
        using var slots = new SemaphoreSlim(ConcurrentCounts);

        var counted = await Task.WhenAll(terms.Select(async term =>
        {
            var key = "lesson-term-frequency:" + term.ToLowerInvariant();
            if (_cache.TryGetValue(key, out long cached)) return (Term: term, Matches: cached);

            await slots.WaitAsync(ct);
            try
            {
                // Checked again inside the gate: lessons on a path wait on the same terms, and
                // without this each would scan for one after queueing behind the others.
                if (_cache.TryGetValue(key, out long warmed)) return (Term: term, Matches: warmed);

                var matches = await _db.ContentItems.CountDocumentsAsync(
                    Builders<ContentItem>.Filter.Regex(
                        c => c.Title,
                        new MongoDB.Bson.BsonRegularExpression(
                            LessonRelevance.WordStartPattern(term), "i")),
                    cancellationToken: ct);

                _cache.Set(key, matches, TimeSpan.FromDays(1));
                return (Term: term, Matches: matches);
            }
            finally
            {
                slots.Release();
            }
        }));

        return counted
            .Where(c => LessonRelevance.IsDistinctive(c.Matches, corpusSize))
            .Select(c => c.Term)
            .ToList();
    }

    /// <summary>How many corpus scans may run at once.</summary>
    private const int ConcurrentCounts = 4;

    /// <summary>
    /// Whether anything has searched for this lesson's videos yet.
    /// </summary>
    /// <remarks>
    /// Separate from having any, because an empty result and an absent one look identical from
    /// the cache and mean opposite things to a lesson deciding whether to keep waiting.
    /// </remarks>
    private async Task<bool> VideosSearchedForAsync(RoadmapLesson lesson, CancellationToken ct)
    {
        if (!_youtube.IsConfigured) return false;

        var query = !string.IsNullOrWhiteSpace(lesson.SearchHint) ? lesson.SearchHint! : lesson.Title;

        return await _youtube.HasSearchedAsync(query, ct: ct);
    }

    /// <summary>How much there is to be rare within. Cached for the same reason.</summary>
    private async Task<long> CorpusSizeAsync(CancellationToken ct)
    {
        if (_cache.TryGetValue("lesson-corpus-size", out long size)) return size;

        size = await _db.ContentItems.EstimatedDocumentCountAsync(cancellationToken: ct);
        _cache.Set("lesson-corpus-size", size, TimeSpan.FromDays(1));

        return size;
    }

    /// <summary>The YouTube id, taken off the embed url the ref built.</summary>
    private static string VideoIdOf(LessonResource video) =>
        video.EmbedUrl?.Split('/').LastOrDefault() ?? video.Url.Split("v=").LastOrDefault() ?? video.Id;

    private static string FirstNonEmpty(params string?[] candidates) =>
        candidates.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c))?.Trim() ?? string.Empty;
}
