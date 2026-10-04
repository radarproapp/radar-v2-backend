using MongoDB.Driver;
using RadarV2.Data;
using RadarV2.Data.Documents;
using RadarV2.Models;
using RadarV2.Services.Interfaces;

namespace RadarV2.Services;

public class MongoIntelligenceFeedService : IIntelligenceFeedService
{
    private readonly RadarDatabase _db;
    private readonly AiRelevanceService _aiRelevance;

    public MongoIntelligenceFeedService(RadarDatabase db, AiRelevanceService aiRelevance)
    {
        _db = db;
        _aiRelevance = aiRelevance;
    }

    public async Task<List<ContentItem>> GetFeedAsync(UserProfile profile, ContentType? filterType = null, int page = 1, int pageSize = 20)
    {
        await EnsureSeedDataAsync();

        var dismissedIds = !string.IsNullOrEmpty(profile.Id)
            ? await GetDismissedItemIdsAsync(profile.Id)
            : [];

        var filter = await BuildFeedFilterAsync(profile, filterType, dismissedIds);

        var items = await _db.ContentItems
            .Find(filter)
            .SortByDescending(i => i.PublishedAt)
            .Limit(page * pageSize * 3)
            .ToListAsync();

        RankByInterest(profile, items);
        items = items.Skip((page - 1) * pageSize).Take(pageSize).ToList();

        await DecorateAsync(profile, items);
        return items;
    }

    public async Task<PagedResult<ContentItem>> GetFeedPageAsync(UserProfile profile, ContentType? filterType, string? cursor, int limit, CancellationToken ct = default)
    {
        await EnsureSeedDataAsync();
        limit = Math.Clamp(limit, 1, 50);

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

        RankByInterest(profile, batch);
        await DecorateAsync(profile, batch);

        return new PagedResult<ContentItem> { Items = batch, NextCursor = nextCursor, HasMore = hasMore };
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
        IReadOnlyCollection<string> dismissedIds)
    {
        var filter = filterType.HasValue
            ? Builders<ContentItem>.Filter.Eq(i => i.Type, filterType.Value)
            : Builders<ContentItem>.Filter.Empty;

        if (dismissedIds.Count > 0)
            filter &= Builders<ContentItem>.Filter.Nin(i => i.Id, dismissedIds);

        var interestLayers = InterestLayerMapper.MapInterestsToLayers(profile.Interests);
        if (interestLayers.Count > 0)
        {
            var layered = filter & Builders<ContentItem>.Filter.In(i => i.Layer, interestLayers);
            if (await _db.ContentItems.Find(layered).Limit(1).AnyAsync()) return layered;
        }

        return filter;
    }

