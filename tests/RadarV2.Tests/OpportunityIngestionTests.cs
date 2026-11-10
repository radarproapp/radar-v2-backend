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
    /// <remarks>
    /// The dates are built from today rather than written out. They used to be spelled in full -
    /// "Deadline: 3rd November 2026" - which made the test a clock: the parser drops a deadline
    /// already in the past, so the morning the nearest of those dates went by, this failed with
    /// nothing having changed. It happened once, was fixed by asserting the parsed date instead
    /// of its futureness, and the literal dates were left in to rot a second time.
    ///
    /// What is under test is that the ordinal suffix parses at all. The year was never the point.
    /// </remarks>
    [Theory]
    [InlineData(1, "st", "Application Deadline: {0} {1}")]
    [InlineData(2, "nd", "Closing Date: {0}, {1}")]
    [InlineData(3, "rd", "Deadline: {0} {1}")]
    [InlineData(22, "nd", "Closing Date: {0}, {1}")]
    [InlineData(30, "th", "Application Closing Date: {0}, {1}")]
    public void AnOrdinalDateIsStillADeadline(int day, string suffix, string shape)
    {
        var deadline = ComfortablyAhead(day);
        var text = string.Format(shape, $"{day}{suffix} {deadline:MMMM}", $"{deadline:yyyy}");

        var found = OpportunityIngestionService.FindDeadline(text);

        Assert.NotNull(found);
        Assert.Equal(deadline, found!.Value.Date);
    }

    /// <summary>A real date far enough ahead that this is never testing the boundary by accident.</summary>
    /// <remarks>
    /// Walks forward past any month too short to hold the day, so a 30th does not land on
    /// February and throw depending on what month the suite happens to run in.
    /// </remarks>
    private static DateTime ComfortablyAhead(int day)
    {
        var candidate = DateTime.UtcNow.Date.AddMonths(2);

        while (DateTime.DaysInMonth(candidate.Year, candidate.Month) < day)
            candidate = candidate.AddMonths(1);

        return new DateTime(candidate.Year, candidate.Month, day);
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
        // Both components under thirteen, so the shape really is ambiguous and the market's own
        // convention is what decides it.
        var deadline = ComfortablyAhead(3);

        var found = OpportunityIngestionService.FindDeadline($"Closes 03/{deadline:MM}/{deadline:yyyy}");

        Assert.NotNull(found);
        Assert.Equal(deadline, found!.Value.Date);
    }

    /// <summary>
    /// And a date that can only be American order is read that way.
    /// </summary>
    /// <remarks>
    /// Reading everything day-first fixed one market and broke the other: 12/25/2026 became
    /// month 25, parsed as nothing, and a US-written posting lost a deadline it previously had
    /// right. A first component above twelve can only be a day, a second above twelve can only
    /// be a month, and only 1-12 against 1-12 is genuinely ambiguous - where this market writes
    /// the day first.
    /// </remarks>
    [Theory]
    [InlineData("Closes {month}/25/{year}")]
    [InlineData("Closes 25/{month}/{year}")]
    public void ANumericDateIsReadTheOnlyWayItCanBeMeant(string shape)
    {
        // Twenty-five cannot be a month, so both orders have exactly one reading.
        var deadline = ComfortablyAhead(25);

        var found = OpportunityIngestionService.FindDeadline(Fill(shape, deadline));

        Assert.NotNull(found);
        Assert.Equal(deadline, found!.Value.Date);
    }

    /// <summary>The unambiguous shapes still parse, in both orders.</summary>
    [Theory]
    [InlineData("Deadline: 15 {monthName} {year}")]
    [InlineData("Application Deadline: {monthName} 15, {year}")]
    public void TheUsualShapesStillParse(string shape)
    {
        var deadline = ComfortablyAhead(15);

        var found = OpportunityIngestionService.FindDeadline(Fill(shape, deadline));

        Assert.NotNull(found);
        Assert.Equal(deadline, found!.Value.Date);
    }

    /// <summary>Puts a real future date into a shape, so no test carries a date that can expire.</summary>
    private static string Fill(string shape, DateTime deadline) => shape
        .Replace("{month}", $"{deadline:MM}")
        .Replace("{monthName}", $"{deadline:MMMM}")
        .Replace("{year}", $"{deadline:yyyy}");

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
    /// A headline that names a programme is that programme, not a generic job.
    /// </summary>
    /// <remarks>
    /// The recruitment markers are an aggregator's house style and sit on everything, including
    /// "Internship Recruitment (4 Positions)". Short-circuiting on them alone filed those as
    /// ordinary jobs - invisible to the filter a learner would actually use to find them.
    /// </remarks>
    [Theory]
    [InlineData("Internship Recruitment (4 Positions) at XYZ Limited", OpportunityType.Internship)]
    [InlineData("Graduate Trainee Recruitment (5 Positions)", OpportunityType.GraduateProgramme)]
    [InlineData("Management Trainee Programme 2027", OpportunityType.GraduateProgramme)]
    [InlineData("Dangote Group Job Recruitment (12 Positions)", OpportunityType.Job)]
    public void AHeadlineThatNamesAProgrammeIsThatProgramme(string title, OpportunityType expected)
    {
        var body = "Candidates should possess a B.Sc Degree in any course of study.";

        Assert.Equal(expected, OpportunityIngestionService.InferType(title, body, OpportunityType.Job));
    }

    /// <summary>
    /// The employer is whatever comes before the earliest marker, not the first one checked.
    /// </summary>
    [Fact]
    public void TheEmployerIsCutAtTheEarliestMarker()
    {
        Assert.Equal(
            "Sahara Group",
            OpportunityIngestionService.InferOrganisation(
                "Sahara Group Vacancies - Graduate Job Recruitment (3 Positions)",
                "Hot Nigerian Jobs"));
    }

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
