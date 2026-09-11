using MongoDB.Driver;
using RadarV2.Data;
using RadarV2.Models;
using RadarV2.Services.Interfaces;

namespace RadarV2.Services;

public class MongoNavigatorService : INavigatorService
{
    private readonly RadarDatabase _db;
    private readonly IOpportunityService _opportunities;

    // Per-user per-day cache so page refreshes don't re-query MongoDB
    private static readonly Dictionary<string, NavigatorFocus> _cache = new();

    public MongoNavigatorService(RadarDatabase db, IOpportunityService opportunities)
    {
        _db           = db;
        _opportunities = opportunities;
    }

    public async Task<NavigatorFocus> GetTodaysFocusAsync(UserProfile profile)
    {
        var key = $"{profile.Id}:{DateTime.UtcNow:yyyy-MM-dd}";
        if (_cache.TryGetValue(key, out var hit)) return hit;

        var focus = await BuildFocusAsync(profile);
        _cache[key] = focus;
        return focus;
    }

    public Task<NavigatorFocus> RefreshAsync(UserProfile profile)
    {
        var key = $"{profile.Id}:{DateTime.UtcNow:yyyy-MM-dd}";
        _cache.Remove(key);
        return GetTodaysFocusAsync(profile);
    }

    // ── Build ────────────────────────────────────────────────────────────────

    private async Task<NavigatorFocus> BuildFocusAsync(UserProfile profile)
    {
        var layers = MapInterestsToLayers(profile.Interests);

        var readTask   = PickBestItemAsync(layers, ContentType.Article, ContentType.ResearchPaper);
        var watchTask  = PickBestItemAsync(layers, ContentType.Video);
        var listenTask = PickBestItemAsync(layers, ContentType.Podcast);
        var oppTask    = _opportunities.GetOpportunitiesAsync(profile, null, 1, 1);

        await Task.WhenAll(readTask, watchTask, listenTask, oppTask);

        var readItem  = readTask.Result;
        var watchItem = watchTask.Result;
        var listenItem = listenTask.Result;
        var topOpp    = oppTask.Result.FirstOrDefault();

        return new NavigatorFocus
        {
            Learn  = BuildLearnCard(profile),
            Read   = readItem   is not null ? ToReadCard(readItem)    : DefaultReadCard(),
            Watch  = watchItem  is not null ? ToWatchCard(watchItem)  : DefaultWatchCard(),
            Listen = listenItem is not null ? ToListenCard(listenItem) : DefaultListenCard(),
            Apply  = topOpp     is not null ? ToApplyCard(topOpp)     : DefaultApplyCard(),
            Build  = BuildBuildCard(profile),
        };
    }

    // ── Content item selection ────────────────────────────────────────────────

    private async Task<ContentItem?> PickBestItemAsync(List<ContentLayer> layers, params ContentType[] types)
    {
        var typeFilter = Builders<ContentItem>.Filter.In(i => i.Type, types);

        // Prefer items in the user's interest layers, then fall back to anything
        if (layers.Count > 0)
        {
            var layerFilter = Builders<ContentItem>.Filter.In(i => i.Layer, layers);
            var item = await _db.ContentItems
                .Find(typeFilter & layerFilter)
                .SortBy(i => i.CredibilityTier)
                .ThenByDescending(i => i.PublishedAt)
                .Limit(1)
                .FirstOrDefaultAsync();
            if (item is not null) return item;
        }

        return await _db.ContentItems
            .Find(typeFilter)
            .SortBy(i => i.CredibilityTier)
            .ThenByDescending(i => i.PublishedAt)
            .Limit(1)
            .FirstOrDefaultAsync();
    }

    // ── Card builders ────────────────────────────────────────────────────────

    private static NavigatorCard ToReadCard(ContentItem item) => new()
    {
        CardType      = NavigatorCardType.Read,
        ContentItemId = item.Id,
        Title         = item.Title,
        Subtitle      = item.Source,
        WhyItMatters  = !string.IsNullOrWhiteSpace(item.WhyItMatters) ? item.WhyItMatters : item.Signal,
        AiSummary     = item.AiSummary,
    };

    private static NavigatorCard ToWatchCard(ContentItem item) => new()
    {
        CardType      = NavigatorCardType.Watch,
        ContentItemId = item.Id,
        Title         = item.Title,
        Subtitle      = item.Source,
        EstimatedTime = item.EstimatedWatchTime ?? item.EstimatedReadTime,
        AiSummary     = item.AiSummary,
    };

