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

        CheckSkills(profile, eligibility, met, failed, unknown);
        CheckQualification(profile, eligibility, met, failed, unknown);
        CheckSeniority(profile, eligibility, met, failed, unknown);
        CheckCertifications(profile, eligibility, met, failed, unknown);
        CheckLocation(profile, eligibility, met, failed, unknown);
        CheckNationality(profile, eligibility, met, failed, unknown);
        CheckExperience(profile, eligibility, met, failed, unknown);
        CheckAge(profile, eligibility, met, failed, unknown);

        var verdict = failed.Count > 0 ? EligibilityVerdict.NotEligible
            : unknown.Count > 0 ? EligibilityVerdict.CannotTell
            : EligibilityVerdict.Eligible;

        // Criteria Radar read for itself can inform and cannot exclude.
        //
        // A review ran the extractor over real job-board text and it fired wrongly more often
        // than rightly: "up to 20 hours per week" became an age limit of 20, company boilerplate
        // about fifteen years in the market became an experience floor, and "a degree is not
        // required" became a requirement. Every one of those hid a posting from somebody who
        // qualified, and they would never know.
        //
        // The patterns are being fixed. This is here because they will still sometimes be wrong,
        // and the design has always said the two directions are not symmetrical: a missed
        // criterion costs a listing that would have shown anyway, a wrongly read one costs an
        // opportunity. Capping extracted criteria at CannotTell makes that asymmetry structural
        // instead of a property of how good the regexes happen to be.
        //
        // The reasons still render, so a reader is told what the posting asks for and what Radar
        // could not confirm. They are simply not acted on behind their back.
        if (verdict == EligibilityVerdict.NotEligible && eligibility.Source == EligibilitySource.Extracted)
            verdict = EligibilityVerdict.CannotTell;

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

        var missing = required
            .Where(need => !held.Any(have => have.Equals(need, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        if (missing.Count == 0)
        {
            met.Add($"You hold {Join(required)}.");
            return;
        }

        // Unknown rather than failed, whether or not the person has listed any. A certification
        // list is never a closed world - nobody enumerates every credential they hold - so "not
        // in the list" is an absent fact, not a contradiction.
        //
        // The asymmetry this replaces punished answering. Having listed nothing reported unknown
        // and was shown; having listed anything reported failed and was hidden, on the inference
        // "holds some certification, therefore not this one". So a person who answered the
        // qualification question truthfully was treated worse than one who skipped it, which is
        // the ask-never-infer rule inverted.
        unknown.Add($"Asks for {Join(missing)}. Radar does not know whether you hold it.");
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

        // The specific answer first. Region holds things like "Africa", which cannot be compared
        // against a country list without guessing in one direction or the other.
        if (!string.IsNullOrWhiteSpace(profile.CountryOfResidence))
        {
            var lives = countries.Any(country =>
                country.Equals(profile.CountryOfResidence, StringComparison.OrdinalIgnoreCase));

            if (lives) met.Add($"Open to {Join(countries)}, and you are in {profile.CountryOfResidence}.");
            else failed.Add($"Open to {Join(countries)} only. You are in {profile.CountryOfResidence}.");

            return;
        }

        if (string.IsNullOrWhiteSpace(profile.Region))
        {
            unknown.Add($"Open to {Join(countries)}. You have not told Radar where you are.");
            return;
        }

        // Region is coarse, so it is allowed to confirm and never to deny: a region that does not
        // appear is not evidence, because the list may name countries while the profile holds a
        // continent. Matched whole rather than by substring - "Niger" is inside "Nigeria", and a
        // containment test would have quietly made one country eligible for the other's rules.
        var here = countries.Any(country =>
            country.Equals(profile.Region, StringComparison.OrdinalIgnoreCase));

        if (here) met.Add($"Open to {Join(countries)}, which includes you.");
        else unknown.Add($"Open to {Join(countries)}. Radar cannot tell whether that includes you.");
    }

    /// <summary>
    /// Skills, which inform rather than exclude.
    /// </summary>
    /// <remarks>
    /// Checked at all because RequiredSkills counts towards WasStated, so a posting whose only
    /// stated criterion was a skill list started the check and then matched nothing - every
    /// branch returned early and the verdict fell through to Eligible, with no met, failed or
    /// unknown lines behind it. A bare "Eligible" against a posting nothing had been compared to
    /// is the worst answer this can give, because it is the one a reader would act on.
    ///
    /// Never a failure, though. A posting listing five skills rarely requires all five, and
    /// treating a gap as ineligibility would hide almost everything from almost everybody -
    /// which is the opposite of what a skills list is for. Overlap is evidence worth stating;
    /// absence is a question, and the honest answer is that Radar cannot tell.
    ///
    /// Judging skill against a required level is Radar Learn's competency model, which is not
    /// built. This deliberately stops short of pretending otherwise.
    /// </remarks>
    private static void CheckSkills(
        UserProfile profile, Eligibility asked, List<string> met, List<string> failed, List<string> unknown)
    {
        if (asked.RequiredSkills is not { Count: > 0 } wanted) return;

        var held = profile.Capabilities ?? [];

        if (held.Count == 0)
        {
            unknown.Add($"Asks for {Join(wanted)}. You have not told Radar your skills.");
            return;
        }

        var shared = wanted
            .Where(need => held.Any(have => have.Equals(need, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        if (shared.Count > 0) met.Add($"You have {Join(shared)}, which this asks for.");
        else unknown.Add($"Asks for {Join(wanted)}. None of those are skills you have listed.");
    }

    /// <summary>
    /// Nationality, which is never inferred.
    /// </summary>
    /// <remarks>
    /// The brief names this one specifically: "do not infer nationality eligibility merely
    /// because a programme is described as international." So a person who has not said is
    /// unknown, and a person who has said is checked against the list rather than against where
    /// they live - a Nigerian national studying in Ghana is still a Nigerian national, and the
    /// two fields exist precisely so that is not confused.
    ///
    /// Any one nationality matching is enough. Dual nationality is the case this exists for, and
    /// a scholarship open to Nigerians does not stop being open because somebody is also
    /// something else.
    /// </remarks>
    private static void CheckNationality(
        UserProfile profile, Eligibility asked, List<string> met, List<string> failed, List<string> unknown)
    {
        if (asked.Nationalities is not { Count: > 0 } wanted) return;

        var held = profile.Nationalities ?? [];

        if (held.Count == 0)
        {
            unknown.Add($"Open to {Join(wanted)} nationals. You have not told Radar your nationality.");
            return;
        }

        var matches = held.Any(mine =>
            wanted.Any(theirs => theirs.Equals(mine, StringComparison.OrdinalIgnoreCase)));

        if (matches) met.Add($"Open to {Join(wanted)} nationals, and you are one.");
        else failed.Add($"Open to {Join(wanted)} nationals only.");
    }

    /// <summary>
    /// Years of experience, in both directions.
    /// </summary>
    /// <remarks>
    /// The ceiling is the half that is easy to forget and the half that does the work here. A
    /// graduate programme whose only rule is "no more than two years" is excluding a six-year
    /// professional on purpose, and without this it was the posting Radar called unchecked.
    /// </remarks>
    private static void CheckExperience(
        UserProfile profile, Eligibility asked, List<string> met, List<string> failed, List<string> unknown)
    {
        if (asked.MinimumYearsExperience is null && asked.MaximumYearsExperience is null) return;

        if (profile.YearsOfExperience is not { } years)
        {
            unknown.Add("Asks for a particular amount of experience. You have not told Radar yours.");
            return;
        }

        if (asked.MinimumYearsExperience is { } least && years < least)
        {
            failed.Add($"Asks for at least {least} years. You have {years}.");
            return;
        }

        if (asked.MaximumYearsExperience is { } most && years > most)
        {
            failed.Add($"Is for people with no more than {most} years. You have {years}.");
            return;
        }

        met.Add($"Your {years} years of experience fits what this asks for.");
    }

    /// <summary>
    /// An age range, which is the one criterion nobody can acquire.
    /// </summary>
    /// <remarks>
    /// Read from the date of birth rather than a stored age, so it cannot go stale - which
    /// matters most here, because the programmes with upper age limits are exactly the ones
    /// somebody ages out of while Radar still has their old answer.
    /// </remarks>
    private static void CheckAge(
        UserProfile profile, Eligibility asked, List<string> met, List<string> failed, List<string> unknown)
    {
        if (asked.MinimumAge is null && asked.MaximumAge is null) return;

        if (profile.Age is not { } age)
        {
            unknown.Add("Has an age range. You have not told Radar your date of birth.");
            return;
        }

        if (asked.MinimumAge is { } youngest && age < youngest)
        {
            failed.Add($"Is for people {youngest} and over. You are {age}.");
            return;
        }

        if (asked.MaximumAge is { } oldest && age > oldest)
        {
            failed.Add($"Is for people up to {oldest}. You are {age}.");
            return;
        }

        met.Add($"You are {age}, which is inside the age range.");
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
