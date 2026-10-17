using System.Text;
using RadarV2.Models;

namespace RadarV2.Services;

/// <summary>
/// Deterministic relevance rules that run before (and as a fallback to) optional AI enrichment, so
/// personalization stays useful with no model/API key configured.
///
/// Ranking follows the product's priority hierarchy:
///   Goal (30) &gt; Problem (20) &gt; Intent (15) &gt; Capability (15) &gt; Strong interest (10)
///   &gt; General interest (5) &gt; Context (5)
/// Each dimension contributes at most its weight, scaled by how much of the phrase matched, so a
/// single keyword cannot dominate and multiple weak signals still add up.
/// </summary>
public static class InterestPersonalization
{
    public sealed record DimensionMatch(string Dimension, string Phrase, double Coverage);
    public sealed record Explanation(int Score, string Why, string YourEdge, string NextMove, IReadOnlyList<string> MatchedSignals);

    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "and", "for", "with", "your", "you", "a", "an", "to", "of", "in", "on", "my",
        "into", "from", "that", "this", "are", "is", "be", "as", "at", "by", "or", "it", "we",
        "will", "can", "want", "trying", "about", "their", "our", "more", "most", "how", "what",
    };

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

    /// <summary>Full deterministic explanation for one item: score, "why", next move and matched signals.</summary>
    public static Explanation Explain(UserProfile profile, ContentItem item)
    {
        var text = Text(item);
        var matches = new List<DimensionMatch>();

        AddDimension(matches, text, "Goal", GoalPhrases(profile), 30);
        AddDimension(matches, text, "Problem", profile.Problems, 20);
        AddDimension(matches, text, "Intent", IntentPhrases(profile), 15);
        AddDimension(matches, text, "Capability", profile.Capabilities, 15);
        AddDimension(matches, text, "Interest", StrongInterests(profile), 10);
        AddDimension(matches, text, "Interest", GeneralInterests(profile), 5);
        AddDimension(matches, text, "Context", ContextPhrases(profile), 5);

        if (item.PersonaImpact.ContainsKey(profile.Persona.ToString()))
            matches.Add(new DimensionMatch("Persona", profile.Persona.ToString(), 1));

        var score = (int)Math.Round(matches.Where(m => m.Dimension != "Persona").Sum(m => m.Coverage * WeightFor(m.Dimension)))
                    + (matches.Any(m => m.Dimension == "Persona") ? 5 : 0);

        var signals = matches
            .Where(m => m.Dimension != "Persona")
            .OrderByDescending(m => WeightFor(m.Dimension) * m.Coverage)
            .Take(3)
            .Select(m => $"{m.Dimension}: {m.Phrase}")
            .ToList();

        var why = BuildWhy(profile, item, matches);
        var edge = BuildYourEdge(profile, item, matches);
        var nextMove = BuildNextMove(profile, item, matches);

        return new Explanation(score, why, edge, nextMove, signals);
    }

    public static int ContentScore(ContentItem item, UserProfile profile) => Explain(profile, item).Score;

    public static string BuildWhy(UserProfile profile, ContentItem item) => Explain(profile, item).Why;

    public static string BuildYourEdge(UserProfile profile, ContentItem item) => Explain(profile, item).YourEdge;

    public static string NextMove(UserProfile profile, ContentItem item) => Explain(profile, item).NextMove;

    /// <summary>Kept for callers without a profile in scope.</summary>
    public static string NextMove(ContentItem item) =>
        item.RecommendedActions.FirstOrDefault()
        ?? (!string.IsNullOrWhiteSpace(item.Topic)
            ? $"Read the full analysis, then add {item.Topic} to your roadmap and connect it to a current module."
            : "Read the full analysis, capture one implication, and decide whether it belongs on your roadmap.");

    public static string WhatToKnow(ContentItem item) =>
        item.KeyInsights.FirstOrDefault()
        ?? item.AiSummary
        ?? item.WhatHappened;

    public static int OpportunityScore(Opportunity opportunity, UserProfile profile)
    {
        var text = $"{opportunity.Title} {opportunity.Description} {opportunity.Organisation} {opportunity.Location}";
        var score = 0;

        foreach (var context in Contexts(profile))
        {
            if (ContainsContext(text, context.Interest)) score += 16;
            if (ContainsContext(text, context.Goal)) score += 12;
            if (ContainsContext(text, context.Lens)) score += 8;
            if (InterestPathService.ActiveInterests(profile).Contains(context.Interest, StringComparer.OrdinalIgnoreCase)) score += 14;
        }

        foreach (var phrase in new[] { profile.PrimaryGoal }.Concat(profile.Capabilities))
            if (ContainsContext(text, phrase)) score += 10;

        if (profile.Geography.Count > 0 && profile.Geography.Any(g => ContainsContext(text, g))) score += 8;

        var normalizedGoal = profile.PrimaryGoal.ToLowerInvariant();
        if (normalizedGoal.Contains("intern") && opportunity.Type == OpportunityType.Internship) score += 12;
        if ((normalizedGoal.Contains("startup") || normalizedGoal.Contains("fund")) && opportunity.Type == OpportunityType.StartupAccelerator) score += 12;
        if (profile.Persona == PersonaType.Researcher && opportunity.Type == OpportunityType.ResearchGrant) score += 10;
        if (profile.Persona == PersonaType.Student && opportunity.Type is OpportunityType.Internship or OpportunityType.Scholarship) score += 8;
        if (profile.Persona == PersonaType.Entrepreneur && opportunity.Type is OpportunityType.StartupAccelerator or OpportunityType.Competition) score += 8;

        if (profile.OpportunityPreferences.Count > 0 &&
            profile.OpportunityPreferences.Any(p => p.Equals(opportunity.Type.ToString(), StringComparison.OrdinalIgnoreCase)))
            score += 10;

        return score;
    }

    public static string PrimaryContext(UserProfile profile)
    {
        var strong = StrongInterests(profile).FirstOrDefault();
        var interest = strong ?? profile.Interests.FirstOrDefault();
        return string.IsNullOrWhiteSpace(interest) ? profile.PrimaryGoal : interest;
    }

    // ── internals ────────────────────────────────────────────────────────────

    private static string BuildWhy(UserProfile profile, ContentItem item, List<DimensionMatch> matches)
    {
        var goal = matches.FirstOrDefault(m => m.Dimension == "Goal")?.Phrase ?? profile.PrimaryGoal;
        var capability = matches.FirstOrDefault(m => m.Dimension == "Capability")?.Phrase ?? profile.Capabilities.FirstOrDefault();
        var interest = matches.FirstOrDefault(m => m.Dimension == "Interest")?.Phrase ?? profile.Interests.FirstOrDefault() ?? "your domain";
        var problem = matches.FirstOrDefault(m => m.Dimension == "Problem")?.Phrase ?? profile.Problems.FirstOrDefault();
        var location = !string.IsNullOrWhiteSpace(profile.City)
            ? $"{profile.City}, {profile.Region}"
            : (!string.IsNullOrWhiteSpace(profile.Region) ? profile.Region : "your target market");
        var target = !string.IsNullOrWhiteSpace(profile.TargetRole)
            ? profile.TargetRole
            : (!string.IsNullOrWhiteSpace(profile.TargetIndustry) ? $"{profile.TargetIndustry} roles" : "technology-focused roles");
        var topic = !string.IsNullOrWhiteSpace(item.Topic) ? item.Topic : item.Title;

        var sb = new StringBuilder();
        sb.Append($"This matters to you because your focus sits directly at the intersection of {Lower(interest)} and your goal to {Lower(goal)}. ");
        sb.Append($"In {location}, developments in {Lower(topic)} create demand for professionals who understand both technical execution and business applications, particularly for {Lower(target)}. ");

        if (!string.IsNullOrWhiteSpace(problem))
        {
            sb.Append($"Given the challenge you flagged regarding {Lower(problem)}, understanding this development provides concrete foresight into how organizations and industry leaders are resolving similar hurdles. ");
        }

        if (!string.IsNullOrWhiteSpace(capability))
        {
            sb.Append($"A skill that becomes significantly more valuable here is applying {Lower(capability)} to solve operational bottlenecks in {Lower(topic)}. ");
        }
        else
        {
            sb.Append($"This makes your current learning path more urgent, particularly as practical expertise in {Lower(topic)} becomes a key market differentiator. ");
        }

        sb.Append("Rather than general industry commentary, this directly impacts how you prioritize your learning modules and career positioning.");
        return sb.ToString().Trim();
    }

    private static string BuildYourEdge(UserProfile profile, ContentItem item, List<DimensionMatch> matches)
    {
        var topic = !string.IsNullOrWhiteSpace(item.Topic) ? item.Topic : item.Title;
        var capability = matches.FirstOrDefault(m => m.Dimension == "Capability")?.Phrase ?? profile.Capabilities.FirstOrDefault() ?? "market research";
        var interest = matches.FirstOrDefault(m => m.Dimension == "Interest")?.Phrase ?? profile.Interests.FirstOrDefault() ?? "this domain";
        var location = !string.IsNullOrWhiteSpace(profile.Region) ? profile.Region : (!string.IsNullOrWhiteSpace(profile.City) ? profile.City : "regional");
        var target = !string.IsNullOrWhiteSpace(profile.TargetRole) ? profile.TargetRole : (!string.IsNullOrWhiteSpace(profile.TargetIndustry) ? profile.TargetIndustry : "target employers and clients");
        var artifact = GetSpecificPortfolioArtifact(capability, topic);

        var sb = new StringBuilder();
        sb.Append($"Your edge is not simply knowing that {Lower(topic)} is evolving. ");
        sb.Append($"You can use this trend to build {artifact}: track how companies and institutions across {location} are responding, identify the business functions being automated or restructured, and turn your findings into a high-signal work sample. ");
        sb.Append($"This demonstrates both applied {Lower(capability)} and {Lower(interest)} capabilities to {Lower(target)}. ");
        sb.Append($"A market worth watching is the emergence of specialized {Lower(topic)} workflows tailored for {location} infrastructure constraints.");

        return sb.ToString().Trim();
    }

    private static string BuildNextMove(UserProfile profile, ContentItem item, List<DimensionMatch> matches)
    {
        var topic = !string.IsNullOrWhiteSpace(item.Topic) ? item.Topic : item.Title;
        var capability = matches.FirstOrDefault(m => m.Dimension == "Capability")?.Phrase ?? profile.Capabilities.FirstOrDefault() ?? "applied problem-solving";
        var location = !string.IsNullOrWhiteSpace(profile.Region) ? profile.Region : (!string.IsNullOrWhiteSpace(profile.City) ? profile.City : "your target market");
        var artifact = GetSpecificPortfolioArtifact(capability, topic);
        var sourceName = !string.IsNullOrWhiteSpace(item.Source) ? item.Source : "the official release";

        var lines = new[]
        {
            $"1. Read the core analysis or documentation from {sourceName} and note the key technical and market implications.",
            $"2. Identify 3 organizations or market players in {location} directly affected by this shift.",
            $"3. Spend 30 minutes researching how they are adapting their technical architecture or commercial strategy.",
            $"4. Build {artifact} and add your findings to your professional portfolio.",
            $"5. Add {topic} to your active roadmap and revisit this topic in seven days to track new moves."
        };

        return string.Join("\n", lines);
    }

    private static string GetSpecificPortfolioArtifact(string capability, string topic)
    {
        var text = $"{capability} {topic}".ToLowerInvariant();

        if (text.Contains("model") || text.Contains("valuation") || text.Contains("dcf") || text.Contains("finance"))
            return "a 3-statement financial forecast model analyzing this market case";

        if (text.Contains("product") || text.Contains("prd") || text.Contains("user story") || text.Contains("feature"))
            return "a 1-page PRD and user journey map addressing this problem";

        if (text.Contains("ai") || text.Contains("prompt") || text.Contains("rag") || text.Contains("llm") || text.Contains("machine learning"))
            return "a working prototype or evaluation script testing this architecture";

        if (text.Contains("sql") || text.Contains("data") || text.Contains("metric") || text.Contains("dashboard") || text.Contains("analytic"))
            return "an interactive metrics dashboard or SQL query notebook exploring this dataset";

        if (text.Contains("api") || text.Contains("backend") || text.Contains("system") || text.Contains("software") || text.Contains("engineer") || text.Contains("code"))
            return "a public GitHub repository implementing this architecture pattern with tests";

        if (text.Contains("invest") || text.Contains("vc") || text.Contains("venture") || text.Contains("pitch") || text.Contains("deck"))
            return "a 2-page investment memo breaking down the unit economics and market risks";

        if (text.Contains("policy") || text.Contains("sandbox") || text.Contains("govern") || text.Contains("regulat"))
            return "a structured policy brief evaluating the regulatory and compliance trade-offs";

        if (text.Contains("strategy") || text.Contains("market") || text.Contains("business") || text.Contains("growth"))
            return "a strategic teardown and competitive positioning matrix";

        return $"a documented case study applying {Lower(capability)} to solve a problem in {Lower(topic)}";
    }

    private static double WeightFor(string dimension) => dimension switch
    {
        "Goal" => 30,
        "Problem" => 20,
        "Intent" => 15,
        "Capability" => 15,
        "Interest" => 10,
        "Context" => 5,
        _ => 0,
    };

    private static void AddDimension(List<DimensionMatch> matches, string text, string dimension, IEnumerable<string> phrases, double weight)
    {
        var bestCoverage = 0.0;
        string? bestPhrase = null;
        foreach (var phrase in phrases.Where(p => !string.IsNullOrWhiteSpace(p)))
        {
            var coverage = Coverage(text, phrase);
            if (coverage > bestCoverage)
            {
                bestCoverage = coverage;
                bestPhrase = phrase;
            }
        }
        if (bestPhrase is not null && bestCoverage > 0)
            matches.Add(new DimensionMatch(dimension, bestPhrase, Math.Min(1, bestCoverage)));
    }

    private static double Coverage(string text, string phrase)
    {
        if (text.Contains(phrase, StringComparison.OrdinalIgnoreCase)) return 1;

        var keywords = Keywords(phrase);
        if (keywords.Count == 0) return 0;
        var hits = keywords.Count(k => text.Contains(k, StringComparison.OrdinalIgnoreCase));
        return (double)hits / keywords.Count;
    }

    private static List<string> Keywords(string phrase) =>
        phrase.Split([' ', ',', '/', '-', '(', ')', '&'], StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.Trim().TrimEnd('.').ToLowerInvariant())
            .Where(w => w.Length >= 4 && !StopWords.Contains(w))
            .Distinct()
            .ToList();

    private static IEnumerable<string> GoalPhrases(UserProfile profile) =>
        new[] { profile.PrimaryGoal }.Concat(Contexts(profile).Select(c => c.Goal));

    private static IEnumerable<string> IntentPhrases(UserProfile profile) =>
        new[] { profile.CurrentIntent }.Concat(Contexts(profile).Select(c => c.Lens));

    private static IEnumerable<string> StrongInterests(UserProfile profile) =>
        profile.DominantInterests.Concat(InterestPathService.ActiveInterests(profile)).Distinct(StringComparer.OrdinalIgnoreCase);

    private static IEnumerable<string> GeneralInterests(UserProfile profile)
    {
        var strong = StrongInterests(profile).ToList();
        return profile.Interests.Where(i => !strong.Contains(i, StringComparer.OrdinalIgnoreCase));
    }

    private static IEnumerable<string> ContextPhrases(UserProfile profile) =>
        new[] { profile.TargetRole, profile.TargetIndustry, profile.Region, profile.City }
            .Concat(profile.Geography)
            .Concat(profile.DecisionNeeds);

    private static string Text(ContentItem item) =>
        $"{item.Title} {item.Topic} {string.Join(' ', item.SecondaryTopics)} {item.Signal} {item.AiSummary} {item.WhatHappened} {string.Join(' ', item.Tags)}".ToLowerInvariant();

    private static string Lower(string value) => value.ToLowerInvariant();

    private static bool ContainsContext(string text, string value) =>
        !string.IsNullOrWhiteSpace(value) && text.Contains(value, StringComparison.OrdinalIgnoreCase);
}
