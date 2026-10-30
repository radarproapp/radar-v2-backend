using MongoDB.Driver;
using RadarV2.Data;
using RadarV2.Data.Documents;
using RadarV2.Models;
using RadarV2.Services.Ingestion;
using RadarV2.Services.Interfaces;

namespace RadarV2.Services;

public class MongoIntelligenceFeedService : IIntelligenceFeedService
{
    private readonly RadarDatabase _db;
    private readonly AiRelevanceService _aiRelevance;
    private readonly IUserLearningRecordService _records;

    public MongoIntelligenceFeedService(RadarDatabase db, AiRelevanceService aiRelevance, IUserLearningRecordService records)
    {
        _db = db;
        _aiRelevance = aiRelevance;
        _records = records;
    }

    public async Task<List<ContentItem>> GetFeedAsync(UserProfile profile, ContentType? filterType = null, int page = 1, int pageSize = 20, bool acrossSurfaces = false)
    {
        // Nothing is theirs until they have said what theirs means.
        //
        // The alternative — rank the whole corpus on an empty profile — produces a feed of
        // whatever published most recently, which on this source mix is sport and entertainment.
        // Shown under a heading that promises relevance, that teaches a new user Radar does not
        // know them, and the lesson is hard to undo. A handful of credible items and a plain
        // explanation is the honest version.
        if (!profile.IsPersonalised) return await GeneralSampleAsync(filterType);

        var dismissedIds = !string.IsNullOrEmpty(profile.Id)
            ? await GetDismissedItemIdsAsync(profile.Id)
            : [];

        var filter = await BuildFeedFilterAsync(
            profile,
            filterType,
            dismissedIds,
            acrossSurfaces ? FilterDefinition<ContentItem>.Empty : null);

        var items = await _db.ContentItems
            .Find(filter)
            .SortByDescending(i => i.PublishedAt)
            .Limit(page * pageSize * 3)
            .ToListAsync();

        RankByInterest(profile, items, await _records.BuildAsync(profile));
        items = items.Skip((page - 1) * pageSize).Take(pageSize).ToList();

        await DecorateAsync(profile, items);
        return items;
    }

    /// <summary>
    /// What Radar holds for this reader on the research side: papers, policy, essays and the
    /// long-form analysis that comes from an academic, policy or ideas source.
    /// </summary>
    /// <remarks>
    /// The other half of moving them out of the feed. Taking a type off one surface without
    /// putting it on another leaves content ingested, stored, ranked and shown to nobody - and
    /// that is invisible from both screens, because each looks correct on its own terms.
    ///
    /// Matched by the same rule as the feed. A paper has to speak to something the reader wrote,
    /// for the same reason a news item does: a list of everything Radar holds, under a heading
    /// promising relevance, is the thing this product was built to stop doing.
    /// </remarks>
    public async Task<List<ContentItem>> GetResearchLibraryAsync(
        UserProfile profile,
        int limit = 20,
        CancellationToken ct = default)
    {
        limit = Math.Clamp(limit, 1, 50);

        if (!profile.IsPersonalised) return [];

        var dismissedIds = !string.IsNullOrEmpty(profile.Id)
            ? await GetDismissedItemIdsAsync(profile.Id)
            : [];

        var filter = await BuildFeedFilterAsync(
            profile, null, dismissedIds, ContentSurface.ResearchFilter());

        // Over-fetched by date, then ranked, then cut.
        //
        // This used to sort on the publication date and stop there, so the library was "the
        // newest research Radar holds" - which is not what the spec asks for. Credibility and
        // relevance come first in its list, and a foundational paper could never appear at all,
        // because something is always newer. Recency is still what decides which candidates are
        // worth considering; it is no longer what decides the order they are read in.
        var candidates = await _db.ContentItems
            .Find(filter)
            .SortByDescending(i => i.PublishedAt)
            .ThenByDescending(i => i.Id)
            .Limit(limit * CandidatesPerPlace)
            .ToListAsync(ct);

        var ranked = ResearchRanking.Ordered(candidates, profile).Take(limit).ToList();

        // Where each one sits in time, said on the card rather than left to be worked out from
        // a year. Costs nothing and needs no model, unlike the breakdown it used to live inside.
        foreach (var paper in ranked) paper.PaperEra = ResearchRanking.EraOf(paper);

        return ranked;
    }

