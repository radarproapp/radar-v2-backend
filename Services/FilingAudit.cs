using System.Text.RegularExpressions;
using RadarV2.Models;
using RadarV2.Services.Ingestion;

namespace RadarV2.Services;

/// <summary>
/// Whether a stored document is filed where it belongs, and where it belongs if not.
/// </summary>
/// <remarks>
/// Radar sorts things as it ingests them, and the sorting has been wrong before: an opportunity
/// typed from its body rather than its title so every posting on a job board read as a Job; a
/// research paper typed as an Article and therefore shown in the news feed. Each of those was
/// fixed at the point of writing, and fixing the writer does not fix the rows - the documents
/// already stored keep whatever they were given, which is the recurring shape of this product's
/// bugs. A scholarship filed as a job is invisible to the student searching for scholarships
/// however good the matching above it gets.
///
/// This is the deterministic half: given a document, say where it should be. It holds no
/// database and makes no changes, so it can be run over a corpus to produce a report, tested
/// exhaustively, and argued with - all of which matter before anything is allowed to rewrite a
/// live collection overnight.
///
/// Two rules throughout. Only a confident, deterministic signal counts: where the evidence is
/// thin the answer is <see cref="Verdict.Unclear"/> and the document is left alone, because a
/// sweep that guesses at three in the morning is worse than one that does nothing. And nothing
/// here consults a model - a nightly job that spends money per document is a different kind of
/// problem, and an unattended model deciding where data lives is not something to be able to
/// audit after the fact.
/// </remarks>
public static class FilingAudit
{
    /// <summary>What an audit concluded about one document.</summary>
    public enum Verdict
    {
        /// <summary>Where it is, is where it belongs.</summary>
        CorrectlyFiled,

        /// <summary>Demonstrably in the wrong place, with somewhere definite to go.</summary>
        Misfiled,

        /// <summary>Something is off, but not definitely enough to move it.</summary>
        Unclear,
    }

    /// <summary>
    /// One finding, in the terms a person reviewing a nightly report needs.
    /// </summary>
    /// <remarks>
    /// <paramref name="Rule"/> and <paramref name="Evidence"/> are not decoration. A sweep that
    /// moves a thousand documents and cannot say which rule moved each one, or why, cannot be
    /// reviewed and cannot be undone - and the first bad rule would quietly rewrite the corpus.
    /// </remarks>
    public sealed record Finding(
        string DocumentId,
        Verdict Verdict,
        string Rule,
        string Was,
        string ShouldBe,
        string Evidence);

    // ── opportunities ────────────────────────────────────────────────────────

    /// <summary>
    /// Whether an opportunity's type matches what its own wording says it is.
    /// </summary>
    /// <remarks>
    /// Re-runs the same inference that files a new one, so the audit and the ingester cannot
    /// disagree about what a scholarship is. A stored type that the inference does not reproduce
    /// is either a posting that was mis-typed before the inference was fixed, or a posting the
    /// inference cannot read - and those are different, which is why the second is Unclear.
    /// </remarks>
    public static Finding AuditType(Opportunity opp)
    {
        // Checked before anything is compared. The inference trusts the title and falls back to
        // the stored type when it has nothing to read - so a posting with no title agrees with
        // itself and would report as verified, which is a reassurance the audit has not earned.
        // Saying it could not tell is the honest answer and keeps it out of the moved pile.
        if (string.IsNullOrWhiteSpace(opp.Title))
            return new Finding(opp.Id, Verdict.Unclear, "opportunity-type", opp.Type.ToString(), opp.Type.ToString(), "No title to read, so nothing here confirms or contradicts the stored type.");

        var inferred = OpportunityIngestionService.InferType(opp.Title, opp.Description ?? string.Empty, opp.Type);

        if (inferred == opp.Type)
            return new Finding(opp.Id, Verdict.CorrectlyFiled, "opportunity-type", opp.Type.ToString(), opp.Type.ToString(), "The stored type is what the wording says.");

        return new Finding(
            opp.Id,
            Verdict.Misfiled,
            "opportunity-type",
            opp.Type.ToString(),
            inferred.ToString(),
            $"The title reads as {inferred}: \"{Shorten(opp.Title)}\"");
    }

