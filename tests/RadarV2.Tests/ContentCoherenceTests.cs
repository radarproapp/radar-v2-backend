using System.Reflection;
using RadarV2.Models;
using RadarV2.Services.Ingestion;

namespace RadarV2.Tests;

/// <summary>
/// A brief must be about the headline it is printed under.
///
/// Reported from production: a lesson on arithmetic offered "Anthropic IPO numbers surprise on the
/// upside", and the summary beneath it was about South African food security and debt stress.
/// Nothing had been mixed up inside Radar. The item came from a whole-site feed carrying a daily
/// podcast, where the title names one segment and the description is a rundown of the whole
/// episode; Radar summarised the description faithfully and printed it under the headline.
///
/// Section 5 forbids presenting a detail as fact when it cannot be verified, and whose data was
/// wrong first does not change what the card asserts.
/// </summary>
public class ContentCoherenceTests
{
    private static readonly Type ResultType =
        typeof(ContentEnricherService).GetNestedType("EnrichmentResult", BindingFlags.NonPublic)!;

    private static object Result(bool? contentIsAboutTitle)
    {
        var result = Activator.CreateInstance(ResultType)!;

        if (contentIsAboutTitle is not null)
        {
            ResultType.GetProperty("ContentIsAboutTitle")!.SetValue(result, contentIsAboutTitle.Value);
        }

        return result;
    }

    private static bool Describes(object result) =>
        (bool)typeof(ContentEnricherService)
            .GetMethod("Describes", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, [result])!;

    private static void Undescribed(ContentItem item) =>
        typeof(ContentEnricherService)
            .GetMethod("Undescribed", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, [item]);

    /// <summary>
    /// A model that does not answer the question is taken at its word.
    /// </summary>
    /// <remarks>
    /// Defaulting the other way would throw away every brief written by a model that ignored one
    /// field — turning a check for a rare fault into a total outage. The same mistake as treating
    /// an absent judgement as a decision, which has already cost this codebase once.
    /// </remarks>
    [Fact]
    public void AnUnansweredQuestionDoesNotDiscardTheBrief()
    {
        Assert.True(Describes(Result(null)));
    }

    [Fact]
    public void ABriefAboutItsHeadlineIsKept()
    {
        Assert.True(Describes(Result(true)));
    }

    [Fact]
    public void ABriefAboutSomethingElseIsNot()
    {
        Assert.False(Describes(Result(false)));
    }

    /// <summary>
    /// What survives is what the feed actually got right.
    /// </summary>
    /// <remarks>
    /// Title, source and link are real and stay. What goes is every field claiming to say what the
    /// item is about — including the raw description, which is the text that did not match the
    /// headline to begin with. Leaving that in place would keep the exact words this removes, just
    /// unprocessed.
    /// </remarks>
    [Fact]
    public void AMismatchedItemKeepsItsFactsAndLosesItsAccount()
    {
        var item = new ContentItem
        {
            Title = "Anthropic IPO numbers surprise on the upside",
            Source = "Moneyweb",
            Url = "https://www.moneyweb.co.za/moneyweb-podcasts/moneyweb-now/anthropic-ipo/",
            WhatHappened = "New research shows South Africa's food security issues are growing.",
            AiSummary = "New research shows South Africa's food security issues are growing.",
            WhyItMatters = "Food insecurity affects health and stability.",
            Signal = "SA faces food security and debt stress challenges.",
            IsEnriched = true,
        };

        Undescribed(item);

        Assert.Equal("Anthropic IPO numbers surprise on the upside", item.Title);
        Assert.Equal("Moneyweb", item.Source);
        Assert.Contains("moneyweb.co.za", item.Url);

        Assert.Empty(item.WhatHappened);
        Assert.Empty(item.AiSummary);
        Assert.Empty(item.WhyItMatters);
        Assert.Empty(item.Signal);

        Assert.False(item.BodyMatchesTitle);
        Assert.False(item.IsEnriched);
    }

    /// <summary>
    /// Items ingested before the check are not claimed to have passed it.
    /// </summary>
    /// <remarks>
    /// The default is true because nothing has found them to be otherwise — not because anything
    /// looked. Worth being explicit, since the same corpus still holds the reported item.
    /// </remarks>
    [Fact]
    public void AnItemThatWasNeverCheckedDefaultsToTrusted()
    {
        Assert.True(new ContentItem().BodyMatchesTitle);
    }
}