    /// <summary>Cheap in-memory interest ranking, used before paging the fetch window.</summary>
    private static void RankByInterest(UserProfile profile, List<ContentItem> items) =>
        items.Sort((a, b) => InterestPersonalization.ContentScore(b, profile)
            .CompareTo(InterestPersonalization.ContentScore(a, profile)));

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
            item.PersonalizedWhy ??= InterestPersonalization.BuildWhy(profile, item);
    }

    public async Task<ContentItem?> GetByIdAsync(string id)
    {
        await EnsureSeedDataAsync();
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

    public async Task<List<ContentItem>> SearchAsync(string query, ContentType? type = null)
    {
        await EnsureSeedDataAsync();

        var filter = Builders<ContentItem>.Filter.Or(
            Builders<ContentItem>.Filter.Regex(i => i.Title, new MongoDB.Bson.BsonRegularExpression(query, "i")),
            Builders<ContentItem>.Filter.Regex(i => i.Signal, new MongoDB.Bson.BsonRegularExpression(query, "i"))
        );

        if (type.HasValue)
            filter &= Builders<ContentItem>.Filter.Eq(i => i.Type, type.Value);

        return await _db.ContentItems.Find(filter).ToListAsync();
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

    private static bool _seeded;

    private async Task EnsureSeedDataAsync()
    {
        if (_seeded) return;
        var count = await _db.ContentItems.CountDocumentsAsync(Builders<ContentItem>.Filter.Empty);
        if (count > 0) { _seeded = true; return; }

        await _db.ContentItems.InsertManyAsync(SeedItems);
        _seeded = true;
    }

    private static readonly List<ContentItem> SeedItems =
    [
        new()
        {
            Id = "a1",
            Type = ContentType.Article,
            Signal = "OpenAI's new training approach cuts multi-step AI errors substantially",
            Title = "A new reasoning framework for multi-step agent tasks",
            Topic = "AI Fundamentals",
            Source = "OpenAI",
            Url = "https://openai.com/research/",
            WhatHappened = "OpenAI describes a training approach where models decompose a task into verifiable sub-steps before acting, then check each result against the original goal. On long-horizon benchmarks it cuts compounding errors substantially compared with single-pass prompting.",
            AiSummary = "This is the concept behind most AI product roles you will interview for. Understanding it puts you ahead of coursework.",
            WhyItMatters = "Verification between steps matters more than a larger model. Long tasks fail by accumulating small errors, not by one big mistake.",
            KeyInsights = ["Verification between steps matters more than a larger model.", "Long tasks fail by accumulating small errors, not by one big mistake.", "The pattern generalises to any workflow you can break into checkable stages."],
            Tags = ["AI", "Machine Learning", "Technology"],
            PublishedAt = DateTime.UtcNow.AddHours(-3),
            EstimatedReadTime = "6 min"
        },
        new()
        {
            Id = "a2",
            Type = ContentType.Article,
            Signal = "Cross-border payment failures are reconciliation problems, not network problems",
            Title = "What actually breaks when you scale payments across borders",
            Topic = "Payments Infrastructure",
            Source = "Stripe",
            Url = "https://stripe.com/blog/engineering",
            WhatHappened = "Stripe's engineering team walks through the failure modes of cross-border payment flows.",
            AiSummary = "If money moves through your product, this is the article that saves you a quarter.",
            WhyItMatters = "Most cross-border failures are reconciliation problems, not network problems.",
            KeyInsights = ["Most cross-border failures are reconciliation problems, not network problems.", "Float held overnight is where the economics of a payments business live.", "Dispute liability is a commercial negotiation dressed up as a technical spec."],
            Tags = ["Fintech", "Payments", "Engineering"],
            PublishedAt = DateTime.UtcNow.AddDays(-1),
            EstimatedReadTime = "9 min"
        },
        new()
        {
            Id = "a3",
            Type = ContentType.Article,
            Signal = "Countries that inverted identity-first rollouts spent twice as much for weaker adoption",
            Title = "Digital public infrastructure and the sequencing problem",
            Topic = "Public Policy",
            Source = "World Bank",
            Url = "https://www.worldbank.org/en/topic/digitaldevelopment",
            WhatHappened = "A comparative look at why identity-first rollouts outperform payments-first ones.",
            AiSummary = "Tells you which rails will exist in three years, and which will not.",
            WhyItMatters = "Identity first, then payments, then consent-based data sharing.",
            KeyInsights = ["Identity first, then payments, then consent-based data sharing.", "Countries that inverted the order spent roughly twice as much for weaker adoption.", "The binding constraint is institutional coordination, not engineering."],
            Tags = ["Policy", "Africa", "Digital Infrastructure"],
            PublishedAt = DateTime.UtcNow.AddDays(-2),
            EstimatedReadTime = "11 min"
        },
        new()
        {
            Id = "p1",
            Type = ContentType.Podcast,
            Signal = "Strong product teams are defined by decision speed, not process quality",
            Title = "Building great product teams",
            Topic = "Product Management",
            Source = "Lenny's Podcast",
            Url = "https://www.lennyspodcast.com/",
            WhatHappened = "A working conversation on how strong product teams actually operate day to day.",
            AiSummary = "The clearest description of the PM job you are working toward.",
            WhyItMatters = "Strong teams are defined by decision speed, not by process quality.",
            KeyInsights = ["Strong teams are defined by decision speed, not by process quality.", "Research that does not reach an engineer has not been done.", "Most 'alignment' problems are unstated disagreement about the goal."],
            WhoShouldListen = "Product managers, founders, team leads",
            Tags = ["Product Management", "Leadership"],
            PublishedAt = DateTime.UtcNow.AddDays(-2),
            EstimatedReadTime = "54 min"
        },
        new()
        {
            Id = "p2",
            Type = ContentType.Podcast,
            Signal = "Network effects need a reason for the first side to show up alone",
            Title = "The economics of network businesses",
            Topic = "Business Strategy",
            Source = "Acquired",
            Url = "https://www.acquired.fm/",
            WhatHappened = "A long-form history of how network effects compound, and the conditions under which they fail to.",
            AiSummary = "This is your market structure. Worth the full two hours.",
            WhyItMatters = "Network effects need a reason for the first side to show up alone.",
            KeyInsights = ["Network effects need a reason for the first side to show up alone.", "Density beats scale in almost every local marketplace.", "Most failed marketplaces solved supply before proving demand."],
            WhoShouldListen = "Founders, strategists, investors",
            Tags = ["Business", "Strategy", "Markets"],
            PublishedAt = DateTime.UtcNow.AddDays(-5),
            EstimatedReadTime = "2h 10m"
        },
        new()
        {
            Id = "v1",
            Type = ContentType.Video,
            Signal = "Generalisation, not accuracy, is what you are actually optimising in ML",
            Title = "Machine Learning Fundamentals — full lecture",
            Topic = "Machine Learning",
            Source = "MIT OpenCourseWare",
            Url = "https://ocw.mit.edu/courses/6-036-introduction-to-machine-learning-fall-2020/",
            WhatHappened = "A complete introductory lecture covering supervised learning, loss functions, and generalisation.",
            AiSummary = "Next item on your roadmap, and the shortest route to real understanding.",
            WhyItMatters = "Generalisation, not accuracy, is the thing you are actually optimising.",
            KeyInsights = ["Generalisation, not accuracy, is the thing you are actually optimising.", "Most model failures are data problems wearing a model costume.", "The bias-variance trade-off explains more production failures than any other idea."],
            Tags = ["Machine Learning", "AI", "Mathematics"],
            PublishedAt = DateTime.UtcNow.AddDays(-7),
            EstimatedWatchTime = "45 min"
        },
        new()
        {
            Id = "r1",
            Type = ContentType.ResearchPaper,
            Signal = "Government-backed digital hubs show 3x higher AI adoption rates among SMEs",
            Title = "AI Adoption Among SMEs in Sub-Saharan Africa",
            Topic = "AI Policy",
            Source = "Journal of African Business",
            Authors = ["Dr. Amara Diallo", "Prof. Chidi Okonkwo"],
            Journal = "Journal of African Business",
            Doi = "10.1080/15228916.2024.001",
            Url = "https://doi.org/10.1080/15228916.2024.001",
            WhatHappened = "Examines the barriers and enablers of AI adoption in SMEs across Sub-Saharan Africa, surveying 500 firms across 12 countries.",
            AiSummary = "Foundational study for anyone researching AI policy or entrepreneurship in Africa.",
            WhyItMatters = "SMEs cite cost and talent shortage as the top two barriers. Government-backed digital hubs show 3x higher adoption rates.",
            KeyInsights = ["SMEs cite cost and talent shortage as the two primary barriers.", "Government-backed digital hubs show 3x higher AI adoption rates.", "Institutional support matters more than technology access alone."],
            Methodology = "Mixed-methods: survey (n=500) + 40 semi-structured interviews",
            Tags = ["AI", "Africa", "Entrepreneurship", "SMEs"],
            PublishedAt = DateTime.UtcNow.AddDays(-30)
        }
    ];
}
