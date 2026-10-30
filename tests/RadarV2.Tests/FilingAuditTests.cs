using RadarV2.Models;
using RadarV2.Services;

namespace RadarV2.Tests;

/// <summary>
/// Finding the documents that are filed in the wrong place.
/// </summary>
/// <remarks>
/// Radar sorts things as it ingests them and the sorting has been wrong before - an opportunity
/// typed from its body so every posting on a job board read as a Job, a research paper typed as
/// an Article and shown in the news feed. Each was fixed at the point of writing, and fixing the
/// writer does not fix the rows. A scholarship filed as a job is invisible to the student
/// looking for scholarships however good the matching above it becomes.
///
/// The bar these tests hold is not "does it find things". It is that the audit never moves
/// anything it is not sure about, because this is the input to a job that rewrites a live
/// collection while nobody is watching.
/// </remarks>
public class FilingAuditTests
{
    private static Opportunity Posting(string title, OpportunityType type, string body = "") => new()
    {
        Id = "o1",
        Title = title,
        Description = body,
        Type = type,
    };

    private static ContentItem Item(ContentType type, ContentLayer layer, string title = "A thing happened") => new()
    {
        Id = "c1",
        Title = title,
        Type = type,
        Layer = layer,
    };

    // ── opportunity type ─────────────────────────────────────────────────────

    /// <summary>The reported shape: a scholarship stored as a job.</summary>
    /// <remarks>
    /// Invisible to the one student it was collected for. The type is what the scholarships
    /// filter reads, so no amount of improvement to the matching above it helps.
    /// </remarks>
    [Fact]
    public void AScholarshipStoredAsAJobIsFound()
    {
        var finding = FilingAudit.AuditType(
            Posting("NNPC/Chevron National University Scholarship 2027", OpportunityType.Job));

        Assert.Equal(FilingAudit.Verdict.Misfiled, finding.Verdict);
        Assert.Equal("Scholarship", finding.ShouldBe);
    }

    /// <summary>Something already in the right place is left entirely alone.</summary>
    [Theory]
    [InlineData("Graduate trainee programme, Dangote", OpportunityType.GraduateProgramme)]
    [InlineData("Software Engineer, backend", OpportunityType.Job)]
    [InlineData("Summer internship, Lagos", OpportunityType.Internship)]
    public void SomethingCorrectlyFiledIsNotTouched(string title, OpportunityType type)
    {
        Assert.Equal(FilingAudit.Verdict.CorrectlyFiled, FilingAudit.AuditType(Posting(title, type)).Verdict);
    }

    /// <summary>
    /// A posting with no title is not re-typed from its body alone.
    /// </summary>
    /// <remarks>
    /// The body is what mis-typed every posting on a job board in the first place: a scholarship
    /// listing's body is full of the language of applying for work. The inference trusts the
    /// title, so with no title there is nothing to be confident from.
    /// </remarks>
    [Fact]
    public void APostingWithNoTitleIsLeftAlone()
    {
        var finding = FilingAudit.AuditType(
            Posting("", OpportunityType.Job, "Applicants must hold a degree and submit a CV."));

        Assert.Equal(FilingAudit.Verdict.Unclear, finding.Verdict);
    }

    /// <summary>Every finding says which rule produced it and what it read.</summary>
    /// <remarks>
    /// A sweep that moves a thousand documents and cannot say which rule moved each one cannot be
    /// reviewed and cannot be undone.
    /// </remarks>
    [Fact]
    public void EveryFindingCarriesItsRuleAndItsEvidence()
    {
        var finding = FilingAudit.AuditType(Posting("PhD Scholarship in Public Health", OpportunityType.Job));

        Assert.False(string.IsNullOrWhiteSpace(finding.Rule));
        Assert.False(string.IsNullOrWhiteSpace(finding.Evidence));
        Assert.Equal("o1", finding.DocumentId);
    }

    // ── open or closed ───────────────────────────────────────────────────────

