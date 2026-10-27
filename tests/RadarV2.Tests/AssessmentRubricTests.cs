using RadarV2.Models;
using RadarV2.Services;

namespace RadarV2.Tests;

/// <summary>
/// The criteria are authored, which is the point: a rubric is a statement about what good work
/// looks like and belongs somewhere reviewable.
///
/// Asking a model to invent the criteria and apply them in one call means nobody knows what the
/// judgement was against, and the criteria drift between two runs on the same essay. So these
/// test the part that must not drift.
/// </summary>
public class AssessmentRubricTests
{
    /// <summary>
    /// An unidentified document gets no criteria, so nothing is judged against a guessed rubric.
    /// Picking a default is how a CV gets told its argument is underdeveloped.
    /// </summary>
    [Fact]
    public void AnUnknownDocumentHasNothingToBeJudgedAgainst()
    {
        Assert.Empty(AssessmentRubric.For(DocumentKind.Unknown, PersonaType.Student));
    }

    [Theory]
    [InlineData(DocumentKind.Essay)]
    [InlineData(DocumentKind.Report)]
    [InlineData(DocumentKind.Cv)]
    [InlineData(DocumentKind.CoverLetter)]
    [InlineData(DocumentKind.ResearchProposal)]
    [InlineData(DocumentKind.PersonalStatement)]
    [InlineData(DocumentKind.BusinessPlan)]
    public void EveryKnownKindHasCriteria(DocumentKind kind)
    {
        var criteria = AssessmentRubric.For(kind, PersonaType.Student);

        Assert.NotEmpty(criteria);
        Assert.All(criteria, c =>
        {
            Assert.False(string.IsNullOrWhiteSpace(c.Name));
            // Every criterion states what it asks of the document, because the reader sees it.
            Assert.False(string.IsNullOrWhiteSpace(c.Asks));
        });
    }

    /// <summary>
    /// Persona changes the criteria, not the tone. A graduate's CV is read for whether it
    /// evidences a role they have not held; a professional's for progression and scope. Telling
    /// both the same things is how generic feedback happens.
    /// </summary>
    [Fact]
    public void ACvIsJudgedDifferentlyForAGraduateAndAProfessional()
    {
        var graduate = AssessmentRubric.For(DocumentKind.Cv, PersonaType.Graduate).Select(c => c.Name).ToList();
        var professional = AssessmentRubric.For(DocumentKind.Cv, PersonaType.YoungProfessional).Select(c => c.Name).ToList();

        Assert.NotEqual(graduate, professional);
        Assert.Contains("Projects carry weight", graduate);
        Assert.Contains("Scope and progression", professional);
    }

    // Referencing is a student criterion. A professional's report is not marked on citation style.
    [Fact]
    public void OnlyAStudentEssayIsMarkedOnReferencing()
    {
        var student = AssessmentRubric.For(DocumentKind.Essay, PersonaType.Student).Select(c => c.Name);
        var professional = AssessmentRubric.For(DocumentKind.Essay, PersonaType.YoungProfessional).Select(c => c.Name);

        Assert.Contains("Referencing", student);
        Assert.DoesNotContain("Referencing", professional);
    }

    // The shared criteria still apply to everybody; persona adds and reorders, it does not replace.
    [Fact]
    public void TheCoreEssayCriteriaApplyToEveryPersona()
    {
        foreach (var persona in Enum.GetValues<PersonaType>())
        {
            var names = AssessmentRubric.For(DocumentKind.Essay, persona).Select(c => c.Name).ToList();

            Assert.Contains("A position", names);
            Assert.Contains("Evidence", names);
            Assert.Contains("Analysis", names);
        }
    }

    /// <summary>
    /// No criterion carries a score, a weight or a mark. The verdict is one of three words
    /// against a criterion the reader can see, rather than a number against a rubric they cannot.
    /// </summary>
    [Fact]
    public void NoCriterionCarriesAScore()
    {
        foreach (var kind in Enum.GetValues<DocumentKind>())
        {
            foreach (var criterion in AssessmentRubric.For(kind, PersonaType.Student))
            {
                Assert.Contains(criterion.Verdict, new[] { "Met", "Partly", "NotMet", "Unjudged" });
                Assert.DoesNotContain("%", criterion.Asks);
                Assert.DoesNotContain("out of", criterion.Asks);
            }
        }
    }

    /// <summary>
    /// Criteria ask something answerable rather than asserting a quality.
    /// </summary>
    /// <remarks>
    /// Contains a question mark rather than ends with one: several criteria ask and then say why
    /// it matters — "Is it clear what role this is for? A CV aimed at everything reads as aimed
    /// at nothing." The second sentence is the useful half, so the assertion checks the question
    /// is there, not that nothing follows it.
    /// </remarks>
    [Fact]
    public void EveryCriterionAsksSomethingAnswerable()
    {
        foreach (var kind in Enum.GetValues<DocumentKind>())
        {
            foreach (var criterion in AssessmentRubric.For(kind, PersonaType.Graduate))
            {
                Assert.Contains("?", criterion.Asks);
            }
        }
    }
}
