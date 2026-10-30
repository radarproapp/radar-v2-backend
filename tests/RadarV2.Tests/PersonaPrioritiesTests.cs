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
        var promotion = Item("What actually earns a promotion in a Nigerian bank");

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

    /// An item that speaks to nobody's stage is not penalised — it simply does not gain.
    [Fact]
    public void SomethingOffTheListIsNotPushedDown()
    {
        var plain = Item("A new proof of the Collatz bound");

        var withCategory = InterestPersonalization.ContentScore(plain, Reader(PersonaType.Student));
        var noCategory = InterestPersonalization.ContentScore(plain, new UserProfile
        {
            Persona = PersonaType.Student,
            Interests = ["Mathematics"],
            Goals = ["Get better at mathematics"],
            PrimaryGoal = "Get better at mathematics",
        });

        Assert.Equal(noCategory, withCategory);
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

    /// Terms are stems, because the matcher asks whether the text contains the term. A plural
    /// stored here would miss the singular the headline actually uses.
    [Fact]
    public void TermsAreStoredAsStemsNotPlurals()
    {
        var plural = Enum.GetValues<PersonaType>()
            .SelectMany(PersonaPriorities.For)
            .Where(term => term.EndsWith('s') && !term.EndsWith("ss"))
            .ToList();

        Assert.True(plural.Count == 0, $"plural terms will miss the singular: {string.Join(", ", plural)}");
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
