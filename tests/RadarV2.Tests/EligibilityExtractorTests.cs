using RadarV2.Models;
using RadarV2.Services.Ingestion;

namespace RadarV2.Tests;

/// <summary>
/// Reading what a posting states, and refusing to read what it does not.
/// </summary>
/// <remarks>
/// Both ingesters left Eligibility empty on purpose - "inventing them from a summary is how
/// people end up applying for things they do not qualify for" - and the cost was that the
/// eligibility half of the engine had no input at all. A comparator with nothing to compare
/// decides nothing.
///
/// The two directions are not symmetrical, and that asymmetry is the whole design. A missed
/// criterion costs somebody a listing they would have been shown anyway, because the verdict
/// falls back to Unchecked or CannotTell and both still display. A wrongly read criterion hides
/// an opportunity from somebody who qualifies, silently. So roughly half the tests below exist to
/// prove this records nothing.
/// </remarks>
public class EligibilityExtractorTests
{
    private static Eligibility Read(string body) => EligibilityExtractor.From(null, body);

    // ── qualifications ───────────────────────────────────────────────────────

    [Theory]
    [InlineData("Applicants must have a bachelor's degree in any discipline.", QualificationLevel.Bachelors)]
    [InlineData("Minimum of a B.Sc in Computer Science.", QualificationLevel.Bachelors)]
    [InlineData("A first degree is required.", QualificationLevel.Bachelors)]
    [InlineData("Candidates must possess an HND.", QualificationLevel.Hnd)]
    [InlineData("OND required.", QualificationLevel.Ond)]
    [InlineData("Master's degree or higher.", QualificationLevel.Masters)]
    [InlineData("A PhD is required for this role.", QualificationLevel.Doctorate)]
    public void AStatedQualificationFloorIsRead(string body, QualificationLevel expected) =>
        Assert.Equal(expected, Read(body).MinimumQualification);

    /// <summary>
    /// A degree the programme awards is not a degree it requires.
    /// </summary>
    /// <remarks>
    /// The mistake that would read a posting exactly backwards. A scholarship funding a master's
    /// asks for a bachelor's; taking every mention of "master's" as a floor would exclude the
    /// precise people it exists for, and those people would never know.
    /// </remarks>
    [Theory]
    [InlineData("Scholarship for a master's degree at a UK university.")]
    [InlineData("Fully funded PhD programme in Economics.")]
    [InlineData("This fellowship supports doctoral research.")]
    [InlineData("A master's degree programme open to African students.")]
    public void ADegreeTheProgrammeAwardsIsNotReadAsOneItRequires(string body) =>
        Assert.Null(Read(body).MinimumQualification);

    /// <summary>Where two are offered, the floor is the lower one.</summary>
    [Fact]
    public void TheLowestStatedQualificationIsTheFloor()
    {
        var read = Read("Minimum of HND or B.Sc required in a relevant field.");

        Assert.Equal(QualificationLevel.Hnd, read.MinimumQualification);
    }

    // ── experience ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData("A minimum of 5 years experience in the field.", 5)]
    [InlineData("At least 3 years of relevant experience.", 3)]
    [InlineData("Not less than 2 years experience required.", 2)]
    public void AStatedExperienceFloorIsRead(string body, int expected) =>
        Assert.Equal(expected, Read(body).MinimumYearsExperience);

    /// <summary>
    /// The ceiling, which is what makes a graduate scheme a graduate scheme.
    /// </summary>
    /// <remarks>
    /// The direction most likely to be forgotten and the one this product most needs: "no more
    /// than two years" is the rule that excludes a six-year professional on purpose.
    /// </remarks>
    [Theory]
    [InlineData("Open to graduates with no more than 2 years experience.", 2)]
    [InlineData("Candidates with less than 3 years experience are encouraged to apply.", 3)]
    [InlineData("Maximum of 1 year experience.", 1)]
    public void AStatedExperienceCeilingIsRead(string body, int expected) =>
        Assert.Equal(expected, Read(body).MaximumYearsExperience);

    [Fact]
    public void ARangeSetsBothEnds()
    {
        var read = Read("We are looking for someone with 3-5 years experience.");

        Assert.Equal(3, read.MinimumYearsExperience);
        Assert.Equal(5, read.MaximumYearsExperience);
    }

    /// <summary>
    /// A number beside the word is not a statement about experience.
    /// </summary>
    /// <remarks>
    /// Years, salaries and quantities all appear as digits in a job posting. A figure this picked
    /// up by accident becomes a floor somebody is measured against.
    /// </remarks>
    [Theory]
    [InlineData("Founded in 2015, the company has 200 years of combined team experience.")]
    [InlineData("Salary of 500,000 naira. Experience in fintech preferred.")]
    [InlineData("The ideal candidate will be experienced and self-motivated.")]
    [InlineData("Experience with Python and 3 other languages.")]
    public void ANumberThatIsNotAnExperienceRequirementIsIgnored(string body)
    {
        var read = Read(body);

        Assert.Null(read.MinimumYearsExperience);
        Assert.Null(read.MaximumYearsExperience);
    }

    // ── age ──────────────────────────────────────────────────────────────────

    [Fact]
    public void AStatedAgeRangeIsRead()
    {
        var read = Read("Open to young Africans aged 18-35.");

        Assert.Equal(18, read.MinimumAge);
        Assert.Equal(35, read.MaximumAge);
    }

    [Theory]
    [InlineData("Applicants must be under 30 years old.", 30)]
    [InlineData("Maximum age of 35 years.", 35)]
    public void AStatedAgeCeilingIsRead(string body, int expected) =>
        Assert.Equal(expected, Read(body).MaximumAge);