    /// <summary>
    /// Whether an opportunity that has closed is still presented as open.
    /// </summary>
    /// <remarks>
    /// Section 21: an opportunity whose deadline has passed is removed from active
    /// recommendations or labelled closed, and never presented as open. Closed, not deleted -
    /// somebody who applied last month should still find it in their tracker, and a deadline
    /// that has passed is a fact about the posting rather than a reason to erase it.
    /// </remarks>
    public static Finding AuditOpenness(Opportunity opp, DateTime? now = null)
    {
        var closed = opp.Deadline.HasValue && opp.Deadline.Value < (now ?? DateTime.UtcNow);
        var saysClosed = string.Equals(opp.Status, "Closed", StringComparison.OrdinalIgnoreCase);

        if (closed == saysClosed)
            return new Finding(opp.Id, Verdict.CorrectlyFiled, "opportunity-openness", opp.Status, opp.Status, "The status matches the deadline.");

        if (closed)
            return new Finding(opp.Id, Verdict.Misfiled, "opportunity-openness", opp.Status, "Closed", $"The deadline was {opp.Deadline:d MMMM yyyy} and it is still marked {opp.Status}.");

        // Marked closed with a deadline in the future. Somebody may have closed it on purpose -
        // a withdrawn posting is closed whatever its deadline says - so this is not reopened.
        return new Finding(opp.Id, Verdict.Unclear, "opportunity-openness", opp.Status, opp.Status, "Marked closed although the deadline has not passed. Left as it is: a posting can be withdrawn early.");
    }

    // ── content ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Whether a content item is on the surface its own type and layer put it on.
    /// </summary>
    /// <remarks>
    /// ContentSurface decides whether something belongs to Radar Intelligence or to Research
    /// Discovery, and the two surfaces are queried by that rule. An item matching neither is
    /// reachable from nothing: it is stored, it is served by no screen, and nobody can tell -
    /// which is the quiet half of this, and the reason a report is worth running even on a night
    /// when nothing is moved.
    /// </remarks>
    public static Finding AuditSurface(ContentItem item)
    {
        var intelligence = ContentSurface.IsIntelligence(item);
        var research = ContentSurface.IsResearch(item);

        if (intelligence ^ research)
        {
            var surface = intelligence ? "Intelligence" : "Research";
            return new Finding(item.Id, Verdict.CorrectlyFiled, "content-surface", surface, surface, "Its type and layer put it on one surface.");
        }

        if (intelligence && research)
            return new Finding(item.Id, Verdict.Unclear, "content-surface", $"{item.Type}/{item.Layer}", "one surface", "Its type and layer claim both surfaces, which is a rule that needs fixing rather than a document that needs moving.");

        return new Finding(
            item.Id,
            Verdict.Unclear,
            "content-surface",
            $"{item.Type}/{item.Layer}",
            "a surface that shows it",
            "Stored, and on no surface - so no screen will ever show it.");
    }

    /// <summary>
    /// Whether something filed as news is actually an advert for an opportunity.
    /// </summary>
    /// <remarks>
    /// The one check here that implies moving a document between collections rather than
    /// correcting a field, and so the one held to the highest bar. A news article reporting that
    /// a scholarship exists is news; a posting telling the reader how to apply and by when is an
    /// opportunity, and in the feed it is a thing with a deadline that nothing is tracking.
    ///
    /// Two independent marks are required, not one. "Apply" appears in plenty of ordinary
    /// writing - applying a method, applying pressure - and a single mark would drag reporting
    /// about hiring into the opportunities list, which is the mirror image of the bug this
    /// exists to fix.
    /// </remarks>
    public static Finding AuditIsReallyAnOpportunity(ContentItem item)
    {
        var text = $"{item.Title} {item.WhatHappened} {item.AiSummary}".ToLowerInvariant();

        var marks = AdvertMarks.Count(mark => mark.IsMatch(text));

        if (marks >= 2)
            return new Finding(item.Id, Verdict.Misfiled, "news-that-is-an-advert", "ContentItem", "Opportunity", $"Reads as a posting rather than a report: {marks} marks of an advert.");

        if (marks == 1)
            return new Finding(item.Id, Verdict.Unclear, "news-that-is-an-advert", "ContentItem", "ContentItem", "One mark of an advert, which ordinary writing also produces. Left where it is.");

        return new Finding(item.Id, Verdict.CorrectlyFiled, "news-that-is-an-advert", "ContentItem", "ContentItem", "Reads as a report.");
    }

    /// <summary>
    /// The things a posting says and a report about a posting does not.
    /// </summary>
    /// <remarks>
    /// Each one addresses the reader and asks them to do something by a date. A report says what
    /// happened; an advert says what you must do and when. Bounded as whole words for the reason
    /// everything in this codebase is: "apply" inside "applied mathematics" is not an advert.
    /// </remarks>
    private static readonly Regex[] AdvertMarks =
    [
        new(@"\bapplications?\s+(are\s+)?(now\s+)?open\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"\bapplications?\s+close\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"\bapply\s+(now|here|before|by|online)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"\bdeadline\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"\beligibility\s+(criteria|requirements?)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"\bhow\s+to\s+apply\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"\bclosing\s+date\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"\bsubmit\s+your\s+(application|cv|proposal)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
    ];

    private static string Shorten(string text) =>
        text.Length <= 80 ? text : text[..77].TrimEnd() + "...";
}
