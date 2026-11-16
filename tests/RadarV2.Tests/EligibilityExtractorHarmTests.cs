using RadarV2.Models;
using RadarV2.Services;
using RadarV2.Services.Ingestion;

namespace RadarV2.Tests;

/// <summary>
/// The postings this extractor used to hide from the people they were for.
/// </summary>
/// <remarks>
/// Every input below was executed against the first version of the extractor and produced a
/// criterion the posting never stated. These are not hypotheticals: a review ran them, and each
/// one ended in NotEligible, which the feed drops silently.
///
/// The original tests passed - 44 of them - because the negative cases happened to be shapes that
/// did not match. "Up to 500 participants" was clean only because the number has three digits;
/// "up to 50 participants" recorded an age limit of fifty. Half of those tests also passed
/// against a stub that returned nothing at all.
///
/// So these are written from the failures rather than from the design, and they are paired with
/// the structural rule that makes the whole class of error survivable: an extracted criterion can
/// inform and can never exclude.
/// </remarks>
public class EligibilityExtractorHarmTests
{
    private static Eligibility Read(string body) => EligibilityExtractor.From(null, body);

    /// <summary>
    /// A quantity is not an age.
    /// </summary>
    /// <remarks>
    /// The age ceiling pattern ended in an alternation that allowed no unit at all, so it matched
    /// immediately after any two digits. Every one of these recorded an age limit, and an age
    /// limit hides the posting from everybody who has given a date of birth.
    /// </remarks>
    [Theory]
    [InlineData("Application closing date: not more than 14 days from the date of this publication.")]
    [InlineData("Internship is up to 20 hours per week.")]
    [InlineData("Under 25 percent of applicants are invited to interview.")]
    [InlineData("Up to 50 applicants will be shortlisted for the aptitude test.")]
    [InlineData("Salary up to 60 million naira per annum.")]
    [InlineData("Deliveries under 30 minutes guaranteed.")]
    [InlineData("Shortlist of not more than 12 candidates.")]
    public void AQuantityIsNotAnAgeLimit(string body)
    {
        var read = Read(body);

        Assert.Null(read.MaximumAge);
        Assert.Null(read.MinimumAge);
    }

    /// <summary>
    /// Words ending in "age" are not the word "age".
    /// </summary>
    /// <remarks>
    /// average, package, coverage, percentage, language. "Average 40-45 hours per week" in a
    /// graduate advert recorded an age range of 40 to 45, which hid it from every graduate.
    /// </remarks>
    [Theory]
    [InlineData("Average 35-40 hours per week.")]
    [InlineData("Coverage 18-35 states nationwide.")]
    [InlineData("Package 15 to 20 percent above market rate.")]
    [InlineData("On average 20 to 30 candidates are interviewed each week.")]
    public void AWordEndingInAgeIsNotAnAgeRange(string body)
    {
        var read = Read(body);

        Assert.Null(read.MinimumAge);
        Assert.Null(read.MaximumAge);
    }

    /// <summary>
    /// A company's history is not the applicant's experience.
    /// </summary>
    /// <remarks>
    /// This boilerplate opens a very large share of Nigerian job adverts, so it was probably the
    /// highest-volume defect of the lot. It recorded an experience floor AND an age floor from
    /// the same phrase.
    /// </remarks>
    [Theory]
    [InlineData("We are a leading indigenous firm with over 20 years of experience in facility management.")]
    [InlineData("ABC Limited, with over 15 years of experience in the Nigerian market, is recruiting a Sales Officer.")]
    [InlineData("Our team brings at least 15 years of combined experience.")]
    [InlineData("Established over 30 years ago, we deliver unmatched experience to clients.")]
    public void CompanyBoilerplateIsNotAnApplicantRequirement(string body)
    {
        var read = Read(body);

        Assert.Null(read.MinimumAge);
        Assert.Null(read.MinimumYearsExperience);
    }

