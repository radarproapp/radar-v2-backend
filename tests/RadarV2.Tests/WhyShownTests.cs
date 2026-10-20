using RadarV2.Models;
using RadarV2.Services;

namespace RadarV2.Tests;

/// <summary>
/// The sentence under every feed card and home-screen signal. It had two implementations that
/// drifted; these pin the behaviour that was wrong in the client copy.
/// </summary>
public class WhyShownTests
{
    private static ContentItem Item(string topic = "Zero Trust", string source = "Krebs on Security") =>
        new() { Topic = topic, Source = source, Title = "Something happened" };

    [Fact]
    public void Strips_the_internal_dimension_prefix()
    {
        // Signals are stored as "Dimension: Phrase". The client copy printed them raw, producing
        // "your interest in Goal: AI".
        var result = WhyShown.Build(Item(), ["Goal: AI"]);

        Assert.DoesNotContain("Goal:", result[6..]); // allow the deliberate "your goal:" wording
        Assert.Contains("AI", result);
    }

    [Fact]
    public void Does_not_repeat_a_phrase_that_appears_in_two_dimensions()
    {
        // A user's goal domain and stated interest are frequently the same word, which produced
        // "Matches your goal: AI and your interest in AI".
        var result = WhyShown.Build(Item(), ["Goal: AI", "Interest: AI"]);

        var occurrences = result.Split("AI").Length - 1;
        Assert.True(occurrences == 1, $"'AI' appeared {occurrences} times in: {result}");
    }

    [Fact]
    public void Combines_two_distinct_dimensions()
    {
        var result = WhyShown.Build(Item(), ["Goal: AI", "Context: ML Engineer"]);

        Assert.Contains("AI", result);
        Assert.Contains("ML Engineer", result);
    }

    [Fact]
    public void Falls_back_to_recency_rather_than_asserting_a_match()
    {
        // With nothing matched, the honest answer is that the item is recent — not an invented
        // connection to the user.
        var result = WhyShown.Build(Item(topic: "Logistics"), []);

        Assert.Contains("Recent", result);
        Assert.DoesNotContain("Matches", result);
    }

    [Fact]
    public void Falls_back_to_the_source_when_there_is_no_topic()
    {
        var result = WhyShown.Build(Item(topic: "", source: "OpenAlex"), []);

        Assert.Contains("OpenAlex", result);
    }

    [Fact]
    public void Never_returns_empty()
    {
        // An empty string renders as a blank line under the card title, which reads as a bug.
        Assert.False(string.IsNullOrWhiteSpace(WhyShown.Build(Item("", ""), [])));
    }
}
