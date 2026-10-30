using RadarV2.Models;
using RadarV2.Services;

namespace RadarV2.Tests;

/// <summary>
/// The feed prioritised by category, which the spec asks for and the scorer did not do.
/// </summary>
/// <remarks>
/// Persona reached the content scorer through <c>PersonaImpact</c> alone — a dictionary written
/// by the AI enricher and by nothing else. On everything it has not reached, a student and a
/// young professional with identical interests got identical rankings, so the category collected
/// at signup had no effect on the feed it was collected for.
/// </remarks>
public class PersonaPrioritiesTests
{
    private static UserProfile Reader(PersonaType persona) => new()
    {
        Persona = persona,
        // Chosen, not defaulted. Without this the category does not score at all, which is the
        // point of the test below.
        PersonaChosenAt = new DateTime(2026, 10, 30, 0, 0, 0, DateTimeKind.Utc),
        Interests = ["Mathematics"],
        Goals = ["Get better at mathematics"],
        PrimaryGoal = "Get better at mathematics",
    };

    private static ContentItem Item(string title, string topic = "Mathematics") => new()
    {
        Title = title,
        Topic = topic,
    };

    /// The reported gap: the same item, the same interests, two stages, one ranking.
    [Fact]
    public void TheSameScholarshipRanksHigherForTheStudentThanForTheProfessional()
    {
        var scholarship = Item("NNPC scholarship applications open for undergraduates");

        var student = InterestPersonalization.ContentScore(scholarship, Reader(PersonaType.Student));
        var professional = InterestPersonalization.ContentScore(scholarship, Reader(PersonaType.YoungProfessional));

        Assert.True(student > professional, $"student {student}, professional {professional}");
    }

    /// And the other way round, so this is a priority rather than a thumb on one scale.
    [Fact]
    public void ThePromotionPieceRanksHigherForTheProfessionalThanForTheStudent()
    {
        var promotion = Item("What actually earns career advancement in a Nigerian bank");

        var professional = InterestPersonalization.ContentScore(promotion, Reader(PersonaType.YoungProfessional));
        var student = InterestPersonalization.ContentScore(promotion, Reader(PersonaType.Student));

        Assert.True(professional > student, $"professional {professional}, student {student}");
    }

    [Fact]
    public void AGraduateSchemeRanksHigherForTheGraduate()
    {
        var scheme = Item("Dangote opens its graduate trainee programme");

        var graduate = InterestPersonalization.ContentScore(scheme, Reader(PersonaType.Graduate));
        var student = InterestPersonalization.ContentScore(scheme, Reader(PersonaType.Student));

        Assert.True(graduate > student, $"graduate {graduate}, student {student}");
    }

    /// <summary>
    /// A category nobody chose does not rank, and does not get named as a reason.
    /// </summary>
    /// <remarks>
    /// PersonaType's default is a real value - Student - and signing up writes a profile without
    /// touching the field. So every new account read as a student, and once the category started
    /// shaping the ranking it also started saying "a priority at your stage" about a stage nobody
    /// had stated. Radar asks; it does not decide on somebody's behalf and then tell them it was
    /// their answer.
    /// </remarks>
    [Fact]
    public void ACategoryNobodyChoseDoesNotRank()
    {
        var scholarship = Item("NNPC scholarship applications open for undergraduates");

        var neverAsked = new UserProfile
        {
            // Persona left at its default, as signing up leaves it.
            Interests = ["Mathematics"],
            Goals = ["Get better at mathematics"],
            PrimaryGoal = "Get better at mathematics",
        };

        Assert.False(neverAsked.HasChosenCategory);

        var explained = InterestPersonalization.Explain(neverAsked, scholarship);

        Assert.DoesNotContain(explained.MatchedSignals, s => s.StartsWith($"{PersonaPriorities.Dimension}:", StringComparison.Ordinal));
        Assert.True(
            explained.Score < InterestPersonalization.ContentScore(scholarship, Reader(PersonaType.Student)),
            "a chosen category should rank above one that was never stated");
    }

    /// <summary>A stored category that is not the default is proof of a choice.</summary>
    ///
    /// Student is what the enum falls back to, so nothing but an explicit answer can make a
    /// profile read as a Graduate. Reading the date alone would have called every profile that
    /// predates the field unanswered - re-asked a Graduate with nothing selected, and let one
    /// tap turn them into a Student while keeping the Graduate's answers.
    [Theory]
    [InlineData(PersonaType.Graduate, true)]
    [InlineData(PersonaType.YoungProfessional, true)]
    [InlineData(PersonaType.Entrepreneur, true)]
    [InlineData(PersonaType.Researcher, true)]
    // The one genuinely unknowable case, and the only one Radar asks about.
    [InlineData(PersonaType.Student, false)]
    public void AStoredCategoryThatIsNotTheDefaultCountsAsChosen(PersonaType stored, bool chosen)
    {
        Assert.Equal(chosen, new UserProfile { Persona = stored }.HasChosenCategory);
    }