    /// <summary>
    /// A sentence saying a degree is not needed does not require one.
    /// </summary>
    /// <remarks>
    /// The clearest inversion of the lot: the posting says the opposite of what was stored, and
    /// the people it was widening access for were the ones it excluded.
    /// </remarks>
    [Theory]
    [InlineData("A university degree is not required for this role.")]
    [InlineData("No B.Sc required; candidates with proven driving experience will be considered.")]
    [InlineData("A first degree is not required, but is an advantage.")]
    [InlineData("Candidates without a bachelor degree are required to submit a portfolio.")]
    public void ADenialIsNotARequirement(string body) =>
        Assert.Null(Read(body).MinimumQualification);

    /// <summary>
    /// A requirement cue near a degree does not make the degree the requirement.
    /// </summary>
    /// <remarks>
    /// The windows bound across clauses, so "required" bound to whatever degree word was nearby -
    /// a referee's PhD, a transcript, a preference.
    /// </remarks>
    [Theory]
    [InlineData("At least one referee must hold a PhD.")]
    [InlineData("Applicants are required to attach a CV, cover letter and master transcript.")]
    [InlineData("Minimum of 3 years experience; B.Sc holders preferred.")]
    [InlineData("Required: strong Excel, good writing, and PhD-level curiosity about data.")]
    public void ADegreeMentionedNearARequirementCueIsNotTheRequirement(string body) =>
        Assert.Null(Read(body).MinimumQualification);

    /// <summary>
    /// What a programme awards is not what it asks for, even when it is phrased as a requirement.
    /// </summary>
    /// <remarks>
    /// The original tests covered only the uncued phrasings. Every cued one failed, and this is
    /// the failure that reads a scholarship exactly backwards - excluding the bachelor's holders
    /// a master's scholarship exists for.
    /// </remarks>
    [Theory]
    [InlineData("Applicants must have been admitted to a master degree programme.")]
    [InlineData("The scholarship requires applicants to be enrolled in a PhD programme.")]
    [InlineData("Minimum funding of 5000 USD per year towards a master degree.")]
    public void AnAwardedDegreeIsNotARequiredOne(string body) =>
        Assert.Null(Read(body).MinimumQualification);

    /// <summary>The duration of a thing is not the applicant's experience.</summary>
    [Theory]
    [InlineData("The fellowship runs for a minimum of 2 years and offers hands-on experience in policy.")]
    [InlineData("Contract duration is 1 to 2 years with the chance to gain experience.")]
    public void TheLengthOfAProgrammeIsNotAnExperienceRequirement(string body)
    {
        var read = Read(body);

        Assert.Null(read.MinimumYearsExperience);
        Assert.Null(read.MaximumYearsExperience);
    }

    /// <summary>
    /// A stated floor is not overwritten by a range elsewhere in the posting.
    /// </summary>
    /// <remarks>
    /// The range used to set both bounds unconditionally, destroying a real eight-year floor and
    /// inventing a two-year ceiling - excluding precisely the senior candidate the advert wanted.
    /// </remarks>
    [Fact]
    public void ARangeDoesNotOverwriteAStatedFloor()
    {
        var read = Read("Candidates need at least 8 years experience; the scheme offers 1-2 years of experience to new joiners.");

        Assert.Equal(8, read.MinimumYearsExperience);
        Assert.NotEqual(2, read.MaximumYearsExperience);
    }

    /// <summary>
    /// A list of nationalities is read whole, or not at all.
    /// </summary>
    /// <remarks>
    /// Only the demonym next to the cue was captured, so a scholarship open to three countries
    /// recorded one - and hid itself from the two it had just named.
    /// </remarks>
    [Fact]
    public void EveryNationalityInTheClauseIsRead()
    {
        var read = Read("Open to Nigerian citizens, Ghanaian citizens and Kenyan citizens.");

        Assert.Contains("Nigerian", read.Nationalities);
        Assert.Contains("Ghanaian", read.Nationalities);
        Assert.Contains("Kenyan", read.Nationalities);
    }

