using RadarV2.Models;
using RadarV2.Services;

namespace RadarV2.Tests;

/// <summary>
/// The summariser used to invent scholarship. A paper with no stated methodology was described as
/// using "an empirical, multi-tiered methodological framework combining quantitative modeling,
/// systematic observational sampling, and iterative sensitivity benchmarking"; the aim section did
/// not read the abstract at all and asserted the same three objectives for every paper; and any
/// section that came out short was padded with a fixed sentence to reach a word count.
///
/// A reader cannot tell invented scholarship from real scholarship when both are written in the
/// same register, which is what made this worse than leaving the section blank.
/// </summary>
public class AcademicPaperSynthesizerTests
{
    private static ContentItem Paper(string title, string abstractText, int year = 2024) => new()
    {
        Title = title,
        WhatHappened = abstractText,
        PublishedAt = new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        Journal = "Journal of Testing",
    };

    [Fact]
    public void A_paper_with_no_abstract_says_so_rather_than_inventing_one()
    {
        var paper = Paper("A study of something", string.Empty);

        AcademicPaperSynthesizer.EnrichPaperWithStrictBreakdown(paper);

        Assert.NotNull(paper.PaperBreakdown);
        Assert.Equal(AcademicPaperSynthesizer.NotStated, paper.PaperBreakdown!.Methodology);
        Assert.Equal(AcademicPaperSynthesizer.NotStated, paper.PaperBreakdown.AimAndObjectives);
        Assert.Equal(AcademicPaperSynthesizer.NotStated, paper.PaperBreakdown.Findings);
    }

    [Fact]
    public void Two_different_papers_do_not_get_the_same_summary()
    {
        // The test that would have caught this originally: identical output for unrelated inputs.
        var first = Paper("Zero trust in cloud networks",
            "We evaluate segmentation policies. Our method samples traffic across 12 clusters. Results show a 40% reduction in lateral movement.");
        var second = Paper("Maternal nutrition in rural clinics",
            "We surveyed 300 mothers. The data show iron supplementation improved outcomes in 62% of cases.");

        AcademicPaperSynthesizer.EnrichPaperWithStrictBreakdown(first);
        AcademicPaperSynthesizer.EnrichPaperWithStrictBreakdown(second);

        Assert.NotEqual(first.PaperBreakdown!.Methodology, second.PaperBreakdown!.Methodology);
        Assert.NotEqual(first.PaperBreakdown.Findings, second.PaperBreakdown.Findings);
    }

    [Fact]
    public void Every_word_of_a_section_comes_from_the_source()
    {
        var abstractText = "We propose a segmentation model. Our method samples traffic across twelve clusters.";
        var paper = Paper("Zero trust", abstractText);

        AcademicPaperSynthesizer.EnrichPaperWithStrictBreakdown(paper);

        var methodology = paper.PaperBreakdown!.Methodology;
        Assert.NotEqual(AcademicPaperSynthesizer.NotStated, methodology);
        Assert.Contains(methodology, abstractText);
    }

    [Fact]
    public void Nothing_is_padded_to_reach_a_word_count()
    {
        var paper = Paper("Short", "We propose a model.");

        AcademicPaperSynthesizer.EnrichPaperWithStrictBreakdown(paper);

        var all = string.Join(" ", new[]
        {
            paper.PaperBreakdown!.Introduction, paper.PaperBreakdown.Problem,
            paper.PaperBreakdown.AimAndObjectives, paper.PaperBreakdown.Methodology,
            paper.PaperBreakdown.KeyArguments, paper.PaperBreakdown.Findings,
            paper.PaperBreakdown.Contribution,
        });

        Assert.DoesNotContain("This analytical dimension emphasizes", all);
        Assert.DoesNotContain("longitudinal observation when deploying strategic interventions", all);
    }

    [Fact]
    public void No_section_claims_controls_the_paper_never_mentioned()
    {
        var paper = Paper("A paper", "We looked at some things and wrote them down.");

        AcademicPaperSynthesizer.EnrichPaperWithStrictBreakdown(paper);

        var all = string.Join(" ", new[]
        {
            paper.PaperBreakdown!.Methodology, paper.PaperBreakdown.Contribution,
            paper.PaperBreakdown.KeyArguments,
        });

        Assert.DoesNotContain("counterfactual controls", all);
        Assert.DoesNotContain("reproducible validation metrics", all);
        Assert.DoesNotContain("multi-tiered methodological framework", all);
    }

