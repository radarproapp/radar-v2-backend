using RadarV2.Models;
using RadarV2.Services;

namespace RadarV2.Tests;

/// <summary>
/// The three facts the check could only ever say "cannot tell" about.
/// </summary>
/// <remarks>
/// Nationality, age and years of experience are all things a posting can require, and nothing on
/// the profile held any of them - so every opportunity stating one came back unverifiable. They
/// are deliberately not onboarding questions: the brief keeps onboarding to three and lets Radar
/// learn the rest, so these are profile fields, asked once something turns out to need them.
/// </remarks>
public class EligibilityFactsTests
{
    // ── nationality ──────────────────────────────────────────────────────────

    [Fact]
    public void ANationalOfTheRightCountryIsEligible()
    {
        var nigerian = new UserProfile { Nationalities = ["Nigerian"] };

        var result = EligibilityCheck.For(nigerian, new Eligibility { Nationalities = ["Nigerian"] });

        Assert.Equal(EligibilityVerdict.Eligible, result.Verdict);
    }

    [Fact]
    public void SomebodyOfAnotherNationalityIsNotEligible()
    {
        var kenyan = new UserProfile { Nationalities = ["Kenyan"] };

        var result = EligibilityCheck.For(kenyan, new Eligibility { Nationalities = ["Nigerian"] });

        Assert.Equal(EligibilityVerdict.NotEligible, result.Verdict);
    }

    /// <summary>
    /// Holding a second nationality does not cost somebody the first.
    /// </summary>
    /// <remarks>
    /// The case the list exists for. A single field would have made Radar pick which of
    /// somebody's nationalities counts, and a scholarship open to Nigerians does not stop being
    /// open because the applicant is also British.
    /// </remarks>
    [Fact]
    public void ADualNationalQualifiesOnEitherNationality()
    {
        var dual = new UserProfile { Nationalities = ["British", "Nigerian"] };

        Assert.Equal(
            EligibilityVerdict.Eligible,
            EligibilityCheck.For(dual, new Eligibility { Nationalities = ["Nigerian"] }).Verdict);

        Assert.Equal(
            EligibilityVerdict.Eligible,
            EligibilityCheck.For(dual, new Eligibility { Nationalities = ["British", "Irish"] }).Verdict);
    }

    /// <summary>
    /// Nationality is never inferred from where somebody lives.
    /// </summary>
    /// <remarks>
    /// The brief names this one: "do not infer nationality eligibility merely because a programme
    /// is described as international." A Nigerian studying in Ghana is still a Nigerian national,
    /// and residence is a separate claim with its own field.
    /// </remarks>
    [Fact]
    public void ResidenceIsNotReadAsNationality()
    {
        var abroad = new UserProfile { CountryOfResidence = "Ghana" };

        var result = EligibilityCheck.For(abroad, new Eligibility { Nationalities = ["Nigerian"] });

        Assert.Equal(EligibilityVerdict.CannotTell, result.Verdict);
        Assert.Empty(result.Failed);
    }

    // ── experience ───────────────────────────────────────────────────────────

    /// <summary>
    /// The ceiling, which is the half that does the work.
    /// </summary>
    /// <remarks>
    /// A graduate programme whose only rule is "no more than two years" is excluding a six-year
    /// professional on purpose. Before this it was the posting Radar called unchecked.
    /// </remarks>
    [Fact]
    public void SomebodyTooExperiencedForAGraduateProgrammeIsNotEligible()
    {
        var experienced = new UserProfile { YearsOfExperience = 6 };
        var graduate = new UserProfile { YearsOfExperience = 1 };

        var scheme = new Eligibility { MaximumYearsExperience = 2 };

        Assert.Equal(EligibilityVerdict.NotEligible, EligibilityCheck.For(experienced, scheme).Verdict);
        Assert.Equal(EligibilityVerdict.Eligible, EligibilityCheck.For(graduate, scheme).Verdict);
    }

    [Fact]
    public void SomebodyShortOfTheExperienceFloorIsNotEligible()
    {
        var junior = new UserProfile { YearsOfExperience = 1 };

        var result = EligibilityCheck.For(junior, new Eligibility { MinimumYearsExperience = 5 });

        Assert.Equal(EligibilityVerdict.NotEligible, result.Verdict);
    }