    /// <summary>
    /// How many papers to look at for each one shown.
    /// </summary>
    /// <remarks>
    /// Enough that ranking has something to choose between, and small enough that the query
    /// is still served by the PublishedAt index rather than reading the collection.
    ///
    /// It moves the cliff rather than removing it. The candidates are still the newest N, so on
    /// a corpus larger than this a genuinely old paper cannot be reached however well cited it
    /// is. Worth fixing with a second candidate pull when the research corpus is big enough for
    /// it to matter, and worth not claiming until then.
    /// </remarks>
    private const int CandidatesPerPlace = 4;

    public async Task<PagedResult<ContentItem>> GetFeedPageAsync(UserProfile profile, ContentType? filterType, string? cursor, int limit, CancellationToken ct = default)
    {
        limit = Math.Clamp(limit, 1, 50);

        // The same rule as GetFeedAsync, and it has to be stated twice because this is the method
        // the feed actually calls. Naming the reason was not enough on its own: the keyset query
        // below would still run against an empty profile and hand back a full page of unranked
        // content underneath a prompt saying the feed is not yet theirs.
        //
        // Undecorated, like the other sample path. DecorateAsync writes a "why this matters to
        // you" per item, and on a profile that has said nothing there is no honest sentence to
        // write — only an LLM call spent inventing one.
        if (!profile.IsPersonalised)
        {
            return new PagedResult<ContentItem>
            {
                Items = await GeneralSampleAsync(filterType),
                NextCursor = null,
                HasMore = false,
                EmptyReason = FeedEmptyReason.NotPersonalisedYet,
            };
        }

        var dismissedIds = !string.IsNullOrEmpty(profile.Id)
            ? await GetDismissedItemIdsAsync(profile.Id)
            : [];

        var filter = await BuildFeedFilterAsync(profile, filterType, dismissedIds);

        if (Cursor.TryDecode(cursor, out var sortKey, out var lastId))
        {
            // Resume strictly *after* the last row of the previous page. Ordering is
            // (PublishedAt desc, Id desc), so "after" means an older timestamp — or the same
            // timestamp with a smaller id, which is what keeps ties from repeating or skipping.
            filter &= Builders<ContentItem>.Filter.Lt(i => i.PublishedAt, sortKey)
                | (Builders<ContentItem>.Filter.Eq(i => i.PublishedAt, sortKey)
                   & Builders<ContentItem>.Filter.Lt(i => i.Id, lastId));
        }

        // Fetch one row beyond the page. Arriving means there is more; dropping it means we never
        // need a second count query just to decide whether to hand out a cursor.
        var batch = await _db.ContentItems
            .Find(filter)
            .SortByDescending(i => i.PublishedAt)
            .ThenByDescending(i => i.Id)
            .Limit(limit + 1)
            .ToListAsync(ct);

        var hasMore = batch.Count > limit;
        if (hasMore) batch.RemoveAt(batch.Count - 1);

        // Take the cursor from the keyset position *before* anything re-ranks the page: relevance
        // scores are recomputed per request and are not a stable, indexable sort key.
        var nextCursor = hasMore && batch.Count > 0
            ? Cursor.Encode(batch[^1].PublishedAt, batch[^1].Id)
            : null;

        RankByInterest(profile, batch, await _records.BuildAsync(profile));
        await DecorateAsync(profile, batch);

        return new PagedResult<ContentItem>
        {
            Items = batch,
            NextCursor = nextCursor,
            HasMore = hasMore,
            // Only costs queries when there is nothing to show, which is the one moment it is
            // worth spending them: the alternative is a blank page and a guess.
            EmptyReason = batch.Count > 0
                ? FeedEmptyReason.NotEmpty
                // The same strand filter without the reader's dismissals, so the diagnosis can
                // tell "nothing matches your words" from "you dismissed it all".
                : await DiagnoseEmptyAsync(
                    filterType,
                    dismissedIds,
                    await BuildFeedFilterAsync(profile, filterType, [])),
        };
    }

