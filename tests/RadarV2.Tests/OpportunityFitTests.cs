using RadarV2.Models;
using RadarV2.Services;

namespace RadarV2.Tests;

/// <summary>
/// Which kinds of opportunity suit which kind of person.
/// </summary>
/// <remarks>
/// This was a switch expression inside a 160-line scoring method, and what it left out could not
/// be seen: Graduate appeared in it nowhere, so every graduate fell to the no-match default while
/// GraduateProgramme matched nobody at all.
///
/// A table can be read. It can also be checked, which is what this does.
/// </remarks>
public class OpportunityFitTests
{
    private static readonly PersonaType[] EveryPersona =
        Enum.GetValues<PersonaType>();

    /// <summary>
    /// A graduate is matched on the things a graduate applies for.
    /// </summary>
    [Fact]
    public void AGraduateIsMatchedOnGraduateOpenings()
    {
        foreach (var type in new[]
                 {
                     OpportunityType.GraduateProgramme,
                     OpportunityType.Internship,
                     OpportunityType.Fellowship,
                     OpportunityType.Job,
                 })
        {
            Assert.True(
                OpportunityFit.IsASignal(PersonaType.Graduate, type),
                $"A graduate got no signal from {type}.");
        }
    }

    /// <summary>
    /// Every persona has something written for it.
    /// </summary>
    /// <remarks>
    /// The specific failure: a persona nobody wrote a line for is not obviously broken from the
    /// outside. The feed still fills, the scores still differ, and the reason is that one of five
    /// kinds of person is being matched on nothing but the words in the advert.
    /// </remarks>
    [Fact]
    public void EveryPersonaHasAtLeastOneKindOfOpportunityWrittenForIt()
    {
        foreach (var persona in EveryPersona)
        {
            var matched = Enum.GetValues<OpportunityType>()
                .Count(type => OpportunityFit.PointsFor(persona, type) == OpportunityFit.Stage);

            Assert.True(matched > 0, $"Nothing is written for {persona}.");
        }
    }

    /// <summary>
    /// The things anybody can attend count for everybody.
    /// </summary>
    /// <remarks>
    /// Named in the spec: conferences, seminars, workshops, job fairs. They scored four points -
    /// the no-match default - for every persona at once, so a student looking for a job fair and
    /// a professional looking for the same job fair were both told it was a poor match.
    ///
    /// Where somebody is in a career changes which job they apply for. It does not change whether
    /// a workshop in their field is worth a Saturday.
    /// </remarks>
    [Theory]
    [InlineData(OpportunityType.Conference)]
    [InlineData(OpportunityType.Seminar)]
    [InlineData(OpportunityType.Workshop)]
    [InlineData(OpportunityType.JobFair)]
    public void AnyoneCanBeOfferedTheThingsAnyoneCanAttend(OpportunityType type)
    {
        foreach (var persona in EveryPersona)
        {
            Assert.True(
                OpportunityFit.IsASignal(persona, type),
                $"A {persona} got no signal from a {type}.");
        }
    }

    /// <summary>
    /// A seminar and a job fair exist as types at all.
    /// </summary>
    /// <remarks>
    /// Both are named in the spec and neither was in the enum, so a seminar was filed as a
    /// generic Event and a job fair had nowhere to go. Nothing could filter for either.
    /// </remarks>
    [Fact]
    public void TheTypesTheSpecNamesExist()
    {
        foreach (var named in new[]
                 {
                     "Job", "Internship", "Fellowship", "ResearchGrant", "Funding",
                     "GraduateProgramme", "Scholarship", "Conference", "Seminar", "Workshop",
                     "JobFair",
                 })
        {
            Assert.True(
                Enum.TryParse<OpportunityType>(named, out _),
                $"There is no opportunity type for {named}.");
        }
    }

    /// <summary>
    /// The types that were already stored keep the values they were stored with.
    /// </summary>
    /// <remarks>
    /// Persisted by value, so a type inserted in the middle of the list relabels every
    /// opportunity already in the database — a Job becomes an Internship and nobody notices
    /// until somebody reads their feed.
    /// </remarks>
    [Fact]
    public void TheExistingTypesKeepTheirStoredValues()
    {
        Assert.Equal(0, (int)OpportunityType.Job);
        Assert.Equal(1, (int)OpportunityType.Internship);
        Assert.Equal(14, (int)OpportunityType.Funding);
    }

    /// <summary>
    /// A type nobody wrote a rule for is not evidence against an opportunity.
    /// </summary>
    [Fact]
    public void AnUnwrittenTypeStillScoresSomething()
    {
        Assert.True(OpportunityFit.PointsFor(PersonaType.Student, OpportunityType.Funding) > 0);
    }

    // ── What the learner actually said ──────────────────────────────────────

    private const string NursingPost =
        "Graduate Nurse Trainee Programme. We are recruiting registered nurses into a two-year "
        + "rotation across paediatrics and critical care. Start date: January.";

