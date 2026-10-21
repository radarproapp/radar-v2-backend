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
    /// <summary>
    /// How well an item fits, and why.
    ///
    /// <paramref name="record"/> is what the user's behaviour says, as opposed to what they typed
    /// at onboarding. It is optional because several callers score items with no user in scope,
    /// and gated on confidence because thin evidence should not outrank something the user
    /// actually told us.
    /// </summary>
    public static Explanation Explain(UserProfile profile, ContentItem item, UserLearningRecord? record = null)
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

        // What the record knows and the profile does not. Weighted below Goal, because a stated
        // goal is a decision and a derived topic is an inference - but above Interest, because
        // what someone is actually working through beats a box they ticked at signup.
        if (record is not null && record.Confidence >= MinConfidenceToUseRecord)
        {
            AddDimension(matches, text, "Role", [record.TargetRole], 22);
            AddDimension(matches, text, "Learning", record.PriorityTopics, 25);
            AddDimension(matches, text, "Gap", record.WeakAreas, 12);
        }

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

    public static int ContentScore(ContentItem item, UserProfile profile, UserLearningRecord? record = null) => Explain(profile, item, record).Score;

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

        foreach (var phrase in GoalPhrases(profile).Concat(profile.Capabilities))
            if (ContainsContext(text, phrase)) score += 10;

        if (profile.Geography.Count > 0 && profile.Geography.Any(g => ContainsContext(text, g))) score += 8;

        var normalizedGoal = profile.PrimaryGoal.ToLowerInvariant();
        if (normalizedGoal.Contains("intern") && opportunity.Type == OpportunityType.Internship) score += 12;
        if ((normalizedGoal.Contains("startup") || normalizedGoal.Contains("fund")) && opportunity.Type == OpportunityType.StartupAccelerator) score += 12;

        // Read the outcome rather than hunting for keywords that an outcome goal does not contain.
        var outcome = LearningIntentReader.Read(profile).Outcome;
        score += outcome switch
        {
            LearningOutcome.NewJob when opportunity.Type is OpportunityType.Job or OpportunityType.Internship => 12,
            LearningOutcome.StartBusiness when opportunity.Type is OpportunityType.StartupAccelerator or OpportunityType.ResearchGrant => 12,
            LearningOutcome.Promotion or LearningOutcome.Leadership
                when opportunity.Type is OpportunityType.Fellowship or OpportunityType.Conference => 10,
            LearningOutcome.EarnMore when opportunity.Type is OpportunityType.Job => 10,
            _ => 0,
        };
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
        var location = PlaceName(profile);
        var target = !string.IsNullOrWhiteSpace(profile.TargetRole)
            ? profile.TargetRole
            : (!string.IsNullOrWhiteSpace(profile.TargetIndustry) ? $"{profile.TargetIndustry} roles" : "technology-focused roles");
        var topic = !string.IsNullOrWhiteSpace(item.Topic) ? item.Topic : item.Title;

        var sb = new StringBuilder();
        // Interest and goal are frequently the same word, which produced "where cybersecurity meets
        // your goal: Cybersecurity". Say it once when they match.
        var goalPhrase = SubjectPhrase.Clean(goal);
        sb.Append(goalPhrase.Equals(interest, StringComparison.OrdinalIgnoreCase)
            ? $"This matters to you because it speaks directly to your goal: {goalPhrase}. "
            : $"This matters to you because it sits where {Lower(interest)} meets your goal: {goalPhrase}. ");
        sb.Append($"In {location}, movement in {Lower(topic)} is creating demand for people who can do the technical work and explain the business case, especially for a {Lower(target)}. ");

        if (!string.IsNullOrWhiteSpace(problem))
        {
            sb.Append($"You said {Lower(problem)} is holding you back. This shows how other teams are solving the same thing. ");
        }

        if (!string.IsNullOrWhiteSpace(capability))
        {
            sb.Append($"A skill that becomes significantly more valuable here is applying {Lower(capability)} to solve operational bottlenecks in {Lower(topic)}. ");
        }
        else
        {
            sb.Append($"This makes your current learning path more urgent, particularly as practical expertise in {Lower(topic)} becomes a key market differentiator. ");
        }

        sb.Append("This is not general industry news. It changes what you should learn next and how you position yourself.");
        return sb.ToString().Trim();
    }

    /// <summary>
    /// Suppressed. Spec 4.5 offers "per-item grounded output, or suppress the section" - this is
    /// the second.
    ///
    /// What it used to produce was the same four sentences for every item in the feed, ending
    /// with "a market worth watching is the emergence of specialized X workflows tailored for Y
    /// infrastructure constraints" - a claim about a market nobody had looked at, written out for
    /// every article. Where the user had stated no capability it filled the gap with "market
    /// research" and told them it demonstrated theirs.
    ///
    /// The section was already removed from the interface in 1.2 and dropped from the AI contract
    /// in 4.3, so nothing renders it. Generating it anyway meant the API kept asserting things
    /// about readers that no screen would ever show them, and that they could not correct.
    ///
    /// The field itself still exists on the models and is now always empty; removing it touches
    /// 67 sites across 13 files and is filed as 4.5b rather than done carelessly here.
    /// </summary>
    private static string BuildYourEdge(UserProfile profile, ContentItem item, List<DimensionMatch> matches) =>
        string.Empty;

    /// <summary>
    /// What to actually do with this item.
    ///
    /// Every step used to be filled in whether or not the ingredient existed: with no stated
    /// capability it claimed "applied problem-solving", and step 2 instructed the reader to
    /// "identify 3 organizations in {location} directly affected by this shift" - three
    /// organisations nothing had found, in a location that defaulted to "your target market".
    ///
    /// Steps are now only included when the thing they refer to is known. A three-step list the
    /// reader can actually follow beats a five-step list with two invented rungs.
    /// </summary>
    private static string BuildNextMove(UserProfile profile, ContentItem item, List<DimensionMatch> matches)
    {
        var topic = !string.IsNullOrWhiteSpace(item.Topic) ? item.Topic : item.Title;
        var capability = matches.FirstOrDefault(m => m.Dimension == "Capability")?.Phrase
                         ?? profile.Capabilities.FirstOrDefault();
        var location = !string.IsNullOrWhiteSpace(profile.Region) ? profile.Region : profile.City;

        var steps = new List<string>();
        var step = 1;

        steps.Add(!string.IsNullOrWhiteSpace(item.Source)
            ? $"{step++}. Read the full piece from {item.Source} and write down the one thing that changed."
            : $"{step++}. Read the full piece and write down the one thing that changed.");

        // Only asks about a place when the user told us one.
        if (!string.IsNullOrWhiteSpace(location))
        {
            steps.Add($"{step++}. Note whether this changes anything for {location}.");
        }

        // Only names a capability the user actually claimed.
        if (!string.IsNullOrWhiteSpace(capability))
        {
            steps.Add($"{step++}. Decide whether it affects your {Lower(capability)} work.");
        }

        steps.Add(!string.IsNullOrWhiteSpace(topic)
            ? $"{step}. If it matters, add {Lower(topic)} to your roadmap."
            : $"{step}. If it matters, add it to your roadmap.");

        return string.Join("\n", steps);
    }

    public static string PortfolioArtifactFor(string capability, string topic) =>
        GetSpecificPortfolioArtifact(capability, topic);

    private static string GetSpecificPortfolioArtifact(string capability, string topic)
    {
        var text = $"{capability} {topic}".ToLowerInvariant();

        if (text.Contains("model") || text.Contains("valuation") || text.Contains("dcf") || text.Contains("finance"))
            return "a 3-statement financial forecast model analyzing this market case";

        if (text.Contains("product") || text.Contains("prd") || text.Contains("user story") || text.Contains("feature"))
            return "a 1-page PRD and user journey map addressing this problem";

        // Checked before the generic "data"/"system" branches below: a SOC analyst asking for a
        // portfolio piece should be told to build a detection rule, not a metrics dashboard.
        if (DomainVocabulary.Matches(text, "cyber"))
            return "a documented detection rule plus an incident write-up showing how you'd triage and contain it";

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

    /// <summary>
    /// How sure the record has to be before it is allowed to move the feed. Same threshold the
    /// roadmap personaliser and the opportunity scorer use - it is the same judgement in all
    /// three places, so it should not drift between them.
    /// </summary>
    private const double MinConfidenceToUseRecord = 0.35;

    private static double WeightFor(string dimension) => dimension switch
    {
        "Goal" => 30,
        "Learning" => 25,
        "Role" => 22,
        "Problem" => 20,
        "Gap" => 12,
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

    /// <summary>
    /// Phrases that stand for the user's goal when scoring content.
    ///
    /// Includes the interpreted domain, not just the raw goal. A goal of "Get Promotion" appears in
    /// no article ever written, so matching on it alone scored every item at zero on this dimension
    /// — and the user saw content that had nothing to do with them. What such a person actually
    /// wants to read about is their field.
    /// </summary>
    private static IEnumerable<string> GoalPhrases(UserProfile profile)
    {
        var phrases = new List<string> { profile.PrimaryGoal };

        var intent = LearningIntentReader.Read(profile);
        if (intent.IsOutcomeDriven && !string.IsNullOrWhiteSpace(intent.Domain))
            phrases.Add(intent.Domain);

        return phrases.Concat(Contexts(profile).Select(c => c.Goal));
    }

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

    /// <summary>
    /// Lowercases for mid-sentence use, but leaves acronyms and the user's own capitalised terms
    /// alone. Blanket ToLowerInvariant turned "SOC Analyst" into "soc analyst" and the user's own
    /// goal into "cybersecurity: learn new skills", which reads like a bug even when the sentence
    /// around it is right.
    /// </summary>
    private static string Lower(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return value;

        return string.Join(" ", value.Split(' ').Select(word =>
        {
            if (word.Length == 0) return word;

            // ALL-CAPS short tokens are acronyms: SOC, AI, SQL, API, SIEM.
            var letters = new string(word.Where(char.IsLetter).ToArray());
            if (letters.Length is > 1 and <= 5 && letters.ToUpperInvariant() == letters) return word;

            // Internal capitals mean a brand or compound: PyTorch, OpenAI, JavaScript.
            if (word.Length > 1 && word[1..].Any(char.IsUpper)) return word;

            return word.ToLowerInvariant();
        }));
    }

    /// <summary>City and region are often the same word ("Lagos, Lagos"). Say it once.</summary>
    private static string PlaceName(UserProfile profile)
    {
        var city = (profile.City ?? string.Empty).Trim();
        var region = (profile.Region ?? string.Empty).Trim();

        if (city.Length > 0 && region.Length > 0)
            return city.Equals(region, StringComparison.OrdinalIgnoreCase) ? city : $"{city}, {region}";

        if (city.Length > 0) return city;
        if (region.Length > 0) return region;
        return "your target market";
    }

    private static bool ContainsContext(string text, string value) =>
        !string.IsNullOrWhiteSpace(value) && text.Contains(value, StringComparison.OrdinalIgnoreCase);
}