    /// <summary>
    /// Why there is nothing to show.
    ///
    /// The feed already drops Radar's own narrowing when it would empty the page — that is the
    /// interest-layer rung in <see cref="BuildFeedFilterAsync"/>. What it cannot do is undo the
    /// user's own choices: silently returning items they dismissed, or types they filtered out,
    /// would be overriding them rather than helping. So the last rung is to say plainly which of
    /// their choices is responsible, and let them undo it.
    /// </summary>
    private async Task<FeedEmptyReason> DiagnoseEmptyAsync(
        ContentType? filterType,
        IReadOnlyCollection<string> dismissedIds,
        FilterDefinition<ContentItem>? withoutDismissals = null)
    {
        if (!await _db.ContentItems.Find(Builders<ContentItem>.Filter.Empty).Limit(1).AnyAsync())
            return FeedEmptyReason.NoContentAtAll;

        if (filterType.HasValue)
        {
            var ofType = Builders<ContentItem>.Filter.Eq(i => i.Type, filterType.Value);
            if (!await _db.ContentItems.Find(ofType).Limit(1).AnyAsync())
                return FeedEmptyReason.NoneOfThisType;
        }

        // Whether their own words are what emptied the page.
        //
        // This ran before the strand filter existed and knew nothing about it, so when the feed
        // correctly matched nothing it reported one of two untrue things: that the reader had
        // dismissed everything — with a "bring them back" button that could not help — or that
        // they were up to date, while the library held plenty that simply did not mention their
        // words.
        //
        // Asked without their dismissals, so the two causes cannot be confused: if nothing
        // matches even before dismissals are applied, dismissals are not the reason.
        if (withoutDismissals is not null
            && !await _db.ContentItems.Find(withoutDismissals).Limit(1).AnyAsync())
        {
            return FeedEmptyReason.NothingMatchesYourTerms;
        }

        // Content exists, matches the type, and matches their words — so the only thing left
        // standing between the reader and it is their own dismissals.
        if (dismissedIds.Count > 0) return FeedEmptyReason.EverythingDismissed;

        return FeedEmptyReason.NothingNewRightNow;
    }

