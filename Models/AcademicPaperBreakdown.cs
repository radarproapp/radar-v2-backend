namespace RadarV2.Models;

/// <summary>
/// A research paper broken into the parts a reader actually needs.
/// </summary>
/// <remarks>
/// Lived in the Learn Hub's model file, which is why it moved when the Hub was removed. It was
/// never a Hub type - ContentItem carries one, and Research Discovery is what builds and shows
/// them. Deleting the file wholesale took it with it and broke the build, which is the useful
/// kind of mistake: a shared type filed under one feature is a dependency nobody has written down.
/// </remarks>
public class AcademicPaperBreakdown
{
    public string Introduction { get; set; } = string.Empty;
    public string Problem { get; set; } = string.Empty;
    public string AimAndObjectives { get; set; } = string.Empty;
    public string Methodology { get; set; } = string.Empty;
    public string KeyArguments { get; set; } = string.Empty;
    public string Findings { get; set; } = string.Empty;
    public string Contribution { get; set; } = string.Empty;
    public string WhyItMatters { get; set; } = string.Empty;

    /// <summary>
    /// Where this paper sits in time: Recent, Established, or Foundational.
    ///
    /// Spec 8.4. A 1998 paper can be the foundational text of its field and still be the wrong
    /// thing to copy as current practice. Without this the reader has to notice the year and draw
    /// the conclusion themselves, which is exactly what nobody does while skimming.
    /// </summary>
    public string Era { get; set; } = string.Empty;

    /// <summary>
    /// What the authors say their work cannot do.
    ///
    /// The most useful section of any paper and the one most often dropped. A reader deciding
    /// whether to act on a finding needs to know the sample was 40 undergraduates before they
    /// generalise it to a company. Extracted, never inferred - a paper that states no limitation
    /// gets <see cref="AcademicPaperSynthesizer.NotStated"/> rather than a polite one invented on
    /// its behalf.
    /// </summary>
    public string Limitations { get; set; } = string.Empty;

    /// <summary>What the authors say should be studied next, where they say it.</summary>
    public string FutureWork { get; set; } = string.Empty;

    /// <summary>
    /// What the paper concludes, in a few sentences.
    /// </summary>
    /// <remarks>
    /// Named in the spec alongside introduction, problem, methodology and findings, and the only
    /// one of the five that was not here. Contribution is a different question - what this adds
    /// to the field - and a reader who asked "so what did they conclude?" was being answered
    /// with an argument about significance.
    /// </remarks>
    public string Conclusion { get; set; } = string.Empty;

    /// <summary>
    /// How long the summary runs, which the 500-word target is measured against.
    /// </summary>
    /// <remarks>
    /// The six sections the summary is specified as: introduction, problem statement, objective
    /// of the research, methodology, analysis and findings, conclusion. Those are what the 500
    /// words are for.
    ///
    /// KeyArguments and Contribution used to be counted here. They are supplementary - a
    /// sentence or two each - so counting them let a summary reach its length while the six
    /// sections a reader actually came for were still short.
    /// </remarks>
    public int TotalWordCount => Words(
        Introduction, Problem, AimAndObjectives, Methodology, Findings, Conclusion);

    /// <summary>
    /// How long the introduction runs, which is specified at 30 to 50 words.
    /// </summary>
    /// <remarks>
    /// It is the orientation, not the summary. Left unbounded it grows into a precis of the
    /// whole paper, and the reader then meets the same ground twice.
    /// </remarks>
    public int IntroductionWordCount => Words(Introduction);

    private static int Words(params string[] sections) =>
        string.Join(" ", sections).Split(new[] { ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length;
}
