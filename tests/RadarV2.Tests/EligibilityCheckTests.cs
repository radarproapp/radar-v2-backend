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

    /// <summary>
    /// A certification Radar has not been told about is unknown, never a denial.
    /// </summary>
    /// <remarks>
    /// Nobody enumerates every credential they hold, so the list is not a closed world and "not
    /// in it" is an absent fact rather than a contradiction.
    ///
    /// The asymmetry this replaces punished answering: having listed nothing reported unknown
    /// and was shown, having listed anything reported failed and was hidden - on the inference
    /// "holds some certification, therefore not this one". Somebody who answered honestly was
    /// treated worse than somebody who skipped the question.
    /// </remarks>
    [Fact]
    public void AHeldCertificationQualifiesAndAnUnlistedOneIsNotHeldAgainstYou()
    {
        var holder = new UserProfile { Certifications = ["PMP"] };

        Assert.Equal(
            EligibilityVerdict.Eligible,
            EligibilityCheck.For(holder, new Eligibility { RequiredCertifications = ["PMP"] }).Verdict);

        var other = EligibilityCheck.For(holder, new Eligibility { RequiredCertifications = ["CFA"] });

        Assert.Equal(EligibilityVerdict.CannotTell, other.Verdict);
        Assert.Empty(other.Failed);
        Assert.True(other.WorthShowing);

        var silent = EligibilityCheck.For(new UserProfile(), new Eligibility { RequiredCertifications = ["CFA"] });
        Assert.Equal(EligibilityVerdict.CannotTell, silent.Verdict);
    }

    /// <summary>
    /// A posting whose only criterion is skills does not come back as a bare "Eligible".
    /// </summary>
    /// <remarks>
    /// RequiredSkills counts towards WasStated, so the check started and then matched nothing:
    /// every branch returned early and the verdict fell through to Eligible with no met, failed
    /// or unknown lines behind it. A confident yes against a posting nothing was compared to is
    /// the worst answer this can give, because it is the one somebody acts on.
    /// </remarks>
    [Fact]
    public void APostingThatOnlyAsksForSkillsIsNotSilentlyApproved()
    {
        var result = EligibilityCheck.For(
            new UserProfile(),
            new Eligibility { RequiredSkills = ["Python", "Kubernetes"] });

        Assert.Equal(EligibilityVerdict.CannotTell, result.Verdict);
        Assert.NotEmpty(result.Unknown);
    }

    /// <summary>Overlapping skills are evidence, and a gap is never a denial.</summary>
    /// <remarks>
    /// A posting listing five skills rarely requires all five, so treating a gap as
    /// ineligibility would hide almost everything from almost everybody. Judging skill against a
    /// required level is Radar Learn's competency model, which is not built.
    /// </remarks>
    [Fact]
    public void SharedSkillsAreEvidenceAndMissingOnesAreNot()
    {
        var analyst = new UserProfile { Capabilities = ["Data Analysis", "Research"] };

        var overlap = EligibilityCheck.For(
            analyst, new Eligibility { RequiredSkills = ["Data Analysis", "Programming"] });

        Assert.Equal(EligibilityVerdict.Eligible, overlap.Verdict);
        Assert.NotEmpty(overlap.Met);

        var none = EligibilityCheck.For(analyst, new Eligibility { RequiredSkills = ["Welding"] });

        Assert.Equal(EligibilityVerdict.CannotTell, none.Verdict);
        Assert.Empty(none.Failed);
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

        // A PhD does not clear "PMP required" - it is unknown, not a pass.
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
