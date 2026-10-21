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

    [Theory]
    [InlineData(2024, "Recent")]
    [InlineData(2015, "Established")]
    [InlineData(1998, "Foundational")]
    public void A_papers_era_is_stated_so_a_classic_is_not_read_as_current_practice(int year, string expected)
    {
        var paper = Paper("A paper", "We did work.", year);

        AcademicPaperSynthesizer.EnrichPaperWithStrictBreakdown(paper);

        Assert.Contains(expected, paper.PaperBreakdown!.Era);
    }
}