    /// <summary>
    /// The filter every page of a feed request is built from: content type, the user's dismissals,
    /// and their interest layers.
    ///
    /// The interest-layer narrowing is dropped when nothing exists inside those layers, which matches
    /// the original fallback behaviour — but the decision is made from an existence check rather than
    /// from whatever the current page happened to contain, so every page of a cursor chain is built
    /// from the same filter instead of changing shape mid-pagination.
    /// </summary>
    private async Task<FilterDefinition<ContentItem>> BuildFeedFilterAsync(
        UserProfile profile,
        ContentType? filterType,
        IReadOnlyCollection<string> dismissedIds,
        FilterDefinition<ContentItem>? surface = null)
    {
        // The feed carries news, podcasts and posts. Papers, policy documents and essays go to
        // Research Discovery, which is the product built to read them - see ContentSurface.
        //
        // Only when no type was asked for. Applying it on top of an explicit type produced an
        // impossible query - In(Type, [Article, Podcast]) and Eq(Type, Essay) matches nothing -
        // and the weekly brief asks this service for Essay and for Video by name to fill its
        // sections. Both came back empty for every personalised reader, silently, because an
        // empty rescue list looks exactly like a week with no blog posts in it.
        //
        // The surface is enforced at the edge instead: FeedController refuses a type that is not
        // in the feed, so /api/feed?type=ResearchPaper is a bad request rather than papers under
        // a heading that says Intelligence. Callers inside the process ask for what they need.
        // The surface is two conditions, and an explicit type replaced both of them.
        //
        // Intelligence means "a news type AND not a research layer". Narrowing by type dropped
        // the layer half, so /api/feed?type=Article handed back a think tank's working paper
        // under a heading that says Intelligence - the behaviour the split exists to end,
        // surviving one layer down from where I thought I had closed it.
        //
        // A caller that wants to reach past the surface passes its own, which is what the weekly
        // brief does to fetch essays and video for its rescue sections.
        var filter = surface ?? ContentSurface.IntelligenceFilter();

        if (filterType.HasValue)
            filter &= Builders<ContentItem>.Filter.Eq(i => i.Type, filterType.Value);

        if (dismissedIds.Count > 0)
            filter &= Builders<ContentItem>.Filter.Nin(i => i.Id, dismissedIds);

        var goalTerms = FeedStrands.TermsFor(FeedStrands.Strand.Goal, profile);

        // An item has to mention something the reader wrote. Nothing else is shown.
        //
        // What this replaced: the interests were mapped onto content layers, items in those
        // layers were kept, and when no layer matched the filter was returned untouched - every
        // item Radar holds, under a heading promising relevance. Both halves failed the same way.
        // The layers are enormous - Artificial Intelligence maps to Ideas, Science and Skills,
        // which between them hold most of the corpus - so even the working path barely narrowed
        // anything, and the fallback narrowed nothing at all. A feed item never had to be about
        // anything the reader had said.
        //
        // The layer mapping is gone from selection rather than kept as an extra way to qualify.
        // Or-ing it back in would admit everything in those three layers again, which is the
        // behaviour being removed; it would just have a better comment above it. What it was
        // genuinely good at - a rough sense of subject area - belongs in ranking, where
        // InterestPersonalization already uses it.
        var strands = new List<FilterDefinition<ContentItem>>();

        // Interests go in whole, so they can be stemmed, expanded into the field's own vocabulary,
        // and looked for as a phrase in the body. Goals and challenges stay word-matched against
        // the narrow fields: they are written as sentences, and the words they reduce to are
        // common enough that letting them loose in a body would fill the feed with near-misses.
        if (profile.Interests.Count > 0) strands.Add(FeedStrands.AboutSubjects(profile.Interests));
        if (goalTerms.Count > 0) strands.Add(FeedStrands.Mentioning(goalTerms));

        // Both strands empty means nothing to match on, and nothing to match on means nothing
        // shown.
        //
        // I wrote this as "return filter" under a comment claiming it was unreachable because
        // IsPersonalised is checked first. It is not: IsPersonalised only wants one non-blank
        // entry in each box, not one that yields a term. Interests ["New"] with goals ["Learn
        // more"] is personalised and reduces to nothing, because all three words are scaffolding
        // — and that profile got the entire corpus, which is exactly the behaviour this change
        // exists to remove, preserved behind an assertion that it could not happen.
        if (strands.Count == 0) return filter & Builders<ContentItem>.Filter.Where(_ => false);

        return filter & Builders<ContentItem>.Filter.Or(strands);
    }

    /// <summary>
    /// Orders the feed by fit. The record is built once and reused: it is derived on read, so
    /// scoring each item separately would rebuild it for every item in the list.
    /// </summary>
    /// <summary>
    /// A couple of items for someone who has not personalised, so the screen is not a void.
    /// </summary>
    /// <remarks>
    /// Deliberately small and deliberately not ranked. These are not a feed and must not look
    /// like one: the personalisation prompt is the content of that screen, and these sit under
    /// it as a sample of what Radar carries.
    ///
    /// Tier 1 only. If two items are all somebody sees before deciding whether this product is
    /// for them, they should come from the most credible sources on the list.
    /// </remarks>
    private async Task<List<ContentItem>> GeneralSampleAsync(ContentType? filterType)
    {
        // The same rule as the feed proper. Without it the two items a brand-new reader meets
        // could both be policy papers, under a heading that says Intelligence - which is the
        // behaviour the split was made to end, surviving on the one path nobody filtered.
        var surface = filterType.HasValue
            ? Builders<ContentItem>.Filter.Eq(i => i.Type, filterType.Value)
            : ContentSurface.IntelligenceFilter();

        const int sample = 2;

        var tierOneSources = SourceRegistry.All
            .Where(source => source.Tier == 1)
            .Select(source => source.Id)
            .ToList();

        // The one thing the sample does honour. Picking Podcasts in the sidebar and being handed
        // two blog posts is a small lie, and it is the kind that reads as a broken filter rather
        // than as a sample.
        var ofType = surface;

        var items = await _db.ContentItems
            .Find(Builders<ContentItem>.Filter.In(i => i.IngestionSourceId, tierOneSources) & ofType)
            .SortByDescending(i => i.PublishedAt)
            .Limit(sample)
            .ToListAsync();

        // Nothing tier 1 of that type — or nothing tier 1 at all, on a new deployment. Anything
        // recent beats an empty screen under a heading that says Latest.
        if (items.Count == 0)
        {
            items = await _db.ContentItems
                .Find(ofType)
                .SortByDescending(i => i.PublishedAt)
                .Limit(sample)
                .ToListAsync();
        }

        return items;
    }

