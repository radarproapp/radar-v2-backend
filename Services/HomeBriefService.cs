using RadarV2.Models;
using RadarV2.Services.Interfaces;

namespace RadarV2.Services;

/// <summary>
/// Composes the home screen from services that already exist, adding no new ingestion or AI of its
/// own. Deliberately synchronous-fast: everything here is a Mongo read plus in-memory scoring, so
/// the home screen never waits on a provider round trip (see the cache-first pattern in
/// AiRelevanceService for how AI enrichment reaches these surfaces instead).
/// </summary>
public class HomeBriefService : IHomeBriefService
{
    private readonly IIntelligenceFeedService _feed;
    private readonly IOpportunityService _opportunities;
    private readonly IRoadmapService _roadmaps;
    private readonly IBehavioralSignalService _signals;

    public HomeBriefService(
        IIntelligenceFeedService feed,
        IOpportunityService opportunities,
        IRoadmapService roadmaps,
        IBehavioralSignalService signals)
    {
        _feed = feed;
        _opportunities = opportunities;
        _roadmaps = roadmaps;
        _signals = signals;
    }

    public async Task<HomeBrief> BuildAsync(UserProfile profile, CancellationToken ct = default)
    {
        var brief = new HomeBrief
        {
            Context = BuildContext(profile),
            Stats = profile.Stats,
        };

        // Pull a wider window than we show: ranking is per-user and in memory, so the top three
        // only mean something if there were candidates to rank.
        var candidates = await _feed.GetFeedAsync(profile, null, 1, 24);
        brief.TopSignals = candidates
            .Select(item => new { item, explanation = InterestPersonalization.Explain(profile, item) })
            .OrderByDescending(x => x.explanation.Score)
            .Take(3)
            .Select(x => new HomeSignal
            {
                ContentItemId = x.item.Id,
                Signal = !string.IsNullOrWhiteSpace(x.item.Signal) ? x.item.Signal : x.item.Title,
                Source = x.item.Source,
                Type = x.item.Type.ToString(),
                Layer = x.item.Layer,
                Topic = x.item.Topic,
                ThumbnailUrl = x.item.ThumbnailUrl,
                PublishedAt = x.item.PublishedAt,
                WhyYoureSeeingThis = WhyShown.Build(x.item, x.explanation.MatchedSignals),
            })
            .ToList();

        brief.Noticed = await BuildNoticedAsync(profile);

        var roadmap = await _roadmaps.GetActiveRoadmapAsync(profile.Id);
        brief.TodaysFocus = BuildFocus(profile, roadmap);
        brief.NextBuild = BuildNextBuild(profile, roadmap);

        var opportunities = await _opportunities.GetOpportunitiesAsync(profile, null, 1, 3);
        brief.Opportunities = opportunities.Select(o => new HomeOpportunity
        {
            Id = o.Id,
            Title = o.Title,
            Organisation = o.Organisation,
            Location = o.Location,
            MatchScorePercent = o.MatchScorePercent,
            MatchTier = o.MatchTier,
            DaysUntilDeadline = o.DaysUntilDeadline == int.MaxValue ? null : o.DaysUntilDeadline,
            WhyItMatches = o.WhyItFits ?? string.Empty,
            ReminderSet = o.ReminderSet,
            UrgencyLabel = o.UrgencyLabel,
        }).ToList();

        brief.ClosingSoon = await BuildClosingSoonAsync(profile);

        return brief;
    }

    /// <summary>
    /// What the user asked to be reminded about, that is actually about to close.
    ///
    /// Closed ones are left out: a reminder for something that shut last week is not a reminder,
    /// it is a reproach. Anything without a deadline is left out too - there is nothing to be
    /// early for.
    /// </summary>
    private async Task<List<HomeOpportunity>> BuildClosingSoonAsync(UserProfile profile)
    {
        var tracked = await _opportunities.GetTrackedPipelineAsync(profile.Id);

        return tracked
            .Where(o => o.ReminderSet && o.Deadline.HasValue && !o.HasClosed)
            .Where(o => o.DaysUntilDeadline <= ReminderWindowDays)
            .OrderBy(o => o.DaysUntilDeadline)
            .Select(o => new HomeOpportunity
            {
                Id = o.Id,
                Title = o.Title,
                Organisation = o.Organisation,
                Location = o.Location,
                MatchScorePercent = o.MatchScorePercent,
                MatchTier = o.MatchTier,
                DaysUntilDeadline = o.DaysUntilDeadline,
                WhyItMatches = o.WhyItFits ?? string.Empty,
                ReminderSet = true,
                UrgencyLabel = o.UrgencyLabel,
            })
            .ToList();
    }

    /// <summary>
    /// How far ahead a reminder starts showing. Two weeks is enough to gather documents and ask
    /// for a reference, which is what most of these actually require.
    /// </summary>
    private const int ReminderWindowDays = 14;