    private static NavigatorCard ToListenCard(ContentItem item) => new()
    {
        CardType      = NavigatorCardType.Listen,
        ContentItemId = item.Id,
        Title         = item.Title,
        Subtitle      = item.Source,
        EstimatedTime = item.EstimatedReadTime,
        AiSummary     = !string.IsNullOrWhiteSpace(item.AiSummary) ? item.AiSummary : item.WhoShouldListen,
    };

    private static NavigatorCard ToApplyCard(Opportunity opp) => new()
    {
        CardType         = NavigatorCardType.Apply,
        OpportunityId    = opp.Id,
        Title            = opp.Title,
        Subtitle         = opp.Organisation,
        MatchScorePercent = opp.MatchScorePercent,
        DeadlineDays     = opp.DaysUntilDeadline == int.MaxValue ? null : (int?)opp.DaysUntilDeadline,
        ActionUrl        = opp.Url,
    };

    private static NavigatorCard BuildLearnCard(UserProfile profile) => new()
    {
        CardType      = NavigatorCardType.Learn,
        Title         = LearnTitle(profile.PrimaryGoal),
        Subtitle      = "Your Learning Roadmap",
        EstimatedTime = "20 minutes",
    };

    private static NavigatorCard BuildBuildCard(UserProfile profile) => new()
    {
        CardType = NavigatorCardType.Build,
        Title    = BuildTitle(profile.PrimaryGoal),
        Subtitle = "Project Studio",
    };

    // ── Fallback cards (no ingested data yet) ────────────────────────────────

    private static NavigatorCard DefaultReadCard() => new()
    {
        CardType     = NavigatorCardType.Read,
        Title        = "Your intelligence feed is filling up",
        Subtitle     = "Radar",
        WhyItMatters = "Content ingestion is running in the background. Check back in a few hours for personalised signals.",
        AiSummary    = "Configure your API keys to start receiving live signals.",
    };

    private static NavigatorCard DefaultWatchCard() => new()
    {
        CardType  = NavigatorCardType.Watch,
        Title     = "Video content will appear here",
        Subtitle  = "Coming soon",
        AiSummary = "Videos are sourced from your interest areas once ingestion runs.",
    };

    private static NavigatorCard DefaultListenCard() => new()
    {
        CardType  = NavigatorCardType.Listen,
        Title     = "Podcasts will appear here",
        Subtitle  = "Coming soon",
        AiSummary = "Podcast episodes are pulled from PodcastIndex and Taddy once credentials are set.",
    };

    private static NavigatorCard DefaultApplyCard() => new()
    {
        CardType          = NavigatorCardType.Apply,
        Title             = "Opportunities matched to your profile",
        Subtitle          = "Radar Opportunities",
        MatchScorePercent = 70,
        DeadlineDays      = 30,
    };

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static string LearnTitle(string goal) => goal switch
    {
        var g when g.Contains("Product",       StringComparison.OrdinalIgnoreCase) => "Continue: Product Discovery Module",
        var g when g.Contains("Data",          StringComparison.OrdinalIgnoreCase) => "Continue: Data Analysis Fundamentals",
        var g when g.Contains("Entrepreneur",  StringComparison.OrdinalIgnoreCase) => "Continue: Business Model Design",
        var g when g.Contains("Finance",       StringComparison.OrdinalIgnoreCase) => "Continue: Financial Modelling",
        var g when g.Contains("Engineer",      StringComparison.OrdinalIgnoreCase) => "Continue: Systems Design",
        _ => "Continue your learning path"
    };

    private static string BuildTitle(string goal) => goal switch
    {
        var g when g.Contains("Product",       StringComparison.OrdinalIgnoreCase) => "Finish your customer interview template",
        var g when g.Contains("Data",          StringComparison.OrdinalIgnoreCase) => "Build a mini data dashboard",
        var g when g.Contains("Entrepreneur",  StringComparison.OrdinalIgnoreCase) => "Draft your one-page business model",
        var g when g.Contains("Finance",       StringComparison.OrdinalIgnoreCase) => "Build a personal budget model in Excel",
        var g when g.Contains("Engineer",      StringComparison.OrdinalIgnoreCase) => "Ship a micro-project to GitHub",
        _ => "Add one thing to your portfolio this week"
    };

    // Mirror of MongoIntelligenceFeedService.MapInterestsToLayers — kept in sync manually.
    private static List<ContentLayer> MapInterestsToLayers(List<string> interests)
    {
        if (interests.Count == 0) return [];
        var layers = new HashSet<ContentLayer>();
        foreach (var interest in interests)
        {
            var norm = interest.Trim().ToLowerInvariant();
            ContentLayer[] mapped = norm switch
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
                _ => []
            };
            foreach (var l in mapped) layers.Add(l);
        }
        return [.. layers];
    }
}
