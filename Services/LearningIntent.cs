using System.Text.RegularExpressions;
using RadarV2.Models;

namespace RadarV2.Services;

/// <summary>
/// Works out what a user is actually trying to learn, before anything is generated for them.
///
/// Everything downstream used to read the raw goal string and paste it into sentences, which is
/// how a user whose goal was "Get Promotion" ended up with "Foundations of Get Promotion",
/// "Applied Get Promotion" and "What Get Promotion actually involves day to day" — and, because
/// resources were keyed on their interest while modules were keyed on their goal, a Python machine
/// learning course on top.
///
/// The missing idea is that a goal and a subject are not the same thing. "Get promoted" is an
/// OUTCOME: the thing to learn is not "promotion", it is how to be promotable in their field.
/// "Cybersecurity" is a SUBJECT: the thing to learn is the subject itself. Until something makes
/// that distinction, no amount of prompt or template work produces a sensible path.
/// </summary>
public sealed record LearningIntent(
    /// <summary>What the user is learning, as a noun phrase that reads correctly inside a sentence.</summary>
    string Subject,
    /// <summary>Field used to route channels and catalogues. Never the outcome.</summary>
    string Domain,
    /// <summary>The outcome they are chasing, when they named one.</summary>
    LearningOutcome Outcome)
{
    /// <summary>True when the goal named a result rather than a field of study.</summary>
    public bool IsOutcomeDriven => Outcome != LearningOutcome.LearnSubject;
}

public enum LearningOutcome
{
    LearnSubject,
    Promotion,
    NewJob,
    EarnMore,
    StartBusiness,
    Leadership,
}

public static partial class LearningIntentReader
{
    [GeneratedRegex(@"\b(promot|senior|next level|move up|career growth|advance)\w*", RegexOptions.IgnoreCase)]
    private static partial Regex Promotion();

    [GeneratedRegex(@"\b(get a job|find a job|land a|new job|employ|hired|internship)\w*", RegexOptions.IgnoreCase)]
    private static partial Regex NewJob();

    [GeneratedRegex(@"\b(earn more|higher pay|salary|income|raise)\w*", RegexOptions.IgnoreCase)]
    private static partial Regex EarnMore();

    [GeneratedRegex(@"\b(start a business|startup|found|entrepreneur|my own company)\w*", RegexOptions.IgnoreCase)]
    private static partial Regex StartBusiness();

    [GeneratedRegex(@"\b(lead a team|leadership|manager|management|become a lead)\w*", RegexOptions.IgnoreCase)]
    private static partial Regex Leadership();

    /// <summary>
    /// Reads the goal against the rest of the profile. The outcome comes from the goal; the domain
    /// comes from role, industry and interests, because someone chasing a promotion still needs to
    /// get better at their actual field, not at "promotion".
    /// </summary>
    /// <summary>
    /// Reads the whole of box 2, not only the first answer.
    ///
    /// Personalisation collects a list of goals — "find a job" and "learn a new skill" together is
    /// a normal answer — while PrimaryGoal is the single string the older onboarding wrote and the
    /// roadmap is still built around. Reading only PrimaryGoal meant a stated goal changed nothing
    /// unless it happened to be listed first: a learner who said "learn a new skill, get a
    /// promotion" scored as LearnSubject, and promotion-shaped opportunities never lifted.
    ///
    /// The outcome is taken from whichever goal names one, rather than from a concatenation. The
    /// subject and domain are built from a single goal string, and joining them produces a subject
    /// line reading "Find a job. Learn a new skill" — which then appears in the roadmap.
    /// </summary>
    public static LearningIntent Read(UserProfile profile)
    {
        var goals = profile.Goals.Where(g => !string.IsNullOrWhiteSpace(g)).ToList();
        var primary = !string.IsNullOrWhiteSpace(profile.PrimaryGoal)
            ? profile.PrimaryGoal
            : goals.FirstOrDefault() ?? string.Empty;

        var intent = Read(primary, profile.TargetRole, profile.TargetIndustry, profile.Interests);
        if (intent.Outcome != LearningOutcome.LearnSubject) return intent;

        // LearnSubject is also what an unreadable goal falls back to, so it is the one outcome
        // worth looking past. The first goal that names something more specific wins.
        foreach (var goal in goals)
        {
            if (ReadOutcome(goal) == LearningOutcome.LearnSubject) continue;
            return Read(goal, profile.TargetRole, profile.TargetIndustry, profile.Interests);
        }

        return intent;
    }

    public static LearningIntent Read(string? goal, string? role, string? industry, IEnumerable<string>? interests)
    {
        var goalText = (goal ?? string.Empty).Trim();
        var interestList = (interests ?? []).Where(i => !string.IsNullOrWhiteSpace(i)).ToList();

        var outcome = ReadOutcome(goalText);

        // The field they work in. Interests first — they are the most specific thing the user told
        // us — then role, then industry.
        var domain = FirstNonEmpty(
            interestList.FirstOrDefault(),
            role,
            industry,
            outcome == LearningOutcome.LearnSubject ? SubjectPhrase.Clean(goalText) : null,
            "your field");

        var subject = outcome switch
        {
            LearningOutcome.Promotion => RoleAware("getting promoted", role, domain),
            LearningOutcome.NewJob => RoleAware("getting hired", role, domain),
            LearningOutcome.EarnMore => RoleAware("increasing what you earn", role, domain),
            LearningOutcome.StartBusiness => $"building a business in {SubjectPhrase.Lower(domain)}",
            LearningOutcome.Leadership => RoleAware("leading a team", role, domain),
            _ => SubjectPhrase.Clean(goalText, domain),
        };

        return new LearningIntent(subject, domain, outcome);
    }

    private static LearningOutcome ReadOutcome(string goal)
    {
        if (string.IsNullOrWhiteSpace(goal)) return LearningOutcome.LearnSubject;

        // Order matters: "become a manager" is leadership, not merely a new job.
        if (Promotion().IsMatch(goal)) return LearningOutcome.Promotion;
        if (Leadership().IsMatch(goal)) return LearningOutcome.Leadership;
        if (StartBusiness().IsMatch(goal)) return LearningOutcome.StartBusiness;
        if (NewJob().IsMatch(goal)) return LearningOutcome.NewJob;
        if (EarnMore().IsMatch(goal)) return LearningOutcome.EarnMore;

        return LearningOutcome.LearnSubject;
    }

    /// <summary>"getting promoted as a SOC Analyst" reads better than "getting promoted".</summary>
    private static string RoleAware(string phrase, string? role, string domain)
    {
        if (!string.IsNullOrWhiteSpace(role)) return $"{phrase} as a {role}";
        if (!string.IsNullOrWhiteSpace(domain) && domain != "your field") return $"{phrase} in {SubjectPhrase.Lower(domain)}";
        return phrase;
    }

    private static string FirstNonEmpty(params string?[] candidates) =>
        candidates.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c))?.Trim() ?? "your field";
}
