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
        var layers = InterestLayerMapper.MapInterestsToLayers(profile.Interests);

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

}
