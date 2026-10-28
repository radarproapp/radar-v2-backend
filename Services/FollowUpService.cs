using RadarV2.Models;

namespace RadarV2.Services;

/// <summary>
/// Turns the user's behaviour (signals) and gaps in their profile into a small number of timely,
/// human follow-up questions. Deterministic — no AI key required — and never more than a few at a
/// time so it doesn't become an interrogation.
/// </summary>
public static class FollowUpService
{
    /// <summary>
    /// How many separate engagements make a term worth naming as a theme.
    /// </summary>
    /// <remarks>
    /// Three. One open is an accident and two is a coincidence; a banner that calls either of
    /// them a habit is telling the reader something about themselves that is not true, and the
    /// next thing they learn is to close it without reading.
    /// </remarks>
    private const int RepeatedEngagements = 3;

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

        // ── Top 3 interacted things ──────────────────────────────────────────
        // Highlights the top 3 topics/terms the user has been engaging with the most across
        // reads, saves, searches, and activity.
        var positiveSignals = allSignals
            .Where(s => s.Strength > 0.05 && (s.NegativeCount == 0 || s.PositiveCount > s.NegativeCount))
            .Where(s => s.ExplicitFeedback > -0.5)
            // A pattern, not a click. "You've been engaging most with X" is a claim about a habit,
            // and one open is not one — so a term has to have been returned to.
            .Where(s => s.PositiveCount >= RepeatedEngagements)
            .OrderByDescending(s => (s.PositiveCount * 2.0) + BehavioralSignalService.DecayedStrength(s))
            .ThenByDescending(s => s.UpdatedAt)
            .Select(s => s.Term.Trim())
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // No padding. "You've been engaging most with X recently" is a claim about behaviour, and
        // it was being filled out with the interests chosen at signup when real signals ran short
        // — so a brand new account was told it had been engaging most with three things it had
        // never opened. If there is not enough behaviour to describe, the banner does not appear.

        // Three, because the sentence names three and a banner that announces one theme as the
        // things you engage with most is describing a coincidence.
        //
        // This read "> 0", directly under a comment claiming that a thin history means no banner.
        // It did not: one signal was enough, so the banner appeared for anybody who had opened
        // anything, every time, which is what made it furniture rather than an observation.
        var top3 = positiveSignals.Take(3).ToList();
        if (top3.Count == 3)
        {
            var prompt = top3.Count switch
            {
                3 => $"You've been engaging most with {top3[0]}, {top3[1]}, and {top3[2]} recently.",
                2 => $"You've been engaging most with {top3[0]} and {top3[1]} recently.",
                _ => $"You've been engaging most with {top3[0]} recently.",
            };

            var unaddedInterest = top3.FirstOrDefault(t => !interests.Contains(t));
            suggestions.Add(new FollowUpSuggestion
            {
                // Identity is the set, not the order.
                //
                // This was the terms joined in rank order, and that order changes on almost every
                // interaction — so dismissing the banner produced a different id next time and the
                // same message came straight back. Dismissal had never once worked for it.
                Id = $"top3:{string.Join("-", top3.OrderBy(t => t, StringComparer.OrdinalIgnoreCase))}",
                Kind = "topInteractions",
                Prompt = prompt,
                Detail = "Radar is tuning your daily intelligence, briefings, and opportunities around these themes.",
                Action = unaddedInterest is not null ? "addInterest" : "explore",
                ActionLabel = unaddedInterest is not null ? $"Add {unaddedInterest} to interests" : "Explore in Feed",
                Value = unaddedInterest ?? top3[0],
                Items = top3,
            });
        }

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
