using RadarV2.Services;
using RadarV2.Models;

namespace RadarV2.Tests;

public class WhyShownRecordTests
{
    private static ContentItem Item() => new() { Topic = "Kubernetes", Source = "The Register" };

    [Theory]
    [InlineData("Learning: container orchestration", "what you're learning next")]
    [InlineData("Role: Platform Engineer", "the role you're aiming for")]
    [InlineData("Gap: networking fundamentals", "something you're still building")]
    public void Record_signals_read_as_plain_english(string signal, string expected)
    {
        // Without a sentence of its own, a new dimension falls through to `_ => phrase` and the
        // card reads "Matches container orchestration" - true, but it does not say why Radar
        // thinks so, which is the whole job of this line.
        var why = WhyShown.Build(Item(), [signal]);

        Assert.Contains(expected, why, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_internal_dimension_name_never_reaches_the_user()
    {
        var why = WhyShown.Build(Item(), ["Learning: container orchestration", "Gap: networking"]);

        Assert.DoesNotContain("Learning:", why, StringComparison.Ordinal);
        Assert.DoesNotContain("Gap:", why, StringComparison.Ordinal);
    }

    [Fact]
    public void A_phrase_matched_on_two_dimensions_is_said_once()
    {
        // A user's target role and their next topic are often the same words.
        var why = WhyShown.Build(Item(), ["Role: platform engineering", "Learning: platform engineering"]);

        var occurrences = why.Split("platform engineering", StringSplitOptions.None).Length - 1;
        Assert.Equal(1, occurrences);
    }

    [Fact]
    public void With_no_signals_it_states_a_fact_rather_than_asserting_a_match()
    {
        var why = WhyShown.Build(Item(), []);

        Assert.Equal("Recent in Kubernetes", why);
    }
}