    /// <summary>A stated interest is matched on the words an advert actually uses.</summary>
    /// <remarks>
    /// The learner types "Nursing" and the posting says "Nurse". As a prefix, "nursing" matches
    /// neither "nurse" nor "nurses", so the unlisted fields - the ones with no synonym list to
    /// fall back on - were exactly the ones that matched nothing.
    /// </remarks>
    [Fact]
    public void AStatedInterestMatchesTheWordsTheAdvertUses()
    {
        Assert.Contains("Nursing", OpportunityFit.Matching(NursingPost, ["Nursing"]));
    }

    /// <summary>
    /// And matched by word, not by letters appearing somewhere.
    /// </summary>
    /// <remarks>
    /// The old match was a plain Contains over the whole advert, lowercased. An interest in art
    /// matched every posting with a "start date" in it, and the reader was then told the reason
    /// it was shown was their interest in art. The same substring rule, in the feed, is what put
    /// football in a prompt-engineering path.
    /// </remarks>
    [Fact]
    public void ALetterSequenceInsideAnotherWordIsNotAMatch()
    {
        Assert.Empty(OpportunityFit.Matching(NursingPost, ["art"]));
    }

    /// <summary>
    /// Goals count, which they did not.
    /// </summary>
    /// <remarks>
    /// Goals were in the profile, used by the roadmap and by the Intelligence feed, and read in
    /// the opportunity scorer only as a fallback source of a single domain string. So somebody
    /// whose stated goal was a scholarship was never matched on it.
    /// </remarks>
    [Fact]
    public void AStatedGoalIsMatchedToo()
    {
        var post = "Commonwealth Scholarship for postgraduate study in the United Kingdom.";

        // The term returned is the one the learner typed, because that is what gets said back to
        // them as the reason this is in front of them.
        Assert.Contains("win a scholarship", OpportunityFit.Matching(post, ["win a scholarship"]));
    }

    /// <summary>
    /// An opportunity with nothing to do with the learner matches nothing.
    /// </summary>
    /// <remarks>
    /// This is the one that matters. Every opportunity started at 48 points with a floor of 38,
    /// so one matching nothing still came back at about half and sat in the Explore tier, and the
    /// daily batch padded itself to twelve from whatever was left. Somebody studying nursing was
    /// shown accelerator programmes because the list was long enough to fill a page.
    /// </remarks>
    [Fact]
    public void AnUnrelatedOpportunityMatchesNothing()
    {
        var accelerator =
            "Seed-stage startup accelerator. Twelve weeks of mentorship and $120,000 for "
            + "pre-product fintech founders in Lagos.";

        Assert.Empty(OpportunityFit.Matching(accelerator, ["Nursing", "paediatrics"]));
    }

    /// <summary>Nothing stated is nothing matched, rather than everything matched.</summary>
    [Fact]
    public void ALearnerWhoStatedNothingMatchesNothing()
    {
        Assert.Empty(OpportunityFit.Matching(NursingPost, []));
        Assert.Empty(OpportunityFit.Matching(NursingPost, ["", "   ", null]));
    }

    /// <summary>The same term twice is one reason, not two.</summary>
    [Fact]
    public void ATermStatedTwiceIsOneReason()
    {
        Assert.Single(OpportunityFit.Matching(NursingPost, ["Nursing", "nursing"]));
    }

    // ── Measured on production ──────────────────────────────────────────────

    /// <summary>
    /// A course-level word does not match a funder with the same word in its name.
    /// </summary>
    /// <remarks>
    /// The exact shape that reached production. A learner whose path was "foundations of
    /// Mathematics" was shown twelve opportunities at 84 per cent, every one of them from a
    /// Foundation - the Heinrich Böll Foundation, the Jack Kent Cooke Foundation, the ACMER
    /// Foundation - because "foundations" stems to "foundation" and that is in the funder's name.
    ///
    /// Each one carried the sentence "It is foundations of Mathematics work, which is what you
    /// are learning." A false match is bad; a false match that explains itself confidently is
    /// what the reader actually loses trust over.
    /// </remarks>
    [Fact]
    public void AFoundationIsNotTheFoundationsOfAnything()
    {
        var fellowship =
            "Heinrich Böll Foundation Feminist Futures Fellowship. The Foundation invites "
            + "applications from activists working on gender justice.";

        Assert.Empty(OpportunityFit.Matching(fellowship, ["foundations of Mathematics"]));
    }

    /// <summary>And the subject itself still matches.</summary>
    [Fact]
    public void TheSubjectUnderTheScaffoldingStillMatches()
    {
        var post = "Teaching assistant in mathematics, supporting undergraduate tutorials.";

        Assert.Contains(
            "foundations of Mathematics",
            OpportunityFit.Matching(post, ["foundations of Mathematics"]));
    }

