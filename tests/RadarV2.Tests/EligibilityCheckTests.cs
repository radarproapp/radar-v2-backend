using RadarV2.Models;
using RadarV2.Services;

namespace RadarV2.Tests;

/// <summary>
/// Could they actually get it, as distinct from would they want it.
/// </summary>
/// <remarks>
/// The distinction the brief puts at the centre: an AI-interested student and an AI-interested
/// director both match "AI", and only one of them should see the senior role. Until this existed,
/// Eligibility recorded what a posting requires, the profile recorded what a person has, and
/// nothing compared them.
/// </remarks>
public class EligibilityCheckTests
{
    private static UserProfile Nobody() => new();

    // ── silence on either side ───────────────────────────────────────────────

    /// <summary>
    /// A posting with no stated criteria is unchecked, not open.
    /// </summary>
    /// <remarks>
    /// The rule the whole screen rests on. An opportunity with nothing recorded is not one open
    /// to everybody - it is one nobody has read the criteria for, and a screen claiming to check
    /// eligibility must not turn silence into a pass.
    /// </remarks>
    [Fact]
    public void APostingThatStatesNothingIsUncheckedRatherThanOpen()
    {
        Assert.Equal(EligibilityVerdict.Unchecked, EligibilityCheck.For(Nobody(), new Eligibility()).Verdict);
        Assert.Equal(EligibilityVerdict.Unchecked, EligibilityCheck.For(Nobody(), null).Verdict);
    }

    /// <summary>
    /// A criterion Radar has never asked about is unknown, not failed.
    /// </summary>
    /// <remarks>
    /// Nothing on the profile holds a nationality, an age or years of experience, so a posting
    /// requiring any of them cannot be checked. Treating that as a failure would hide
    /// opportunities from people who qualify; treating it as a pass would claim a check nobody
    /// made. It is reported, phrased as the question that is missing.
    /// </remarks>
    [Fact]
    public void ACriterionNobodyHasBeenAskedAboutIsReportedAsUnknown()
    {
        var result = EligibilityCheck.For(Nobody(), new Eligibility
        {
            Nationalities = ["Nigerian"],
            MinimumYearsExperience = 3,
            MinimumAge = 18,
            MaximumAge = 30,
        });

        Assert.Equal(EligibilityVerdict.CannotTell, result.Verdict);
        Assert.Equal(3, result.Unknown.Count);
        Assert.Empty(result.Failed);
        Assert.True(result.WorthShowing);
    }

    // ── qualifications ───────────────────────────────────────────────────────

    [Fact]
    public void SomebodyAboveTheQualificationFloorIsEligible()
    {
        var graduate = new UserProfile { Qualification = QualificationLevel.Masters };

        var result = EligibilityCheck.For(graduate, new Eligibility
        {
            MinimumQualification = QualificationLevel.Bachelors,
        });

        Assert.Equal(EligibilityVerdict.Eligible, result.Verdict);
        Assert.Single(result.Met);
    }

    [Fact]
    public void SomebodyBelowTheQualificationFloorIsNotEligible()
    {
        var holder = new UserProfile { Qualification = QualificationLevel.Ond };

        var result = EligibilityCheck.For(holder, new Eligibility
        {
            MinimumQualification = QualificationLevel.Bachelors,
        });

        Assert.Equal(EligibilityVerdict.NotEligible, result.Verdict);
        Assert.False(result.WorthShowing);
    }

    /// <summary>
    /// Not having said is not the same as not having it.
    /// </summary>
    /// <remarks>
    /// The failure mode that would have been silent: an unstated qualification read as "below
    /// the floor" removes every unanswered profile from every opportunity that states one.
    /// </remarks>
    [Fact]
    public void AnUnstatedQualificationIsUnknownRatherThanTooLow()
    {
        var result = EligibilityCheck.For(Nobody(), new Eligibility
        {
            MinimumQualification = QualificationLevel.Bachelors,
        });

        Assert.Equal(EligibilityVerdict.CannotTell, result.Verdict);
        Assert.Empty(result.Failed);
        Assert.True(result.WorthShowing);
    }

    // ── seniority ────────────────────────────────────────────────────────────

    /// <summary>
    /// The case the whole engine turns on.
    /// </summary>
    /// <remarks>
    /// Both match "AI". The director should not see the internship and the graduate should not
    /// see the head-of-department role, and neither of those is a question about interest.
    /// </remarks>
    [Fact]
    public void ADirectorIsNotShownAGraduateScheme()
    {
        var director = new UserProfile { SeniorityLevel = Seniority.Director };
        var graduate = new UserProfile { SeniorityLevel = Seniority.Entry };

        var scheme = new Eligibility { MaximumSeniority = Seniority.Mid };

        Assert.Equal(EligibilityVerdict.NotEligible, EligibilityCheck.For(director, scheme).Verdict);
        Assert.Equal(EligibilityVerdict.Eligible, EligibilityCheck.For(graduate, scheme).Verdict);
    }

    [Fact]
    public void AGraduateIsNotShownASeniorRole()
    {
        var graduate = new UserProfile { SeniorityLevel = Seniority.Entry };

        var senior = new Eligibility { MinimumSeniority = Seniority.Senior };

        Assert.Equal(EligibilityVerdict.NotEligible, EligibilityCheck.For(graduate, senior).Verdict);
    }