    [Fact]
    public void A_missing_doi_is_reported_as_missing_not_as_open_access()
    {
        // It used to read "verified DOI: Direct Open Access" - a claim of verification, standing
        // in for the absence of the thing being verified.
        var paper = Paper("A paper", "We did work.");

        AcademicPaperSynthesizer.EnrichPaperWithStrictBreakdown(paper);

        Assert.Contains("No DOI on record", paper.PaperBreakdown!.WhyItMatters);
        Assert.DoesNotContain("verified", paper.PaperBreakdown.WhyItMatters, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A classic is marked as one, and a merely old paper is not promoted into a classic.
    /// </summary>
    /// <remarks>
    /// The era was read off the year alone, so anything past fifteen years was labelled
    /// "Foundational - predates current practice by design" whether or not a soul had cited it.
    /// Most published work is never cited again; telling a student that an ignored 1998 paper is
    /// the text their field rests on is a claim, and a false one. Citations are the only
    /// evidence available that old work is a foundation rather than a dead end, so they are what
    /// decides it - and the threshold is high, because the label is meant to mean something.
    /// </remarks>
    [Theory]
    // Not a literal year. This runs against the real clock, so a hard-coded "recent" year
    // becomes a failing test in 2030 - which is a test that knows the date better than the
    // person who will be debugging it.
    [InlineData(0, 0, "Recent")]
    [InlineData(-8, 0, "Established")]
    [InlineData(-28, 3, "Established")]
    [InlineData(-28, 20_000, "Foundational")]
    public void A_papers_era_is_stated_so_a_classic_is_not_read_as_current_practice(int yearsAgo, int citations, string expected)
    {
        var paper = Paper("A paper", "We did work.", DateTime.UtcNow.Year + yearsAgo);
        paper.CitationCount = citations;

        AcademicPaperSynthesizer.EnrichPaperWithStrictBreakdown(paper);

        Assert.Contains(expected, paper.PaperBreakdown!.Era);
    }

    /// <summary>
    /// The introduction is held to its specified length, not merely asked for it.
    /// </summary>
    /// <remarks>
    /// It is the orientation, not the summary. The template took the first two sentences of the
    /// abstract whole, so a clinical trial with a long title and two long opening sentences
    /// produced 98 words against a 30-to-50 bound - and the reader then met the same ground
    /// again under the five headings that follow.
    /// </remarks>
    [Fact]
    public void The_introduction_is_held_to_fifty_words()
    {
        var longAbstract = string.Join(" ", Enumerable.Repeat(
            "This randomised controlled trial of a community health intervention examined whether "
            + "the approach changed outcomes across a large and varied population over time.", 4));

        var paper = Paper(
            "A very long clinical trial title describing a community health intervention in detail",
            longAbstract);

        AcademicPaperSynthesizer.EnrichPaperWithStrictBreakdown(paper);

        Assert.True(
            paper.PaperBreakdown!.IntroductionWordCount <= AcademicPaperSynthesizer.MostIntroductionWords,
            $"introduction ran to {paper.PaperBreakdown.IntroductionWordCount} words");
    }

    /// <summary>And a cut is visibly a cut, rather than reading as the end of a thought.</summary>
    [Fact]
    public void A_trimmed_introduction_says_it_was_trimmed()
    {
        var longAbstract = string.Join(" ", Enumerable.Repeat("A sentence about the work that keeps going and going.", 6));

        var paper = Paper("A title", longAbstract);

        AcademicPaperSynthesizer.EnrichPaperWithStrictBreakdown(paper);

        if (paper.PaperBreakdown!.IntroductionWordCount == AcademicPaperSynthesizer.MostIntroductionWords)
            Assert.EndsWith("...", paper.PaperBreakdown.Introduction);
    }

    /// <summary>The model's answer is held to the same ceiling as the template's.</summary>
    [Fact]
    public void The_ceiling_applies_to_whoever_wrote_the_introduction()
    {
        var sixty = string.Join(" ", Enumerable.Range(1, 60).Select(i => $"word{i}"));

        var held = AcademicPaperSynthesizer.WithinIntroductionLength(sixty);

        Assert.True(held.Split(' ').Length <= AcademicPaperSynthesizer.MostIntroductionWords + 1);
        Assert.DoesNotContain("word60", held);
    }

    /// <summary>Both producers count the same six sections.</summary>
    /// <remarks>
    /// The template added up its own list - KeyArguments and Contribution, which are
    /// supplementary, and no Conclusion, which is one of the six - so the two paths disagreed
    /// about the length of the same summary by roughly a section.
    /// </remarks>
    [Fact]
    public void The_word_count_is_the_one_the_summary_is_measured_by()
    {
        var paper = Paper("A paper", "We studied a thing. We found a result. We conclude it matters.");

        AcademicPaperSynthesizer.EnrichPaperWithStrictBreakdown(paper);

        Assert.Equal(paper.PaperBreakdown!.TotalWordCount, paper.WordCount);
    }
}