    /// <summary>
    /// Nothing is a real answer, and the default must not claim it.
    /// </summary>
    /// <remarks>
    /// Why the field is nullable. Zero years is exactly who a graduate scheme wants, so a
    /// non-nullable int would have answered "no experience" on behalf of every profile that was
    /// never asked - and made them eligible for things on the strength of a default.
    /// </remarks>
    [Fact]
    public void NoStatedExperienceIsUnknownRatherThanZero()
    {
        var unasked = new UserProfile();
        var genuinelyNew = new UserProfile { YearsOfExperience = 0 };

        var scheme = new Eligibility { MaximumYearsExperience = 2 };

        Assert.Equal(EligibilityVerdict.CannotTell, EligibilityCheck.For(unasked, scheme).Verdict);
        Assert.Equal(EligibilityVerdict.Eligible, EligibilityCheck.For(genuinelyNew, scheme).Verdict);
    }

    // ── age ──────────────────────────────────────────────────────────────────

    [Fact]
    public void SomebodyInsideTheAgeRangeIsEligible()
    {
        var person = new UserProfile { DateOfBirth = DateTime.UtcNow.AddYears(-25) };

        var result = EligibilityCheck.For(person, new Eligibility { MinimumAge = 18, MaximumAge = 30 });

        Assert.Equal(EligibilityVerdict.Eligible, result.Verdict);
    }

    [Fact]
    public void SomebodyWhoHasAgedOutIsNotEligible()
    {
        var person = new UserProfile { DateOfBirth = DateTime.UtcNow.AddYears(-35) };

        var result = EligibilityCheck.For(person, new Eligibility { MaximumAge = 30 });

        Assert.Equal(EligibilityVerdict.NotEligible, result.Verdict);
        Assert.False(result.WorthShowing);
    }

    /// <summary>
    /// Age is counted from the date, so it cannot go stale.
    /// </summary>
    /// <remarks>
    /// The reason a date is stored rather than a number. Somebody who answers 29 is still 29 in
    /// Radar three years later, and the programmes with upper age limits are precisely the ones
    /// people age out of - so a stored age quietly recommends things that have closed to them.
    /// </remarks>
    [Fact]
    public void AgeIsDerivedFromTheDateRatherThanRemembered()
    {
        var justTurned = new UserProfile { DateOfBirth = DateTime.UtcNow.Date.AddYears(-30) };
        var almost = new UserProfile { DateOfBirth = DateTime.UtcNow.Date.AddYears(-30).AddDays(1) };

        Assert.Equal(30, justTurned.Age);
        Assert.Equal(29, almost.Age);
    }

    [Fact]
    public void NoDateOfBirthMeansNoAge()
    {
        Assert.Null(new UserProfile().Age);

        var result = EligibilityCheck.For(new UserProfile(), new Eligibility { MaximumAge = 30 });
        Assert.Equal(EligibilityVerdict.CannotTell, result.Verdict);
    }

    // ── residence ────────────────────────────────────────────────────────────

    [Fact]
    public void AStatedCountryOfResidenceIsCheckedExactly()
    {
        var here = new UserProfile { CountryOfResidence = "Nigeria" };
        var elsewhere = new UserProfile { CountryOfResidence = "Kenya" };

        var open = new Eligibility { Countries = ["Nigeria", "Ghana"] };

        Assert.Equal(EligibilityVerdict.Eligible, EligibilityCheck.For(here, open).Verdict);
        Assert.Equal(EligibilityVerdict.NotEligible, EligibilityCheck.For(elsewhere, open).Verdict);
    }

    /// <summary>
    /// One country's name inside another's does not make it eligible.
    /// </summary>
    /// <remarks>
    /// The earlier version compared with a two-way Contains, so "Niger" matched "Nigeria" and a
    /// programme open to one accepted the other. Matched whole now.
    /// </remarks>
    [Fact]
    public void ACountryWhoseNameSitsInsideAnotherIsNotConfusedWithIt()
    {
        var nigerien = new UserProfile { CountryOfResidence = "Niger" };

        var result = EligibilityCheck.For(nigerien, new Eligibility { Countries = ["Nigeria"] });

        Assert.Equal(EligibilityVerdict.NotEligible, result.Verdict);
    }

    /// <summary>A coarse region still confirms and still never denies.</summary>
    [Fact]
    public void RegionAloneCannotDenyAnything()
    {
        var vague = new UserProfile { Region = "Africa" };

        var result = EligibilityCheck.For(vague, new Eligibility { Countries = ["Nigeria"] });

        Assert.Equal(EligibilityVerdict.CannotTell, result.Verdict);
        Assert.Empty(result.Failed);
    }
}