    private static string BuildContext(UserProfile profile)
    {
        var parts = new List<string>();
        var role = !string.IsNullOrWhiteSpace(profile.TargetRole) ? profile.TargetRole : profile.CurrentIntent;
        if (!string.IsNullOrWhiteSpace(role)) parts.Add(role);
        if (!string.IsNullOrWhiteSpace(profile.PrimaryGoal)) parts.Add(profile.PrimaryGoal);

        var place = !string.IsNullOrWhiteSpace(profile.City) ? profile.City : profile.Region;
        if (!string.IsNullOrWhiteSpace(place)) parts.Add(place);

        return string.Join(" · ", parts);
    }

    /// <summary>
    /// The one-line reason an item is on the home screen. Uses the matched dimensions the scorer
    /// actually fired on, so it names the user's own words back to them ("Cybersecurity", "SOC
    /// Analyst") rather than asserting a connection nothing computed.
    /// </summary>
    /// <summary>
    /// Spec item 2: the top things the user has actually been interacting with. Inferred signals
    /// only — declared interests are what they told us at onboarding, not what Radar noticed.
    /// </summary>
    private async Task<List<NoticedSignal>> BuildNoticedAsync(UserProfile profile)
    {
        var signals = await _signals.GetTopSignalsAsync(profile.Id, 12);

        return signals
            .Where(s => s.Source == SignalSource.Inferred && s.PositiveCount > 0)
            .OrderByDescending(BehavioralSignalService.DecayedStrength)
            .Take(3)
            .Select(s => new NoticedSignal
            {
                Term = s.Term,
                TimesEngaged = s.PositiveCount,
                Suggestion = $"You keep coming back to {s.Term}. Want it on your roadmap?",
                ActionUrl = $"/feed?q={Uri.EscapeDataString(s.Term)}",
            })
            .ToList();
    }

    /// <summary>
    /// Spec item 4: name the actual topic. "Continue learning path" was the complaint — this
    /// returns the next incomplete lesson in the current stage, or null when there is no roadmap
    /// to draw one from.
    /// </summary>
    private static HomeFocus? BuildFocus(UserProfile profile, GrowthRoadmap? roadmap)
    {
        if (roadmap is null) return null;

        var stageModule = roadmap.Modules
            .Where(m => string.Equals(m.Level, roadmap.CurrentStage, StringComparison.OrdinalIgnoreCase))
            .OrderBy(m => m.Order)
            .FirstOrDefault(m => !m.IsCompleted)
            ?? roadmap.Modules.OrderBy(m => m.Order).FirstOrDefault(m => !m.IsCompleted);

        if (stageModule is null) return null;

        var lesson = stageModule.Lessons.OrderBy(l => l.Order).FirstOrDefault(l => !l.IsCompleted);
        var topic = lesson?.Title ?? stageModule.Title;
        var goal = !string.IsNullOrWhiteSpace(roadmap.Goal) ? roadmap.Goal : profile.PrimaryGoal;

        return new HomeFocus
        {
            Topic = topic,
            Stage = stageModule.Level,
            Why = string.IsNullOrWhiteSpace(goal)
                ? $"Next step in {stageModule.Title}."
                : $"This is the next step toward {goal}.",
            EstimatedTime = lesson?.EstimatedTime,
        };
    }

    /// <summary>
    /// Spec item 6: name the artifact. Built from the user's own capability and current topic so
    /// a SOC analyst is told to write a detection rule, not to "build something you can show".
    /// </summary>
    /// <summary>Null for anything blank, so a coalescing chain can skip an empty string.</summary>
    private static string? Firm(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static HomeBuild? BuildNextBuild(UserProfile profile, GrowthRoadmap? roadmap)
    {
        // Firm, not ??. TargetRole is a non-nullable string defaulting to "", so `?? ` never
        // falls past it: the chain always stopped at the empty string and the whole section
        // vanished. It was hidden while paths were auto-created, because roadmap?.Goal answered
        // first; once a path legitimately starts empty, every user lost "next build".
        var topic = Firm(roadmap?.Goal)
            ?? Firm(profile.TargetRole)
            ?? Firm(profile.Interests.FirstOrDefault())
            ?? Firm(profile.PrimaryGoal);

        if (topic is null) return null;

        var capability = Firm(profile.Capabilities.FirstOrDefault())
            ?? Firm(profile.TargetRole)
            ?? Firm(profile.Interests.FirstOrDefault())
            ?? topic;

        return new HomeBuild
        {
            Artifact = InterestPersonalization.PortfolioArtifactFor(capability, topic),
            Why = $"Evidence beats a claim. This is the piece that shows you can do {topic}, not just that you studied it.",
        };
    }
}
