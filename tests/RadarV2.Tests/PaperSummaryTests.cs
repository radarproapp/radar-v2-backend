using RadarV2.Models;
using RadarV2.Services;

namespace RadarV2.Tests;

/// <summary>
/// What a research paper's summary has to contain, and what it must not.
/// </summary>
/// <remarks>
/// The summary is the feature. A reader deciding whether a paper is worth two hours needs the
/// five things the spec names — what it is about, the problem, how they did it, what they found,
/// what they conclude — in English they do not need the field to read.
///
/// The prompt asked for a "comprehensive introduction contextualizing the paper within the
/// field" and got exactly that: a second abstract in the same register as the first.
/// </remarks>
public class PaperSummaryTests
{
    private static string PromptSource()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "RadarV2.csproj")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(dir!.FullName, "Services", "MongoResearchService.cs"));
    }

    /// <summary>All five sections the spec names are asked for.</summary>
    [Fact]
    public void TheSummaryAsksForEverySectionTheSpecNames()
    {
        var prompt = PromptSource();

        foreach (var section in new[] { "introduction", "problem", "methodology", "findings", "conclusion" })
        {
            Assert.Contains($"\"{section}\":", prompt);
        }
    }

    /// <summary>
    /// In plain English, asked for in those words.
    /// </summary>
    /// <remarks>
    /// A target nobody states is a target nobody hits. The old prompt asked for "comprehensive"
    /// and "empirical" sections and said nothing about who was reading.
    /// </remarks>
    [Fact]
    public void TheSummaryAsksForPlainEnglishAndAWordCount()
    {
        var prompt = PromptSource();

        Assert.Contains("Very simple English", prompt);
        Assert.Contains("500 words", prompt);
        Assert.Contains("not in its field", prompt);

        // Measured on production: the first summary came back at 185 words against a 500-word
        // target, about 20 a section. A target with no floor and no account of how to reach it
        // honestly is a target the model rounds down to whatever the abstract already said.
        Assert.Contains("no fewer than 70", prompt);
        Assert.Contains("by explaining, not by padding", prompt);

        // The introduction is specified separately, and is the orientation rather than the
        // summary - left on the same budget as the rest it grows into a precis of the whole
        // paper, and the reader meets the same ground twice.
        Assert.Contains("30 to 50 words", prompt);
    }

    /// <summary>
    /// And says what to do when the abstract does not say.
    /// </summary>
    /// <remarks>
    /// The summary is built from a title and an abstract, so every section beyond what those
    /// contain is invention. A paper that does not report its sample size gets a sentence saying
    /// so, rather than a confident paragraph about a number nobody published.
    /// </remarks>
    [Fact]
    public void TheSummaryIsForbiddenFromFillingGaps()
    {
        var prompt = PromptSource();

        Assert.Contains("Do not add anything that is not in the title or abstract", prompt);

        // One wording for "the paper does not say", shared with the template fallback by
        // referencing its constant. Two phrasings reached the same reader on one screen: the
        // model wrote "Not stated in the abstract" and the backfilled sections said "Not clearly
        // stated in the available source", which reads as two different findings.
        // Asserted as the interpolation rather than the phrase: the point is that the prompt
        // references the one constant, so the two can never drift apart again.
        Assert.Contains("{{AcademicPaperSynthesizer.NotStated}}", prompt);
    }

    /// <summary>
    /// The 500 words are counted across every section, including ones added later.
    /// </summary>
    /// <remarks>
    /// Era, Limitations and FutureWork each reached readers empty after being added, because
    /// nothing backfilled them. The word count has the same shape of bug: a section left out of
    /// the sum makes every summary read shorter than it is.
    /// </remarks>
    [Fact]
    public void TheWordCountCountsTheConclusion()
    {
        var withoutConclusion = new AcademicPaperBreakdown { Introduction = "one two three" };
        var withConclusion = new AcademicPaperBreakdown
        {
            Introduction = "one two three",
            Conclusion = "four five",
        };

        Assert.Equal(3, withoutConclusion.TotalWordCount);
        Assert.Equal(5, withConclusion.TotalWordCount);
    }

    /// <summary>
    /// A conclusion is extracted from the paper, or marked as not stated. Never assembled.
    /// </summary>
    [Fact]
    public void AConclusionComesFromThePaperOrIsMarkedMissing()
    {
        var said = new ContentItem
        {
            Title = "Irrigation scheduling in semi-arid smallholder farms",
            WhatHappened =
                "We measured yield across 40 plots over two seasons. Overall, scheduled irrigation "
                + "raised yield by 18 per cent against the control.",
            PaperBreakdown = new AcademicPaperBreakdown { Introduction = "A study of irrigation." },
        };

        AcademicPaperSynthesizer.EnsureDerivedParts(said);
        Assert.Contains("18 per cent", said.PaperBreakdown!.Conclusion);

        var silent = new ContentItem
        {
            Title = "A note on soil salinity",
            WhatHappened = "We describe a method for sampling soil at depth.",
            PaperBreakdown = new AcademicPaperBreakdown { Introduction = "A method note." },
        };

        AcademicPaperSynthesizer.EnsureDerivedParts(silent);
        Assert.Equal(AcademicPaperSynthesizer.NotStated, silent.PaperBreakdown!.Conclusion);
    }
}