    private static void RankByInterest(UserProfile profile, List<ContentItem> items, UserLearningRecord? record = null) =>
        items.Sort((a, b) => InterestPersonalization.ContentScore(b, profile, record)
            .CompareTo(InterestPersonalization.ContentScore(a, profile, record)));

    /// <summary>
    /// Per-user decoration applied after the page is chosen: AI relevance ranking, then the saved
    /// flag and a personalised reason. Deliberately separate from page selection so it can never
    /// influence which items land on a page.
    /// </summary>
    private async Task DecorateAsync(UserProfile profile, List<ContentItem> items)
    {
        await _aiRelevance.ApplyAsync(profile, items);

        if (!string.IsNullOrEmpty(profile.Id))
        {
            var savedIds = await GetSavedItemIdsAsync(profile.Id);
            foreach (var item in items)
                item.IsSaved = savedIds.Contains(item.Id);
        }

        foreach (var item in items)
        {
            // AI relevance (when a key is configured) fills these first; the deterministic engine
            // then fills whatever is still empty so the UI always has a grounded reason and action.
            var explanation = InterestPersonalization.Explain(profile, item);

            // Assigned only when there is something to say. Explain returns an empty Why when
            // nothing in the profile fired on the item, and ??= would happily write that empty
            // string into the slot — leaving the card with a blank reason block instead of
            // falling through to the honest one-liner below.
            if (string.IsNullOrWhiteSpace(item.PersonalizedWhy) && !string.IsNullOrWhiteSpace(explanation.Why))
                item.PersonalizedWhy = explanation.Why;

            item.NextMove ??= explanation.NextMove;
            if (item.MatchedSignals.Count == 0)
                item.MatchedSignals = explanation.MatchedSignals.ToList();

            item.WhyYoureSeeingThis ??= WhyShown.Build(item, item.MatchedSignals);
        }
    }

    public async Task<ContentItem?> GetByIdAsync(string id)
    {
        return await _db.ContentItems.Find(i => i.Id == id).FirstOrDefaultAsync();
    }

    public async Task SaveItemAsync(string userId, string contentItemId)
    {
        var exists = await _db.SavedItems
            .Find(s => s.UserId == userId && s.ContentItemId == contentItemId)
            .AnyAsync();

        if (!exists)
            await _db.SavedItems.InsertOneAsync(new SavedItemDoc
            {
                UserId = userId,
                ContentItemId = contentItemId
            });
    }

    public async Task UnsaveItemAsync(string userId, string contentItemId)
    {
        await _db.SavedItems.DeleteOneAsync(
            s => s.UserId == userId && s.ContentItemId == contentItemId);
    }

    /// <summary>
    /// Undoes every dismissal for this user.
    ///
    /// Exists so the empty feed can offer a way out. Telling someone "you have dismissed
    /// everything that matches" is only an improvement on a blank page if they can act on it, and
    /// there was no way to undo a dismissal at all - not one at a time, and not in bulk.
    /// </summary>
    public async Task<long> RestoreDismissedAsync(string userId)
    {
        var result = await _db.DismissedItems.DeleteManyAsync(d => d.UserId == userId);
        return result.DeletedCount;
    }

    public async Task DismissItemAsync(string userId, string contentItemId)
    {
        var exists = await _db.DismissedItems
            .Find(d => d.UserId == userId && d.ContentItemId == contentItemId)
            .AnyAsync();

        if (!exists)
            await _db.DismissedItems.InsertOneAsync(new DismissedItemDoc
            {
                UserId = userId,
                ContentItemId = contentItemId
            });
    }