    /// <summary>The other route in, which gating the terms alone left open.</summary>
    ///
    /// An enriched item carries a PersonaImpact entry per stage and was worth five points for a
    /// match. Gating the twelve-point dimension and leaving this one ungated meant the same
    /// unstated claim was still being made, five points at a time - and invisibly, because this
    /// dimension is filtered out of the signals the card shows.
    [Fact]
    public void AnEnrichedItemDoesNotScoreForACategoryNobodyChose()
    {
        var enriched = Item("A piece the enricher reached");
        enriched.PersonaImpact["Student"] = "Worth knowing before your finals.";

        var neverAsked = new UserProfile
        {
            Interests = ["Mathematics"],
            Goals = ["Get better at mathematics"],
            PrimaryGoal = "Get better at mathematics",
        };

        Assert.Equal(
            InterestPersonalization.ContentScore(Item("A piece the enricher reached"), neverAsked),
            InterestPersonalization.ContentScore(enriched, neverAsked));

        // And still scores for somebody who did choose.
        Assert.True(
            InterestPersonalization.ContentScore(enriched, Reader(PersonaType.Student))
            > InterestPersonalization.ContentScore(Item("A piece the enricher reached"), Reader(PersonaType.Student)));
    }

    /// What the reader typed still wins. The category is a box they ticked; the goal is a
    /// sentence they wrote, and a feed that put the stage first would be guessing over them.
    [Fact]
    public void AStatedGoalOutranksTheCategory()
    {
        var reader = new UserProfile
        {
            Persona = PersonaType.Student,
            Interests = ["Mathematics"],
            Goals = ["Become an actuary"],
            PrimaryGoal = "Become an actuary",
        };

        var theirGoal = InterestPersonalization.ContentScore(Item("How to become an actuary in Nigeria", "Actuarial science"), reader);
        var theirStage = InterestPersonalization.ContentScore(Item("Chevron scholarship now open", "Funding"), reader);

        Assert.True(theirGoal > theirStage, $"goal {theirGoal}, stage {theirStage}");
    }

    /// An item that speaks to nobody's stage does not record a category at all.
    ///
    /// The first version of this compared one profile against another built field for field the
    /// same way, which is f(x) == f(x) and would have passed at any weight including a thousand.
    [Fact]
    public void SomethingOffTheListRecordsNoCategory()
    {
        var plain = Item("A new proof of the Collatz bound");

        var signals = InterestPersonalization.Explain(Reader(PersonaType.Student), plain).MatchedSignals;

        Assert.DoesNotContain(signals, signal => signal.StartsWith($"{PersonaPriorities.Dimension}:", StringComparison.Ordinal));
    }

    /// <summary>
    /// A term has to appear as itself, not as letters inside another word.
    /// </summary>
    /// <remarks>
    /// Every one of these was a real twelve-point match before the terms were bounded, and each
    /// was then printed to the reader as the reason the item was in front of them. "graduate job"
    /// lost its second word to the four-letter floor and became a bare search for "graduate",
    /// which undergraduate and postgraduate both contain.
    /// </remarks>
    [Theory]
    [InlineData(PersonaType.Graduate, "UNILAG undergraduate enrolment rises 12% as more school leavers apply")]
    [InlineData(PersonaType.Graduate, "Nigeria graduated 500,000 students last year")]
    [InlineData(PersonaType.Graduate, "Oil industry reported record profits in Q3")]
    [InlineData(PersonaType.Graduate, "Rising demand for skilled welders in Lekki free zone")]
    [InlineData(PersonaType.Entrepreneur, "Seed yields improve around smallholder farms in Kano")]
    [InlineData(PersonaType.Entrepreneur, "Business owners say migrant workers are being granted permits faster")]
    [InlineData(PersonaType.Entrepreneur, "An adventure tourism push in the capital")]
    [InlineData(PersonaType.YoungProfessional, "Coca-Cola launches promotional campaign for the festive season")]
    [InlineData(PersonaType.YoungProfessional, "Remote sensing framework maps Lagos flood risk")]
    [InlineData(PersonaType.YoungProfessional, "Training a large language model now needs petabyte-scale data management")]
    [InlineData(PersonaType.Researcher, "Researchers were granted access to the new telescope")]
    [InlineData(PersonaType.Researcher, "Court recalls judge over leaked papers")]
    // Nigerian news, which is what this feed actually carries. Every one of these matched.
    [InlineData(PersonaType.Student, "CBN unveils new foreign exchange programme for bureau de change operators")]
    [InlineData(PersonaType.Student, "NAFDAC revokes certification of 12 bottled water firms in Lagos")]
    [InlineData(PersonaType.Student, "Nigeria receives WHO certification for polio-free status")]
    [InlineData(PersonaType.Student, "Deeper Life Bible Church fellowship holds annual convention in Lagos")]
    [InlineData(PersonaType.Graduate, "INEC begins certification of election results in Rivers")]
    [InlineData(PersonaType.Researcher, "Nigeria submits country report to the African Peer Review Mechanism")]
    [InlineData(PersonaType.YoungProfessional, "NFF reviews contract awarded to Super Eagles coach Finidi George")]
    [InlineData(PersonaType.YoungProfessional, "Nigeria and Russia sign strategic partnership on nuclear power")]
    [InlineData(PersonaType.YoungProfessional, "APC holds leadership development retreat for ward chairmen")]
    [InlineData(PersonaType.YoungProfessional, "Senate probes industry practices behind rising drug prices")]
    [InlineData(PersonaType.YoungProfessional, "Microsoft launches new business intelligence tools for African SMEs")]
    [InlineData(PersonaType.Entrepreneur, "Tunde Onakoya, co-founder of Chess in Slums, breaks Guinness World Record")]
    public void LettersInsideAnotherWordAreNotAMatch(PersonaType persona, string headline)
    {
        Assert.Null(PersonaPriorities.Matched(headline.ToLowerInvariant(), persona));
    }