    /// <summary>
    /// A year is not an age.
    /// </summary>
    /// <remarks>
    /// An invented age bound is the worst of all of these, because it is the one criterion
    /// nobody can satisfy by trying harder. Bounded to a plausible human range on the way in.
    /// </remarks>
    [Theory]
    [InlineData("Applications close under 2030 funding rules.")]
    [InlineData("The 2024-2025 cohort is now open.")]
    [InlineData("Up to 500 participants will be selected.")]
    public void AFigureThatIsNotAnAgeIsIgnored(string body)
    {
        var read = Read(body);

        Assert.Null(read.MinimumAge);
        Assert.Null(read.MaximumAge);
    }

    // ── nationality ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Open to Nigerian nationals only.", "Nigerian")]
    [InlineData("Applicants must be Ghanaian citizens.", "Ghanaian")]
    [InlineData("Restricted to Kenyan passport holders.", "Kenyan")]
    public void AnExplicitNationalityRestrictionIsRead(string body, string expected) =>
        Assert.Contains(expected, Read(body).Nationalities);

    /// <summary>
    /// Mentioning a country says nothing about nationality.
    /// </summary>
    /// <remarks>
    /// The brief names this one specifically, and on a Nigerian job board nearly every posting
    /// mentions Nigeria. Reading that as a restriction would attach a nationality rule to almost
    /// everything - and a wrongly recorded nationality hides a posting from exactly the person it
    /// was meant for.
    /// </remarks>
    [Theory]
    [InlineData("A great opportunity based in Lagos, Nigeria.")]
    [InlineData("The Nigerian technology sector is growing fast.")]
    [InlineData("This is an international programme open to all.")]
    [InlineData("Work with Nigerian startups across Africa.")]
    public void MerelyMentioningACountryIsNotARestriction(string body) =>
        Assert.Empty(Read(body).Nationalities);

    /// <summary>A capitalised word that is not a demonym is never a nationality.</summary>
    /// <remarks>
    /// Why the list is closed. An open capture would read "must be Senior candidates" as a
    /// nationality, and every posting it touched would then be hidden from everybody.
    /// </remarks>
    [Theory]
    [InlineData("Applicants must be Senior candidates with leadership experience.")]
    [InlineData("Open to Graduate applicants.")]
    [InlineData("Must be Microsoft certified.")]
    public void ACapitalisedWordThatIsNotADemonymIsNotANationality(string body) =>
        Assert.Empty(Read(body).Nationalities);

    // ── the whole thing ──────────────────────────────────────────────────────

    /// <summary>
    /// A posting that states nothing produces an eligibility that says so.
    /// </summary>
    /// <remarks>
    /// Which is exactly what the ingesters stored before this existed, so a posting this cannot
    /// read is no worse off than it was.
    /// </remarks>
    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("Join our growing team. Competitive salary and great culture.")]
    public void APostingThatStatesNothingIsStillUnstated(string? body) =>
        Assert.False(EligibilityExtractor.From(null, body).WasStated);

    [Fact]
    public void ARealisticPostingIsReadInFull()
    {
        var read = EligibilityExtractor.From(
            "Graduate Trainee Programme 2027",
            "We are recruiting graduate trainees. Applicants must have a bachelor's degree, "
            + "no more than 2 years experience, and be under 28 years old. "
            + "Open to Nigerian nationals only. Apply before 31 March 2027.");

        Assert.True(read.WasStated);
        Assert.Equal(QualificationLevel.Bachelors, read.MinimumQualification);
        Assert.Equal(2, read.MaximumYearsExperience);
        Assert.Equal(28, read.MaximumAge);
        Assert.Equal(["Nigerian"], read.Nationalities);
    }

    /// <summary>
    /// What is read is what the check can then act on.
    /// </summary>
    /// <remarks>
    /// The point of the whole exercise, asserted end to end rather than trusted: the extractor
    /// feeds the comparator, and the comparator reaches a verdict that is not Unchecked. Without
    /// this pairing both halves pass their own tests while the feature does nothing.
    /// </remarks>
    [Fact]
    public void WhatIsExtractedIsWhatTheCheckDecidesOn()
    {
        var eligibility = EligibilityExtractor.From(
            "Graduate Programme",
            "Applicants must have a bachelor's degree and no more than 2 years experience.");

        var tooExperienced = new UserProfile
        {
            Qualification = QualificationLevel.Bachelors,
            YearsOfExperience = 7,
        };

        var graduate = new UserProfile
        {
            Qualification = QualificationLevel.Bachelors,
            YearsOfExperience = 1,
        };

        // CannotTell rather than NotEligible, and that is the fix rather than a regression.
        // Criteria Radar read out of prose can inform and cannot exclude: a review ran this
        // extractor over real job-board text and it invented an age limit from "up to 20 hours
        // per week" and an experience floor from company boilerplate, each of which hid a
        // posting from somebody who qualified. The reasons still render; they are simply not
        // acted on behind the reader's back.
        var tooMany = RadarV2.Services.EligibilityCheck.For(tooExperienced, eligibility);

        Assert.Equal(RadarV2.Services.EligibilityVerdict.CannotTell, tooMany.Verdict);
        Assert.NotEmpty(tooMany.Failed);
        Assert.True(tooMany.WorthShowing);

        Assert.Equal(
            RadarV2.Services.EligibilityVerdict.Eligible,
            RadarV2.Services.EligibilityCheck.For(graduate, eligibility).Verdict);
    }
}
