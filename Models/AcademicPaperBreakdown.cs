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

    public int TotalWordCount =>
        (Introduction + " " + Problem + " " + AimAndObjectives + " " + Methodology + " " + KeyArguments + " " + Findings + " " + Contribution)
        .Split(new[] { ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length;
}