    /// <summary>
    /// A posting that excludes a nationality is not recorded as open to it.
    /// </summary>
    /// <remarks>
    /// Exactly inverted: the one group named as excluded became the only group allowed.
    /// </remarks>
    [Theory]
    [InlineData("This programme is not open to Nigerian nationals.")]
    [InlineData("This scholarship is not restricted to Ghanaian citizens.")]
    public void ADeniedNationalityIsNotRecordedAsAGrant(string body) =>
        Assert.Empty(Read(body).Nationalities);

    /// <summary>The title does not run into the body.</summary>
    /// <remarks>
    /// Joined with a space, a match ran across the boundary - "Massive Recruitment: Up to 25
    /// Positions" produced an age limit of twenty-five from a title about headcount.
    /// </remarks>
    [Fact]
    public void AMatchDoesNotRunFromTheTitleIntoTheBody()
    {
        var read = EligibilityExtractor.From(
            "Minimum Wage Review Officer",
            "Bachelor of Science in Accounting is what we award.");

        Assert.Null(read.MinimumQualification);
    }

    /// <summary>
    /// A very long body does not throw, and does not take a feed down with it.
    /// </summary>
    /// <remarks>
    /// The RSS path passes the whole body, uncapped. 198KB of it pushed a pattern past its
    /// timeout - and that exception escaped the entry loop, so the failing entry blocked every
    /// older entry in that feed on every later run.
    /// </remarks>
    [Fact]
    public void AVeryLongPostingIsReadWithoutThrowing()
    {
        var huge = string.Concat(Enumerable.Repeat("required ", 25_000));

        var read = EligibilityExtractor.From("A role", huge);

        Assert.NotNull(read);
    }

    // ── the rule that makes all of this survivable ───────────────────────────

    /// <summary>
    /// An extracted criterion can inform and can never exclude.
    /// </summary>
    /// <remarks>
    /// The structural half, and the reason the fixes above are not the whole answer. Better
    /// patterns help; they will still sometimes misread somebody else's prose, and the cost of
    /// being wrong falls entirely on the person who never sees what they lost.
    ///
    /// So a posting Radar read for itself is capped at CannotTell. The reader is still told what
    /// it asks for and what could not be confirmed - they simply are not acted on behind their
    /// back.
    /// </remarks>
    [Fact]
    public void AnExtractedCriterionNeverHidesAnOpportunity()
    {
        var extracted = new Eligibility
        {
            Source = EligibilitySource.Extracted,
            MinimumQualification = QualificationLevel.Masters,
        };

        var holder = new UserProfile { Qualification = QualificationLevel.SecondarySchool };

        var result = EligibilityCheck.For(holder, extracted);

        Assert.Equal(EligibilityVerdict.CannotTell, result.Verdict);
        Assert.True(result.WorthShowing);

        // Still told what the posting asks for. Capped, not silenced.
        Assert.NotEmpty(result.Failed);
    }

    /// <summary>
    /// A stated criterion still excludes, which is the point of recording provenance.
    /// </summary>
    /// <remarks>
    /// Criteria somebody asserted on purpose - a curator, or a source that publishes them as
    /// structured data - are trusted. The cap applies to Radar's own reading of prose, not to
    /// the model.
    /// </remarks>
    [Fact]
    public void AStatedCriterionStillExcludes()
    {
        var stated = new Eligibility
        {
            Source = EligibilitySource.Stated,
            MinimumQualification = QualificationLevel.Masters,
        };

        var holder = new UserProfile { Qualification = QualificationLevel.SecondarySchool };

        Assert.Equal(EligibilityVerdict.NotEligible, EligibilityCheck.For(holder, stated).Verdict);
    }

    /// <summary>Everything ingestion writes is marked as extracted.</summary>
    /// <remarks>
    /// The cap is worthless if the extractor forgets to say where the criteria came from, and
    /// Stated is the default on the model - so a missing assignment fails open, in the direction
    /// that hides things.
    /// </remarks>
    [Fact]
    public void TheExtractorAlwaysMarksItsOwnWork()
    {
        Assert.Equal(EligibilitySource.Extracted, EligibilityExtractor.From("A role", "Anything").Source);
        Assert.Equal(EligibilitySource.Extracted, EligibilityExtractor.From(null, null).Source);
    }
}