    private static readonly DateTime Now = new(2026, 10, 30, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>Section 21: a closed opportunity is never presented as open.</summary>
    [Fact]
    public void ADeadlineThatHasPassedIsFound()
    {
        var closed = Posting("Chevron scholarship", OpportunityType.Scholarship);
        closed.Deadline = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
        closed.Status = "Open";

        var finding = FilingAudit.AuditOpenness(closed, Now);

        Assert.Equal(FilingAudit.Verdict.Misfiled, finding.Verdict);
        Assert.Equal("Closed", finding.ShouldBe);
    }

    /// <summary>
    /// Closed before its deadline is left closed, because a posting can be withdrawn.
    /// </summary>
    /// <remarks>
    /// The dangerous direction. Reopening on a date would put a withdrawn posting back in front
    /// of people and send them to apply for something that is gone - the audit undoing a true
    /// fact because it only knows one of the reasons something closes.
    /// </remarks>
    [Fact]
    public void SomethingClosedEarlyIsNotReopened()
    {
        var withdrawn = Posting("A post that was pulled", OpportunityType.Job);
        withdrawn.Deadline = new DateTime(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc);
        withdrawn.Status = "Closed";

        var finding = FilingAudit.AuditOpenness(withdrawn, Now);

        Assert.Equal(FilingAudit.Verdict.Unclear, finding.Verdict);
        Assert.Equal("Closed", finding.ShouldBe);
    }

    /// <summary>An opportunity with no deadline is not closed for lacking one.</summary>
    [Fact]
    public void NoDeadlineIsNotAClosedDeadline()
    {
        var rolling = Posting("Rolling applications", OpportunityType.Job);
        rolling.Status = "Open";

        Assert.Equal(FilingAudit.Verdict.CorrectlyFiled, FilingAudit.AuditOpenness(rolling, Now).Verdict);
    }

    // ── which surface ────────────────────────────────────────────────────────

    [Fact]
    public void APaperAndAnArticleEachSitOnOneSurface()
    {
        Assert.Equal(FilingAudit.Verdict.CorrectlyFiled, FilingAudit.AuditSurface(Item(ContentType.ResearchPaper, ContentLayer.Science)).Verdict);
        Assert.Equal(FilingAudit.Verdict.CorrectlyFiled, FilingAudit.AuditSurface(Item(ContentType.Article, ContentLayer.Skills)).Verdict);
    }

    /// <summary>
    /// Something on no surface is stored and unreachable, and nobody can tell.
    /// </summary>
    /// <remarks>
    /// The quiet half, and the reason a report is worth running on a night when nothing is
    /// moved. A document that matches neither surface filter is served by no screen - it is not
    /// missing, it is not broken, it simply never appears, and no error is raised anywhere.
    /// </remarks>
    [Fact]
    public void SomethingOnNoSurfaceIsReported()
    {
        var orphan = Item(ContentType.Video, ContentLayer.Science);

        var finding = FilingAudit.AuditSurface(orphan);

        Assert.Equal(FilingAudit.Verdict.Unclear, finding.Verdict);
        Assert.Contains("no surface", finding.Evidence, StringComparison.OrdinalIgnoreCase);
    }

    // ── news that is really an advert ────────────────────────────────────────

    /// <summary>A posting in the news feed is a deadline nothing is tracking.</summary>
    [Fact]
    public void APostingFiledAsNewsIsFound()
    {
        var advert = Item(ContentType.Article, ContentLayer.Skills, "Shell graduate programme");
        advert.WhatHappened = "Applications are now open. The closing date is 14 December. How to apply is set out below.";

        var finding = FilingAudit.AuditIsReallyAnOpportunity(advert);

        Assert.Equal(FilingAudit.Verdict.Misfiled, finding.Verdict);
        Assert.Equal("Opportunity", finding.ShouldBe);
    }

    /// <summary>
    /// Reporting about an opportunity is still reporting.
    /// </summary>
    /// <remarks>
    /// The mirror image of the bug, and the easier mistake to make: an article explaining that a
    /// scholarship scheme exists belongs in the feed. Dragging it into the opportunities list
    /// would put something nobody can apply for in front of somebody looking for something to
    /// apply for.
    /// </remarks>
    [Theory]
    [InlineData("The federal government says it will fund more scholarships next year.")]
    [InlineData("Researchers applied a new method to the same dataset and got a different answer.")]
    [InlineData("Applied mathematics enrolment rose 12% across the south-west.")]
    [InlineData("The minister applied pressure on the agency over hiring delays.")]
    public void ReportingAboutOpportunitiesStaysInTheFeed(string body)
    {
        var news = Item(ContentType.Article, ContentLayer.Policy, "Education funding");
        news.WhatHappened = body;

        Assert.NotEqual(FilingAudit.Verdict.Misfiled, FilingAudit.AuditIsReallyAnOpportunity(news).Verdict);
    }

    /// <summary>
    /// One mark is not enough to move anything.
    /// </summary>
    /// <remarks>
    /// "Deadline" alone appears in reporting about deadlines. Requiring two independent marks is
    /// what keeps the news feed from being emptied into the opportunities list.
    /// </remarks>
    [Fact]
    public void OneMarkOfAnAdvertIsNotEnoughToMoveIt()
    {
        var news = Item(ContentType.Article, ContentLayer.Policy, "Budget talks");
        news.WhatHappened = "The deadline for the budget negotiations passed without agreement.";

        Assert.Equal(FilingAudit.Verdict.Unclear, FilingAudit.AuditIsReallyAnOpportunity(news).Verdict);
    }
}
