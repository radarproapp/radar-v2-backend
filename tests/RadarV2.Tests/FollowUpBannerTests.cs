using RadarV2.Models;
using RadarV2.Services;

namespace RadarV2.Tests;

/// <summary>
/// "Radar noticed" has to have noticed something.
///
/// The banner appeared on every visit. Two reasons, and both of them made it furniture rather
/// than an observation: it fired whenever there was a single positive signal of any strength, and
/// its identity was the three terms joined in rank order — an order that changes with almost every
/// interaction — so dismissing it produced a different identity next time and the same message
/// came straight back. Dismissal had never worked for it.
/// </summary>
public class FollowUpBannerTests
{
    private static BehavioralSignal Signal(string term, int positives) => new()
    {
        Term = term,
        Source = SignalSource.Inferred,
        Strength = 0.6,
        PositiveCount = positives,
        NegativeCount = 0,
        UpdatedAt = DateTime.UtcNow,
    };

    private static UserProfile Profile() => new() { Id = "p1", Name = "Ada" };

    private static FollowUpSuggestion? TopThemes(params BehavioralSignal[] signals) =>
        FollowUpService.Suggest(Profile(), signals, signals)
            .FirstOrDefault(s => s.Kind == "topInteractions");

    /// <summary>
    /// One thing opened once is not a habit, and saying so is a claim about the reader.
    /// </summary>
    [Fact]
    public void ASingleEngagementIsNotSomethingRadarNoticed()
    {
        Assert.Null(TopThemes(Signal("Artificial Intelligence", 1)));
    }

    /// <summary>
    /// Nor is one theme, however often. The sentence names three.
    /// </summary>
    [Fact]
    public void OneThemeIsNotTheThingsYouEngageWithMost()
    {
        Assert.Null(TopThemes(Signal("Artificial Intelligence", 9)));
    }

    [Fact]
    public void TwoThemesAreNotEnoughEither()
    {
        Assert.Null(TopThemes(Signal("Artificial Intelligence", 9), Signal("Machine Learning", 7)));
    }

    /// <summary>
    /// Three themes, each returned to, is a pattern worth naming.
    /// </summary>
    [Fact]
    public void ThreeRepeatedThemesAreWorthSaying()
    {
        var banner = TopThemes(
            Signal("Artificial Intelligence", 9),
            Signal("ProductTesting", 6),
            Signal("Machine Learning", 5));

        Assert.NotNull(banner);
        Assert.Equal(3, banner!.Items.Count);
    }

    /// <summary>
    /// Three themes a reader has barely touched are still not a pattern.
    /// </summary>
    [Fact]
    public void ThreeGlancesDoNotCount()
    {
        Assert.Null(TopThemes(
            Signal("Artificial Intelligence", 1),
            Signal("ProductTesting", 2),
            Signal("Machine Learning", 1)));
    }

    /// <summary>
    /// The same three themes are the same banner, whatever order they rank in today.
    /// </summary>
    /// <remarks>
    /// This is what made dismissal useless. The identity was the terms joined in rank order, and
    /// opening one article reorders them — so the banner a reader had just closed came back under
    /// a new name.
    /// </remarks>
    [Fact]
    public void ReorderingTheSameThemesIsStillTheSameBanner()
    {
        var first = TopThemes(
            Signal("Artificial Intelligence", 9),
            Signal("ProductTesting", 6),
            Signal("Machine Learning", 5));

        var afterOneMoreRead = TopThemes(
            Signal("Machine Learning", 11),
            Signal("Artificial Intelligence", 9),
            Signal("ProductTesting", 6));

        Assert.NotNull(first);
        Assert.NotNull(afterOneMoreRead);
        Assert.Equal(first!.Id, afterOneMoreRead!.Id);
    }

    /// <summary>A different set of themes is a different banner, and should be shown.</summary>
    [Fact]
    public void ADifferentSetIsADifferentBanner()
    {
        var first = TopThemes(
            Signal("Artificial Intelligence", 9),
            Signal("ProductTesting", 6),
            Signal("Machine Learning", 5));

        var later = TopThemes(
            Signal("Artificial Intelligence", 9),
            Signal("Climate Finance", 8),
            Signal("Machine Learning", 5));

        Assert.NotEqual(first!.Id, later!.Id);
    }
}
