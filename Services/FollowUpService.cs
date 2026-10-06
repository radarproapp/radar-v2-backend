using RadarV2.Models;

namespace RadarV2.Services;

/// <summary>
/// Turns the user's behaviour (signals) and gaps in their profile into a small number of timely,
/// human follow-up questions. Deterministic — no AI key required — and never more than a few at a
/// time so it doesn't become an interrogation.
/// </summary>
public static class FollowUpService
{
    public static List<FollowUpSuggestion> Suggest(
        UserProfile profile,
        IReadOnlyList<BehavioralSignal> topSignals,
        IReadOnlyList<BehavioralSignal> allSignals)
    {
        var suggestions = new List<FollowUpSuggestion>();
        var declared = allSignals
            .Where(s => s.Source == SignalSource.Declared)
            .Select(s => s.Term)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var interests = profile.Interests.ToHashSet(StringComparer.OrdinalIgnoreCase);

        // 1. Something the user keeps engaging with, that Radar only inferred.
        foreach (var signal in topSignals
            .Where(s => s.Source == SignalSource.Inferred && s.Strength >= 0.5
                        && !declared.Contains(s.Term) && !interests.Contains(s.Term))
            .Take(2))
        {
            suggestions.Add(new FollowUpSuggestion
            {
                Id = $"interest:{signal.Term}",
                Kind = "interest",
                Prompt = $"You've been engaging with {signal.Term} a lot. Want Radar to treat it as one of your interests?",
                Detail = "It would then count towards your feed, roadmap and opportunities.",
                Action = "addInterest",
                ActionLabel = "Add interest",
                Value = signal.Term,
            });
        }

        // 2. A shift in what they're actually focused on, away from their stated goal.
        var topInferred = topSignals.FirstOrDefault(s => s.Source == SignalSource.Inferred);
        if (topInferred is not null
            && !string.IsNullOrWhiteSpace(topInferred.Term)
            && topInferred.Strength >= 0.6
            && !interests.Contains(topInferred.Term)
            && !ProfileMentions(profile, topInferred.Term))
        {
            suggestions.Add(new FollowUpSuggestion
            {
                Id = $"focus:{topInferred.Term}",
                Kind = "focus",
                Prompt = $"You've been focused on {topInferred.Term} recently. Make it your #1 focus?",
                Detail = "Radar would reprioritise your feed and roadmap around it.",
                Action = "setFocus",
                ActionLabel = "Set as focus",
                Value = topInferred.Term,
            });
        }

        // 3. Gaps that sharpen personalization the most, in priority order.
        if (string.IsNullOrWhiteSpace(profile.CurrentIntent))
        {
            suggestions.Add(new FollowUpSuggestion
            {
                Id = "context:intent",
                Kind = "context",
                Prompt = "What are you trying to do right now?",
                Detail = "A current intent (e.g. \"find a finance internship\") sharpens what Radar shows you.",
                Action = "editProfile",
                ActionLabel = "Add intent",
            });
        }
        else if (profile.Problems.Count == 0)
        {
            suggestions.Add(new FollowUpSuggestion
            {
                Id = "context:problem",
                Kind = "context",
                Prompt = "What's making your goal difficult at the moment?",
                Detail = "Radar can point at the work that unblocks it.",
                Action = "editProfile",
                ActionLabel = "Tell Radar",
            });
        }
        else if (profile.Capabilities.Count == 0)
        {
            suggestions.Add(new FollowUpSuggestion
            {
                Id = "context:capabilities",
                Kind = "context",
                Prompt = "What do you want to become better at?",
                Detail = "Radar will prioritise learning and content that build those skills.",
                Action = "editProfile",
                ActionLabel = "Add capabilities",
            });
        }

        // De-duplicate by applied value (interest/focus can both name the same term).
        return suggestions
            .GroupBy(s => s.Value ?? s.Id, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .Take(3)
            .ToList();
    }

    private static bool ProfileMentions(UserProfile profile, string term) =>
        profile.PrimaryGoal.Contains(term, StringComparison.OrdinalIgnoreCase)
        || profile.CurrentIntent.Contains(term, StringComparison.OrdinalIgnoreCase)
        || profile.TargetRole.Contains(term, StringComparison.OrdinalIgnoreCase)
        || profile.TargetIndustry.Contains(term, StringComparison.OrdinalIgnoreCase)
        || profile.Capabilities.Any(c => c.Contains(term, StringComparison.OrdinalIgnoreCase))
        || profile.DecisionNeeds.Any(d => d.Contains(term, StringComparison.OrdinalIgnoreCase));
}
