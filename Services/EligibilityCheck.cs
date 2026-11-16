using RadarV2.Models;

namespace RadarV2.Services;

/// <summary>
/// Whether somebody could actually get this, as distinct from whether they would want it.
/// </summary>
/// <remarks>
/// The distinction the brief puts at the centre: "a user interested in AI should not
/// automatically receive every AI-related job." Relevance asks whether something is about what
/// you care about; eligibility asks whether you could apply. An AI-interested student and an
/// AI-interested director both match "AI", and only one of them should see the senior role.
///
/// Until now this existed only as a schema. Eligibility recorded what a posting requires, the
/// profile recorded what a person has, and nothing compared them - so the half of the engine the
/// brief calls most important had no code at all.
/// </remarks>
public enum EligibilityVerdict
{
    /// <summary>
    /// The posting states no criteria, so nobody has read them.
    /// </summary>
    /// <remarks>
    /// Deliberately not Eligible. An opportunity with nothing recorded is not one open to
    /// everybody - it is one nobody has checked, and a screen that claims to check eligibility
    /// must not turn silence into a pass.
    /// </remarks>
    Unchecked,

    /// <summary>Every stated criterion is met by something the person has told Radar.</summary>
    Eligible,

    /// <summary>
    /// A stated criterion needs a fact Radar has never asked for.
    /// </summary>
    /// <remarks>
    /// The honest answer, and a common one: nationality, age and years of experience are all
    /// things a posting can require and nothing on the profile holds. The brief is explicit that
    /// these must not be guessed - "do not infer nationality eligibility merely because a
    /// programme is described as international", "do not assume a remote opportunity accepts
    /// applicants from Nigeria."
    ///
    /// Shown rather than hidden, with the gap named, because the fix is to ask.
    /// </remarks>
    CannotTell,

    /// <summary>A stated criterion is contradicted by something the person has told Radar.</summary>
    NotEligible,
}

/// <summary>What the check concluded, and why.</summary>
/// <param name="Verdict">The conclusion.</param>
/// <param name="Met">Criteria the person satisfies, for the "who is eligible" line.</param>
/// <param name="Failed">Criteria the person does not satisfy.</param>
/// <param name="Unknown">Criteria that need a fact nobody has given, phrased as what to ask.</param>
public sealed record EligibilityResult(
    EligibilityVerdict Verdict,
    IReadOnlyList<string> Met,
    IReadOnlyList<string> Failed,
    IReadOnlyList<string> Unknown)
{
    /// <summary>Whether this is safe to put in front of this person as something to apply for.</summary>
    /// <remarks>
    /// NotEligible is the only hard no. Unchecked and CannotTell both mean Radar does not know,
    /// and hiding on a guess is the failure the brief warns about more than once - the cost of
    /// wrongly hiding a scholarship somebody qualifies for is higher than the cost of showing one
    /// they do not, provided Radar says which it is rather than implying a check it did not make.
    /// </remarks>
    public bool WorthShowing => Verdict != EligibilityVerdict.NotEligible;
}

public static class EligibilityCheck
{
    /// <summary>
    /// Compares a person against what a posting asks for.
    /// </summary>
    /// <remarks>
    /// Every absent criterion on the posting means "not stated", never "no restriction" - a
    /// source that did not say who may apply has told us nothing. Every absent fact on the person
    /// means "not asked", never "does not have it".
    ///
    /// Those two rules are why the result has three lists rather than a boolean. A caller that
    /// cannot tell a met criterion from an unasked one will assert something nothing established,
    /// which on a screen that claims to check eligibility is worse than saying nothing.
    /// </remarks>
    public static EligibilityResult For(UserProfile profile, Eligibility? eligibility)
    {
        var met = new List<string>();
        var failed = new List<string>();
        var unknown = new List<string>();

        if (eligibility is null || !eligibility.WasStated)
            return new EligibilityResult(EligibilityVerdict.Unchecked, met, failed, unknown);

        CheckQualification(profile, eligibility, met, failed, unknown);
        CheckSeniority(profile, eligibility, met, failed, unknown);
        CheckCertifications(profile, eligibility, met, failed, unknown);
        CheckLocation(profile, eligibility, met, failed, unknown);
        CheckFactsNobodyHasAsked(eligibility, unknown);

        var verdict = failed.Count > 0 ? EligibilityVerdict.NotEligible
            : unknown.Count > 0 ? EligibilityVerdict.CannotTell
            : EligibilityVerdict.Eligible;

        return new EligibilityResult(verdict, met, failed, unknown);
    }

    private static void CheckQualification(
        UserProfile profile, Eligibility asked, List<string> met, List<string> failed, List<string> unknown)
    {
        if (asked.MinimumQualification is not { } floor) return;

        var wanted = $"{Name(floor)} or higher";

        if (profile.Qualification is not { } held)
        {
            unknown.Add($"Asks for {wanted}. You have not told Radar your qualification.");
            return;
        }

        if (held >= floor) met.Add($"Asks for {wanted}, and you have {Name(held)}.");
        else failed.Add($"Asks for {wanted}. You have {Name(held)}.");
    }