    public async Task<List<ContentItem>> GetSavedItemsAsync(string userId)
    {
        var savedIds = await GetSavedItemIdsAsync(userId);
        if (savedIds.Count == 0) return [];

        var items = await _db.ContentItems
            .Find(i => savedIds.Contains(i.Id))
            .ToListAsync();

        foreach (var item in items)
            item.IsSaved = true;

        return items;
    }

    /// <summary>
    /// Finds content by what it is about, not only by what it is called.
    /// </summary>
    /// <remarks>
    /// Two things were wrong here. It searched Title and Signal alone, so looking for a podcast
    /// about a topic found only episodes with that word in the title — and podcast titles are
    /// frequently a show name and a number. And it passed the query straight into a
    /// BsonRegularExpression unescaped, so a search containing a bracket or a star either threw
    /// or matched something nobody asked for.
    ///
    /// Topic, tags, the summary and the transcript are searched too. The transcript is the one
    /// that makes "find me the episode where they discuss X" work at all, and it is already
    /// stored for anything the backfill has reached — nothing today, until a transcription key is
    /// set, which is why that search quietly returns less than it will.
    ///
    /// Bounded, because it was not: an unqualified search returned the whole matching corpus.
    /// </remarks>
    public async Task<List<ContentItem>> SearchAsync(string query, ContentType? type = null)
    {
        var term = query.Trim();
        if (term.Length < 2) return [];

        // Escaped. A user searching for "C++" or "(beta)" is searching for those characters, not
        // writing a pattern.
        var pattern = new MongoDB.Bson.BsonRegularExpression(
            System.Text.RegularExpressions.Regex.Escape(term), "i");

        var filter = Builders<ContentItem>.Filter.Or(
            Builders<ContentItem>.Filter.Regex(i => i.Title, pattern),
            Builders<ContentItem>.Filter.Regex(i => i.Signal, pattern),
            Builders<ContentItem>.Filter.Regex(i => i.Topic, pattern),
            Builders<ContentItem>.Filter.Regex(i => i.AiSummary, pattern),
            Builders<ContentItem>.Filter.Regex(i => i.WhatHappened, pattern),
            Builders<ContentItem>.Filter.Regex(i => i.TranscriptText, pattern),
            Builders<ContentItem>.Filter.AnyEq(i => i.Tags, term));

        if (type.HasValue)
            filter &= Builders<ContentItem>.Filter.Eq(i => i.Type, type.Value);

        var items = await _db.ContentItems
            .Find(filter)
            .SortByDescending(i => i.PublishedAt)
            .Limit(60)
            .ToListAsync();

        // Title matches first: somebody searching "Lex Fridman" wants the show, not an episode
        // that mentions him in passing. Within each group, newest first.
        return items
            .OrderByDescending(i => (i.Title ?? string.Empty).Contains(term, StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(i => i.PublishedAt)
            .ToList();
    }

    private async Task<HashSet<string>> GetSavedItemIdsAsync(string userId)
    {
        var saved = await _db.SavedItems
            .Find(s => s.UserId == userId)
            .ToListAsync();
        return saved.Select(s => s.ContentItemId).ToHashSet();
    }

    private async Task<HashSet<string>> GetDismissedItemIdsAsync(string userId)
    {
        var dismissed = await _db.DismissedItems
            .Find(d => d.UserId == userId)
            .ToListAsync();
        return dismissed.Select(d => d.ContentItemId).ToHashSet();
    }

    // The seed data is gone, and nothing replaces it.
    //
    // Seven hand-written items were inserted whenever the collection was empty, so that a fresh
    // deployment did not show a blank feed. One of them was a research paper — "AI Adoption
    // Among SMEs in Sub-Saharan Africa", by Dr. Amara Diallo and Prof. Chidi Okonkwo, in the
    // Journal of African Business, doi 10.1080/15228916.2024.001, surveying 500 firms across 12
    // countries. None of that exists; the doi resolves to nothing. The other six attached
    // invented key insights to real articles and lectures.
    //
    // The reason for having it has also gone. A new user does not meet a feed at all now: they
    // meet the three personalisation questions, and by the time they have answered, the
    // ingestion cycle has run. An empty collection reports NoContentAtAll, which is true and
    // which the client already draws.
}