    /// <summary>
    /// The same rule across the words that frame a course rather than name it.
    /// </summary>
    [Theory]
    [InlineData("Introduction to Law", "An introduction to our graduate scheme.")]
    [InlineData("Principles of Accounting", "We work on first principles here.")]
    [InlineData("Advanced Nursing Practice", "Advanced applications are open until Friday.")]
    public void FramingWordsDoNotCarryAMatch(string path, string advert)
    {
        Assert.Empty(OpportunityFit.Matching(advert, [path]));
    }

    /// <summary>
    /// And a short subject is matched on itself rather than on its framing.
    /// </summary>
    /// <remarks>
    /// "Introduction to Law" has nothing in it of four letters or more except the framing word,
    /// because the matcher drops short words - so the fallback matched this path against any
    /// advert mentioning an introduction. Three letters is enough for a whole-word match: "law"
    /// matches law and not lawn.
    /// </remarks>
    [Fact]
    public void AShortSubjectIsMatchedOnItself()
    {
        var post = "Legal intern, commercial law team, Lagos.";

        Assert.Contains("Introduction to Law", OpportunityFit.Matching(post, ["Introduction to Law"]));
    }

    /// <summary>
    /// A subject that is nothing but framing words still matches on them.
    /// </summary>
    /// <remarks>
    /// Dropping every word would leave nothing to match, which would hide content from somebody
    /// whose stated interest Radar had privately decided was too vague. That is not a judgement
    /// it is entitled to make without saying so.
    /// </remarks>
    [Fact]
    public void ASubjectMadeOnlyOfFramingWordsStillMatches()
    {
        Assert.Contains(
            "Foundations",
            OpportunityFit.Matching("The Foundations programme opens in March.", ["Foundations"]));
    }

    // ── A word that merely starts the same way ──────────────────────────────

    /// <summary>
    /// A longer word that happens to begin with the subject is a different word.
    /// </summary>
    /// <remarks>
    /// The match was an open-ended prefix: a word had to start a word, and then anything could
    /// follow. So "Bank" matched "bankruptcy", and "mass communication" matched "Massachusetts
    /// Institute of Technology" through its first word. Both were named as past failures and both
    /// were still live.
    ///
    /// Three letters of growth is the limit now, which covers the endings an advert actually
    /// uses - nurse, nurses, nursing - and not six more letters of a different noun.
    /// </remarks>
    [Theory]
    [InlineData("Banking", "Insolvency and bankruptcy paralegal, two-year contract.")]
    [InlineData("mass communication", "Massachusetts Institute of Technology summer school.")]
    [InlineData("public policies", "Nigeria Police Force recruitment drive, apply by Friday.")]
    public void AWordThatMerelyStartsTheSameWayIsNotAMatch(string stated, string advert)
    {
        Assert.Empty(OpportunityFit.Matching(advert, [stated]));
    }

    /// <summary>
    /// The endings an advert does use still match.
    /// </summary>
    /// <remarks>
    /// The learner types one form and the posting uses another: nursing against "nurse",
    /// engineering against "engineer", policies against "policy". Bounding the growth must not
    /// cost those, which is the whole reason the stem exists.
    /// </remarks>
    [Theory]
    [InlineData("Nursing", "Graduate Nurse Residency Programme, paediatrics.")]
    [InlineData("Engineering", "Graduate engineer programme, two rotations.")]
    [InlineData("public policies", "Public policy analyst, think tank, Abuja.")]
    [InlineData("Banking", "Retail banking graduate trainee, Lagos.")]
    public void TheFormsAnAdvertActuallyUsesStillMatch(string stated, string advert)
    {
        Assert.Contains(stated, OpportunityFit.Matching(advert, [stated]));
    }

    /// <summary>
    /// Every type the spec names for a stage scores for that stage.
    /// </summary>
    /// <remarks>
    /// Both of these were in the written list and absent from the table, and the absence is
    /// invisible by inspection: the default is four points rather than an error, so the type
    /// simply ranked as though it said nothing about the reader. A student's exchange fellowship
    /// and a graduate's pitch competition are both named in the spec for those stages.
    /// </remarks>
    [Theory]
    [InlineData(PersonaType.Student, OpportunityType.Fellowship)]
    [InlineData(PersonaType.Student, OpportunityType.Scholarship)]
    [InlineData(PersonaType.Student, OpportunityType.Internship)]
    [InlineData(PersonaType.Student, OpportunityType.Competition)]
    [InlineData(PersonaType.Graduate, OpportunityType.Competition)]
    [InlineData(PersonaType.Graduate, OpportunityType.GraduateProgramme)]
    [InlineData(PersonaType.Graduate, OpportunityType.Job)]
    [InlineData(PersonaType.Graduate, OpportunityType.Scholarship)]
    public void TheTypesTheSpecNamesForAStageScoreForIt(PersonaType persona, OpportunityType type)
    {
        Assert.Equal(OpportunityFit.Stage, OpportunityFit.PointsFor(persona, type));
    }
}
