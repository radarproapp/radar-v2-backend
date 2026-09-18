using MongoDB.Driver;
using RadarV2.Data;
using RadarV2.Data.Documents;
using RadarV2.Models;
using RadarV2.Services.Interfaces;

namespace RadarV2.Services;

public class MongoIntelligenceFeedService : IIntelligenceFeedService
{
    private readonly RadarDatabase _db;

    public MongoIntelligenceFeedService(RadarDatabase db) => _db = db;

    public async Task<List<ContentItem>> GetFeedAsync(UserProfile profile, ContentType? filterType = null, int page = 1, int pageSize = 20)
    {
        await EnsureSeedDataAsync();

        var dismissedIds = !string.IsNullOrEmpty(profile.Id)
            ? await GetDismissedItemIdsAsync(profile.Id)
            : [];

        // Build base filter
        var filter = filterType.HasValue
            ? Builders<ContentItem>.Filter.Eq(i => i.Type, filterType.Value)
            : Builders<ContentItem>.Filter.Empty;
        if (dismissedIds.Count > 0)
            filter &= Builders<ContentItem>.Filter.Nin(i => i.Id, dismissedIds);

        // Narrow to user's interest layers when defined — fall back to full feed if no match
        var interestLayers = MapInterestsToLayers(profile.Interests);
        if (interestLayers.Count > 0)
            filter &= Builders<ContentItem>.Filter.In(i => i.Layer, interestLayers);

        var items = await _db.ContentItems
            .Find(filter)
            .SortByDescending(i => i.PublishedAt)
            .Skip((page - 1) * pageSize)
            .Limit(pageSize)
            .ToListAsync();

        // Fall back to un-filtered page if interests returned nothing
        if (items.Count == 0 && interestLayers.Count > 0)
        {
            var fallbackFilter = filterType.HasValue
                ? Builders<ContentItem>.Filter.Eq(i => i.Type, filterType.Value)
                : Builders<ContentItem>.Filter.Empty;
            if (dismissedIds.Count > 0)
                fallbackFilter &= Builders<ContentItem>.Filter.Nin(i => i.Id, dismissedIds);
            items = await _db.ContentItems
                .Find(fallbackFilter)
                .SortByDescending(i => i.PublishedAt)
                .Skip((page - 1) * pageSize)
                .Limit(pageSize)
                .ToListAsync();
        }

        if (!string.IsNullOrEmpty(profile.Id))
        {
            var savedIds = await GetSavedItemIdsAsync(profile.Id);
            foreach (var item in items)
                item.IsSaved = savedIds.Contains(item.Id);
        }

        return items;
    }

    // Maps onboarding interest strings → ContentLayer values for personalised feed filtering.
    // Interests come from the onboarding select list; the mapping is intentionally broad so
    // users aren't locked out of related content.
    private static List<ContentLayer> MapInterestsToLayers(List<string> interests)
    {
        if (interests.Count == 0) return [];

        var layers = new HashSet<ContentLayer>();
        foreach (var interest in interests)
        {
            var norm = interest.Trim().ToLowerInvariant();
            var mapped = norm switch
            {
                var s when s.Contains("tech") || s.Contains("ai") || s.Contains("software")
                    => [ContentLayer.Ideas, ContentLayer.Science],
                var s when s.Contains("business") || s.Contains("entrepreneur") || s.Contains("startup")
                    => [ContentLayer.Ideas, ContentLayer.Finance, ContentLayer.Career],
                var s when s.Contains("finance") || s.Contains("invest") || s.Contains("money") || s.Contains("banking")
                    => [ContentLayer.Finance],
                var s when s.Contains("policy") || s.Contains("governance") || s.Contains("politi")
                    => [ContentLayer.Policy],
                var s when s.Contains("health") || s.Contains("medicine") || s.Contains("medical")
                    => [ContentLayer.Medicine],
                var s when s.Contains("climate") || s.Contains("environment") || s.Contains("green")
                    => [ContentLayer.Environment],
                var s when s.Contains("science") || s.Contains("research") || s.Contains("academic")
                    => [ContentLayer.Science, ContentLayer.Academic],
                var s when s.Contains("sport") || s.Contains("football") || s.Contains("soccer")
                    => [ContentLayer.Sports],
                var s when s.Contains("music") || s.Contains("afrobeat")
                    => [ContentLayer.Music],
                var s when s.Contains("film") || s.Contains("movie") || s.Contains("cinema") || s.Contains("tv")
                    => [ContentLayer.Film],
                var s when s.Contains("educat") || s.Contains("learn") || s.Contains("school") || s.Contains("university")
                    => [ContentLayer.Education, ContentLayer.Learning],
                var s when s.Contains("fashion") || s.Contains("style") || s.Contains("beauty")
                    => [ContentLayer.Fashion],
                var s when s.Contains("travel") || s.Contains("tourism")
                    => [ContentLayer.Travel],
                var s when s.Contains("faith") || s.Contains("religion") || s.Contains("spiritual")
                    => [ContentLayer.Faith],
                var s when s.Contains("philosophy") || s.Contains("ethics")
                    => [ContentLayer.Philosophy],
                var s when s.Contains("law") || s.Contains("legal")
                    => [ContentLayer.Law],
                var s when s.Contains("real estate") || s.Contains("property") || s.Contains("housing")
                    => [ContentLayer.RealEstate],
                var s when s.Contains("energy") || s.Contains("oil") || s.Contains("power") || s.Contains("gas")
                    => [ContentLayer.Energy],
                var s when s.Contains("agriculture") || s.Contains("farming") || s.Contains("food")
                    => [ContentLayer.Agriculture],
                var s when s.Contains("industry") || s.Contains("mining") || s.Contains("manufacturing")
                    => [ContentLayer.Industry],
                var s when s.Contains("career") || s.Contains("job") || s.Contains("work")
                    => [ContentLayer.Career],
                var s when s.Contains("art") || s.Contains("craft") || s.Contains("design")
                    => [ContentLayer.Art],
                var s when s.Contains("history")
                    => [ContentLayer.History],
                var s when s.Contains("gaming") || s.Contains("esport") || s.Contains("game")
                    => [ContentLayer.Gaming],
                var s when s.Contains("transport") || s.Contains("mobility") || s.Contains("logistic")
                    => [ContentLayer.Transportation],
                var s when s.Contains("book") || s.Contains("literature") || s.Contains("reading")
                    => [ContentLayer.Literature],
                var s when s.Contains("lifestyle")
                    => [ContentLayer.Lifestyle],
                _ => Array.Empty<ContentLayer>()
            };
            foreach (var l in mapped) layers.Add(l);
        }
        return [.. layers];
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
