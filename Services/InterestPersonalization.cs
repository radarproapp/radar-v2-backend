using RadarV2.Models;

namespace RadarV2.Services;

/// <summary>
/// Shared, deterministic relevance rules used before optional AI enrichment.
/// This keeps personalization useful when no model/API key is configured.
/// </summary>
public static class InterestPersonalization
{
    public static IReadOnlyList<UserInterestContext> Contexts(UserProfile profile)
    {
        var contexts = profile.InterestContexts
            .Where(c => !string.IsNullOrWhiteSpace(c.Interest))
            .ToList();

        return contexts.Count > 0
            ? contexts
            : profile.Interests.Select(interest => new UserInterestContext
            {
                Interest = interest,
                Goal = profile.PrimaryGoal,
                Level = "Beginner"
            }).ToList();
    }

    public static int ContentScore(ContentItem item, UserProfile profile)
    {
        var text = $"{item.Title} {item.Topic} {item.Signal} {item.AiSummary} {string.Join(' ', item.Tags)}";
        var score = 0;

        foreach (var context in Contexts(profile))
        {
            if (ContainsContext(text, context.Interest)) score += 24;
            if (ContainsContext(text, context.Goal)) score += 12;
            if (ContainsContext(text, context.Lens)) score += 10;

            score += context.Level.ToLowerInvariant() switch
            {
                "beginner" when item.Type is ContentType.Article or ContentType.Video => 5,
                "intermediate" when item.Type is ContentType.Framework or ContentType.Documentation => 5,
                "advanced" when item.Type is ContentType.ResearchPaper or ContentType.PolicyPaper => 5,
                _ => 0
            };
            if (InterestPathService.ActiveInterests(profile).Contains(context.Interest, StringComparer.OrdinalIgnoreCase)) score += 18;
        }

        if (item.PersonaImpact.TryGetValue(profile.Persona.ToString(), out _)) score += 8;
        return score;
    }

    public static int OpportunityScore(Opportunity opportunity, UserProfile profile)
    {
        var text = $"{opportunity.Title} {opportunity.Description} {opportunity.Organisation}";
        var score = 0;

        foreach (var context in Contexts(profile))
        {
            if (ContainsContext(text, context.Interest)) score += 16;
            if (ContainsContext(text, context.Goal)) score += 12;
            if (ContainsContext(text, context.Lens)) score += 8;
            if (InterestPathService.ActiveInterests(profile).Contains(context.Interest, StringComparer.OrdinalIgnoreCase)) score += 14;
        }

        var normalizedGoal = profile.PrimaryGoal.ToLowerInvariant();
        if (normalizedGoal.Contains("intern") && opportunity.Type == OpportunityType.Internship) score += 12;
        if ((normalizedGoal.Contains("startup") || normalizedGoal.Contains("fund")) && opportunity.Type == OpportunityType.StartupAccelerator) score += 12;
        if (profile.Persona == PersonaType.Researcher && opportunity.Type == OpportunityType.ResearchGrant) score += 10;
        if (profile.Persona == PersonaType.Student && opportunity.Type is OpportunityType.Internship or OpportunityType.Scholarship) score += 8;
        if (profile.Persona == PersonaType.Entrepreneur && opportunity.Type is OpportunityType.StartupAccelerator or OpportunityType.Competition) score += 8;
        return score;
    }

    public static string PrimaryContext(UserProfile profile)
    {
        var context = Contexts(profile).OrderByDescending(c => ContentScoreForContext(c, profile)).FirstOrDefault();
        return context is null ? profile.PrimaryGoal : $"{context.Interest} ({context.Level})";
    }

    public static string BuildWhy(UserProfile profile, ContentItem item)
    {
        var path = InterestPathService.Build(profile).FirstOrDefault(p => p.IsPrimary)?.Title
            ?? profile.Interests.FirstOrDefault()
            ?? "your current direction";
        var active = InterestPathService.ActiveInterests(profile);
        var context = Contexts(profile).FirstOrDefault(c => active.Contains(c.Interest, StringComparer.OrdinalIgnoreCase))
            ?? Contexts(profile).FirstOrDefault();
        var level = context?.Level.ToLowerInvariant() ?? "current";
        var goal = string.IsNullOrWhiteSpace(profile.PrimaryGoal) ? "your direction" : profile.PrimaryGoal.ToLowerInvariant();
        var focus = string.IsNullOrWhiteSpace(context?.Lens) ? string.Empty : $", especially your focus on {context.Lens}";
        return $"This matters because it helps you move toward {goal} through {path} at a {level} level{focus}.";
    }

    public static string WhatToKnow(ContentItem item) =>
        item.KeyInsights.FirstOrDefault()
        ?? item.AiSummary
        ?? item.WhatHappened;

    public static string NextMove(ContentItem item) =>
        item.RecommendedActions.FirstOrDefault()
        ?? (!string.IsNullOrWhiteSpace(item.Topic) ? $"Add {item.Topic} to your roadmap and connect it to a current module." : "Read the brief, capture one implication, and decide whether it belongs on your roadmap.");

    private static int ContentScoreForContext(UserInterestContext context, UserProfile profile) =>
        (context.Interest.Equals(profile.Interests.FirstOrDefault(), StringComparison.OrdinalIgnoreCase) ? 2 : 0)
        + (context.Goal.Equals(profile.PrimaryGoal, StringComparison.OrdinalIgnoreCase) ? 1 : 0);

    private static bool ContainsContext(string text, string value) =>
        !string.IsNullOrWhiteSpace(value) && text.Contains(value, StringComparison.OrdinalIgnoreCase);
}