    /// <summary>
    /// Seniority, through the rule rather than a raw comparison.
    /// </summary>
    /// <remarks>
    /// SeniorityRule exists so nobody writes <c>person >= floor</c> against a nullable enum and
    /// silently excludes everybody who has not answered. The null case is handled here as its own
    /// branch so it reports as unknown rather than quietly passing.
    /// </remarks>
    private static void CheckSeniority(
        UserProfile profile, Eligibility asked, List<string> met, List<string> failed, List<string> unknown)
    {
        if (asked.MinimumSeniority is null && asked.MaximumSeniority is null) return;

        if (profile.SeniorityLevel is not { } held)
        {
            unknown.Add("Asks for a particular level. You have not told Radar yours.");
            return;
        }

        if (!SeniorityRule.Meets(held, asked.MinimumSeniority))
        {
            failed.Add($"Asks for {asked.MinimumSeniority} level or above. You are {held}.");
            return;
        }

        if (!SeniorityRule.Within(held, asked.MaximumSeniority))
        {
            // The direction Radar is most likely to get wrong: a subject match putting a director
            // in front of a graduate scheme.
            failed.Add($"Is aimed at {asked.MaximumSeniority} level or below. You are {held}.");
            return;
        }

        met.Add($"You are {held}, which this is open to.");
    }

    private static void CheckCertifications(
        UserProfile profile, Eligibility asked, List<string> met, List<string> failed, List<string> unknown)
    {
        if (asked.RequiredCertifications is not { Count: > 0 } required) return;

        var held = profile.Certifications ?? [];

        if (held.Count == 0)
        {
            unknown.Add($"Asks for {Join(required)}. You have not told Radar your certifications.");
            return;
        }

        var missing = required
            .Where(need => !held.Any(have => have.Equals(need, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        if (missing.Count == 0) met.Add($"You hold {Join(required)}.");
        else failed.Add($"Asks for {Join(missing)}.");
    }

    /// <summary>
    /// Where somebody is, against where a posting is open to.
    /// </summary>
    /// <remarks>
    /// Region is the only location fact the profile holds, and it is coarse. It is used to
    /// confirm a match, never to deny one: a region that does not appear in the list is not
    /// evidence of ineligibility, because the list may name countries and the profile may hold a
    /// continent. Denying on that would hide opportunities from people who qualify.
    /// </remarks>
    private static void CheckLocation(
        UserProfile profile, Eligibility asked, List<string> met, List<string> failed, List<string> unknown)
    {
        if (asked.Countries is not { Count: > 0 } countries) return;

        if (string.IsNullOrWhiteSpace(profile.Region))
        {
            unknown.Add($"Open to {Join(countries)}. You have not told Radar where you are.");
            return;
        }

        var here = countries.Any(country =>
            country.Contains(profile.Region, StringComparison.OrdinalIgnoreCase)
            || profile.Region.Contains(country, StringComparison.OrdinalIgnoreCase));

        if (here) met.Add($"Open to {Join(countries)}, which includes you.");
        else unknown.Add($"Open to {Join(countries)}. Radar cannot tell whether that includes you.");
    }

    /// <summary>
    /// Criteria Radar has never asked anybody about.
    /// </summary>
    /// <remarks>
    /// Nationality, age and years of experience are all things a posting can require and nothing
    /// on the profile holds. Reporting them as unknown rather than ignoring them is the point:
    /// silently dropping a criterion makes a partial check look like a complete one, and the
    /// brief is explicit that nationality in particular must never be inferred.
    ///
    /// Each line is phrased as what is missing, so the eventual fix - asking - is obvious from
    /// the output rather than needing to be rediscovered.
    /// </remarks>
    private static void CheckFactsNobodyHasAsked(Eligibility asked, List<string> unknown)
    {
        if (asked.Nationalities is { Count: > 0 } nationalities)
            unknown.Add($"Open to {Join(nationalities)} nationals. Radar has not asked yours.");

        if (asked.MinimumYearsExperience is not null || asked.MaximumYearsExperience is not null)
            unknown.Add("Asks for a particular amount of experience. Radar has not asked yours.");

        if (asked.MinimumAge is not null || asked.MaximumAge is not null)
            unknown.Add("Has an age range. Radar has not asked your age.");
    }

    private static string Join(IEnumerable<string> values) => string.Join(", ", values);

    /// <summary>The qualification in the words the onboarding question used.</summary>
    private static string Name(QualificationLevel level) => level switch
    {
        QualificationLevel.InProgress => "a qualification in progress",
        QualificationLevel.SecondarySchool => "secondary school",
        QualificationLevel.Nce => "an NCE",
        QualificationLevel.Ond => "an OND",
        QualificationLevel.Hnd => "an HND",
        QualificationLevel.Bachelors => "a bachelor's degree",
        QualificationLevel.Masters => "a master's degree",
        QualificationLevel.Doctorate => "a PhD",
        _ => level.ToString(),
    };
}