    /// And the forms a headline actually uses still match.
    [Theory]
    [InlineData(PersonaType.Student, "chevron scholarships now open for 200 level students", "scholarship")]
    [InlineData(PersonaType.Student, "applications open for summer internships at mtn", "internship")]
    [InlineData(PersonaType.Graduate, "dangote opens its graduate trainee programme", "graduate trainee")]
    [InlineData(PersonaType.Graduate, "entry-level roles are drying up in lagos fintech", "entry-level")]
    [InlineData(PersonaType.YoungProfessional, "what actually earns career advancement in a nigerian bank", "career advancement")]
    [InlineData(PersonaType.Researcher, "tetfund opens research grants for 2027", "research grant")]
    public void TheFormsAHeadlineActuallyUsesStillMatch(PersonaType persona, string headline, string expected)
    {
        Assert.Equal(expected, PersonaPriorities.Matched(headline, persona));
    }

    /// The longest match is the one reported, so the reason names the specific thing.
    [Fact]
    public void TheMostSpecificTermIsTheOneNamed()
    {
        Assert.Equal(
            "graduate trainee",
            PersonaPriorities.Matched("shell graduate trainee programme opens", PersonaType.Graduate));
    }

    /// Every category has a list. One without would silently lose the whole dimension.
    [Theory]
    [InlineData(PersonaType.Student)]
    [InlineData(PersonaType.Graduate)]
    [InlineData(PersonaType.YoungProfessional)]
    [InlineData(PersonaType.Entrepreneur)]
    [InlineData(PersonaType.Researcher)]
    public void EveryStageHasSomethingItIsHereFor(PersonaType persona)
    {
        Assert.NotEmpty(PersonaPriorities.For(persona));
    }

    /// <summary>
    /// Every term can actually fire.
    /// </summary>
    /// <remarks>
    /// A term that matches nothing is invisible: the dimension simply does not score, and no
    /// test of a headline would notice the one entry that never contributes. Stray whitespace
    /// or a character the pattern treats specially is all it takes.
    ///
    /// An earlier rule here banned plurals, on the theory that a stored "scholarships" would
    /// miss "scholarship". Bounded growth handles that now, and "bursaries" is irregular enough
    /// to be worth listing in both forms.
    /// </remarks>
    [Fact]
    public void EveryTermMatchesItself()
    {
        var dead = Enum.GetValues<PersonaType>()
            .SelectMany(PersonaPriorities.For)
            .Where(term => !PersonaPriorities.Mentions($"something about {term} in Lagos", term))
            .ToList();

        Assert.True(dead.Count == 0, $"terms that can never fire: {string.Join(", ", dead)}");
    }

    /// <summary>
    /// What the reader stated leads the reason, even though the category outscores an interest.
    /// </summary>
    /// <remarks>
    /// WhyShown prints the first two signals. Ordered by score alone, the category (12) displaces
    /// a general interest (10), so a student reading about the subject they typed would be told
    /// only "a priority at your stage" - the stage they ticked named ahead of the subject they
    /// wrote. The score and the sentence are allowed to disagree about order; they are answering
    /// different questions.
    /// </remarks>
    [Fact]
    public void TheReaderOwnWordsLeadTheReason()
    {
        var item = Item("Chevron scholarship opens for mathematics undergraduates", "Mathematics");

        var why = WhyShown.Build(item, InterestPersonalization.Explain(Reader(PersonaType.Student), item).MatchedSignals);

        Assert.StartsWith("Matches your interest in Mathematics", why, StringComparison.Ordinal);
    }

    /// The reason is sayable. A ranking the reader cannot be told the cause of is the thing this
    /// product refuses to produce.
    [Fact]
    public void TheCategoryIsNamedAsTheReason()
    {
        var why = WhyShown.Build(
            Item("NNPC scholarship applications open"),
            InterestPersonalization.Explain(Reader(PersonaType.Student), Item("NNPC scholarship applications open")).MatchedSignals);

        Assert.Contains("scholarship", why, StringComparison.OrdinalIgnoreCase);
    }
}
