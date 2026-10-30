using RadarV2.Models;
using RadarV2.Services.Ingestion;

namespace RadarV2.Tests;

/// <summary>
/// What Radar decides about an opportunity it has only read the advert for.
/// </summary>
/// <remarks>
/// None of this was tested. The inference is the part that is easiest to get quietly wrong and
/// the hardest to notice from the outside: a job filed as Training is still a row in the feed, a
/// missing deadline reads as "never closes", and a date parsed in the wrong order is a real
/// deadline that silently becomes no deadline at all.
///
/// The cases below are the wording of the Nigerian job board that was added as a source, which
/// is where this market's conventions differ most from the feeds the parser was written against.
/// </remarks>
public class OpportunityIngestionTests
{
    // ── Deadlines ───────────────────────────────────────────────────────────

    /// <summary>
    /// The ordinal suffix these boards write, which stopped the match dead.
    /// </summary>
    /// <remarks>
    /// "Application Closing Date: 30th November, 2026" is close to universal on Nigerian
    /// postings. Both date shapes began with digits followed by whitespace, so the "th" meant no
    /// match and almost every job on that feed stored no deadline - which reads as never closing:
    /// no urgency on a posting that shuts in three days, and a row that never expires out of the
    /// scoring window.
    /// </remarks>
    [Theory]
    [InlineData("Application Closing Date: 30th November, 2026")]
    [InlineData("Application Deadline: 1st December 2026")]
    [InlineData("Closing Date: 22nd December, 2026")]
    [InlineData("Deadline: 3rd November 2026")]
    public void AnOrdinalDateIsStillADeadline(string text)
    {
        var found = OpportunityIngestionService.FindDeadline(text);

        Assert.NotNull(found);
        Assert.True(found > DateTime.UtcNow, $"{text} parsed to {found}.");
    }

    /// <summary>
    /// A numeric date is read the way this market writes it.
    /// </summary>
    /// <remarks>
    /// "Closes 03/12/2026" means the third of December here. Parsed as American order it became
    /// the twelfth of March - nine months early, usually already past, and then dropped by the
    /// sanity check without a word. A wrong deadline makes somebody miss a real one; this one
    /// was quietly becoming no deadline at all.
    /// </remarks>
    [Fact]
    public void ANumericDateIsReadDayFirst()
    {
        var found = OpportunityIngestionService.FindDeadline("Closes 03/12/2026");

        Assert.NotNull(found);
        Assert.Equal(12, found!.Value.Month);
        Assert.Equal(3, found.Value.Day);
    }

    /// <summary>The unambiguous shapes still parse, in both orders.</summary>
    [Theory]
    [InlineData("Deadline: 15 December 2026", 12, 15)]
    [InlineData("Application Deadline: December 15, 2026", 12, 15)]
    public void TheUsualShapesStillParse(string text, int month, int day)
    {
        var found = OpportunityIngestionService.FindDeadline(text);

        Assert.NotNull(found);
        Assert.Equal(month, found!.Value.Month);
        Assert.Equal(day, found.Value.Day);
    }

    /// <summary>
    /// No date beats a guessed one.
    /// </summary>
    /// <remarks>
    /// The rule the method was written around: a wrong deadline makes somebody miss a real
    /// chance or panic about a fake one.
    /// </remarks>
    [Theory]
    [InlineData("Apply as soon as possible")]
    [InlineData("Deadline: rolling")]
    [InlineData("Closing Date: 30th February, 2026")]
    public void NothingIsInventedWhenThereIsNoDate(string text)
    {
        Assert.Null(OpportunityIngestionService.FindDeadline(text));
    }

    /// <summary>A date already gone is not a deadline.</summary>
    [Fact]
    public void APastDateIsNotADeadline()
    {
        Assert.Null(OpportunityIngestionService.FindDeadline("Closing Date: 2nd January, 2020"));
    }

    // ── What kind of thing it is ────────────────────────────────────────────

    /// <summary>
    /// An ordinary job advert is a job.
    /// </summary>
    /// <remarks>
    /// The wording that mis-filed them. "B.Sc Degree in any course of study" appears in close to
    /// every Nigerian job posting, and "course" matched the Training keyword - so a large share
    /// of a job feed was filed as Training, which is a different thing a learner is looking for
    /// on a different day.
    /// </remarks>
    [Theory]
    [InlineData("Solevo Nigeria Limited Job Recruitment (3 Positions)",
        "Candidates should possess a B.Sc Degree in any course of study with 2 years experience.")]
    [InlineData("Rainbow Educational Service Limited Job Recruitment (6 Positions)",
        "Interested candidates should possess relevant qualifications and on-the-job training is provided.")]
    public void AJobAdvertIsAJob(string title, string body)
    {
        Assert.Equal(OpportunityType.Job, OpportunityIngestionService.InferType(title, body, OpportunityType.Job));
    }

    /// <summary>What a posting is actually about still decides it.</summary>
    [Theory]
    [InlineData("Commonwealth Scholarship 2027", "Full tuition and a stipend for postgraduate study.", OpportunityType.Scholarship)]
    [InlineData("Graduate Internship Programme", "A twelve-month internship for recent graduates.", OpportunityType.Internship)]
    [InlineData("Research Grant Call", "Funding for early-career researchers.", OpportunityType.ResearchGrant)]
    public void TheSubjectOfThePostingStillDecides(string title, string body, OpportunityType expected)
    {
        Assert.Equal(expected, OpportunityIngestionService.InferType(title, body, OpportunityType.Job));
    }

    // ── Who is offering it ──────────────────────────────────────────────────

    /// <summary>
    /// The employer, not the board the advert was found on.
    /// </summary>
    /// <remarks>
    /// With no colon and no " at ", this fell back to the feed's name - so every job carried
    /// "Hot Nigerian Jobs" as the organisation while the real employer sat in plain sight at the
    /// front of the title. A learner deciding whether to apply reads that line first.
    /// </remarks>
    [Theory]
    [InlineData("Solevo Nigeria Limited Job Recruitment (3 Positions)", "Solevo Nigeria Limited")]
    [InlineData("St. Dominic Catholic Hospital Job Recruitment (5 Positions)", "St. Dominic Catholic Hospital")]
    [InlineData("Rainbow Educational Service Limited Job Recruitment (6 Positions)", "Rainbow Educational Service Limited")]
    public void TheEmployerIsTakenFromTheTitle(string title, string expected)
    {
        Assert.Equal(expected, OpportunityIngestionService.InferOrganisation(title, "Hot Nigerian Jobs"));
    }

    /// <summary>And the board is named only when the advert does not name anybody.</summary>
    [Fact]
    public void TheBoardIsTheLastResort()
    {
        Assert.Equal(
            "Opportunity Desk",
            OpportunityIngestionService.InferOrganisation("Fully Funded Fellowships Open Now", "Opportunity Desk"));
    }
}