    /// <summary>
    /// An unstated seniority is unknown, and this is the trap worth naming.
    /// </summary>
    /// <remarks>
    /// Seniority is an ordered enum, so the obvious check - person >= floor - reads an
    /// unanswered profile as below entry level and removes it from everything. This model has
    /// made that mistake with a certification above Doctorate and with FreeApi at zero; the rule
    /// exists so the comparison is never written by hand again.
    /// </remarks>
    [Fact]
    public void AnUnstatedSeniorityDoesNotExcludeSomebodyFromEverything()
    {
        var result = EligibilityCheck.For(Nobody(), new Eligibility
        {
            MinimumSeniority = Seniority.Senior,
        });

        Assert.Equal(EligibilityVerdict.CannotTell, result.Verdict);
        Assert.True(result.WorthShowing);
    }

    // ── certifications ───────────────────────────────────────────────────────

    [Fact]
    public void AMissingCertificationIsAFailureAndAHeldOneIsNot()
    {
        var holder = new UserProfile { Certifications = ["PMP"] };

        Assert.Equal(
            EligibilityVerdict.Eligible,
            EligibilityCheck.For(holder, new Eligibility { RequiredCertifications = ["PMP"] }).Verdict);

        Assert.Equal(
            EligibilityVerdict.NotEligible,
            EligibilityCheck.For(holder, new Eligibility { RequiredCertifications = ["CFA"] }).Verdict);
    }

    /// <summary>
    /// A PhD does not clear a certification requirement, and a certificate does not clear a degree.
    /// </summary>
    /// <remarks>
    /// Both directions of the bug that put a certification on the qualification ladder, now
    /// asserted rather than argued. They are orthogonal: one is how far somebody got in formal
    /// education, the other is a specific thing they were assessed on.
    /// </remarks>
    [Fact]
    public void AQualificationAndACertificationAreNotInterchangeable()
    {
        var doctor = new UserProfile { Qualification = QualificationLevel.Doctorate, Certifications = [] };
        var certified = new UserProfile { Certifications = ["CompTIA A+"] };

        // A PhD with no certifications cannot clear "PMP required" - it is unknown, not a pass.
        Assert.NotEqual(
            EligibilityVerdict.Eligible,
            EligibilityCheck.For(doctor, new Eligibility { RequiredCertifications = ["PMP"] }).Verdict);

        // And a certificate does not stand in for a degree.
        Assert.NotEqual(
            EligibilityVerdict.Eligible,
            EligibilityCheck.For(certified, new Eligibility
            {
                MinimumQualification = QualificationLevel.Bachelors,
            }).Verdict);
    }

    // ── location ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Location confirms a match and never denies one.
    /// </summary>
    /// <remarks>
    /// Region is the only location fact the profile holds and it is coarse - the posting may name
    /// countries while the profile holds a continent. Denying on that mismatch would hide
    /// opportunities from people who qualify, so a region that does not appear reports unknown.
    /// </remarks>
    [Fact]
    public void AnUnmatchedRegionIsUnknownRatherThanADenial()
    {
        var here = new UserProfile { Region = "Nigeria" };
        var elsewhere = new UserProfile { Region = "Africa" };

        var open = new Eligibility { Countries = ["Nigeria", "Ghana"] };

        Assert.Equal(EligibilityVerdict.Eligible, EligibilityCheck.For(here, open).Verdict);

        var vague = EligibilityCheck.For(elsewhere, open);
        Assert.Equal(EligibilityVerdict.CannotTell, vague.Verdict);
        Assert.Empty(vague.Failed);
    }

    // ── the shape of the answer ──────────────────────────────────────────────

    /// <summary>
    /// A contradiction outranks an unknown.
    /// </summary>
    /// <remarks>
    /// Knowing one criterion fails is enough, whatever else is unanswered. The reverse ordering
    /// would let a single unasked question soften a definite no into a maybe.
    /// </remarks>
    [Fact]
    public void AKnownFailureOutranksAnUnknown()
    {
        var holder = new UserProfile { Qualification = QualificationLevel.SecondarySchool };

        var result = EligibilityCheck.For(holder, new Eligibility
        {
            MinimumQualification = QualificationLevel.Masters,
            Nationalities = ["Nigerian"],
        });

        Assert.Equal(EligibilityVerdict.NotEligible, result.Verdict);
        Assert.NotEmpty(result.Failed);
        Assert.NotEmpty(result.Unknown);
    }

    /// <summary>
    /// Only a known failure hides something.
    /// </summary>
    /// <remarks>
    /// Wrongly hiding a scholarship somebody qualifies for costs more than showing one they do
    /// not, as long as Radar says which it is rather than implying a check it did not make.
    /// </remarks>
    [Theory]
    [InlineData(EligibilityVerdict.Unchecked, true)]
    [InlineData(EligibilityVerdict.Eligible, true)]
    [InlineData(EligibilityVerdict.CannotTell, true)]
    [InlineData(EligibilityVerdict.NotEligible, false)]
    public void OnlyAKnownFailureIsWithheld(EligibilityVerdict verdict, bool shown) =>
        Assert.Equal(shown, new EligibilityResult(verdict, [], [], []).WorthShowing);

    /// <summary>Every reason is a sentence somebody could read on a card.</summary>
    [Fact]
    public void TheReasonsAreWrittenForAPersonNotALog()
    {
        var holder = new UserProfile { Qualification = QualificationLevel.Ond };

        var result = EligibilityCheck.For(holder, new Eligibility
        {
            MinimumQualification = QualificationLevel.Bachelors,
        });

        var reason = Assert.Single(result.Failed);
        Assert.Contains("bachelor's degree", reason);
        Assert.Contains("OND", reason);
        Assert.DoesNotContain("QualificationLevel", reason);
    }
}
