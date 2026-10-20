using RadarV2.Services;

namespace RadarV2.Tests;

/// <summary>
/// Goals arrive as whole sentences typed by a user and get dropped into titles. Every case here
/// is one that actually shipped and was reported.
/// </summary>
public class SubjectPhraseTests
{
    [Theory]
    // The reported one: rendered as "Foundations of Get Promotion", "Applied Get Promotion",
    // "What Get Promotion actually involves day to day".
    [InlineData("Get Promotion", "Promotion")]
    [InlineData("Find Remote Work", "Remote Work")]
    [InlineData("Earn More", "More")]
    // Article-bearing forms, handled by a separate rule.
    [InlineData("Become a hospital nurse manager", "hospital nurse manager")]
    [InlineData("Get a job in data analytics", "job in data analytics")]
    [InlineData("Work as an analyst", "analyst")]
    // Learning verbs.
    [InlineData("Learn cybersecurity", "cybersecurity")]
    [InlineData("Master Power BI", "Power BI")]
    // Colon form used by the onboarding presets.
    [InlineData("Cybersecurity: Learn New Skills", "Cybersecurity")]
    public void Clean_strips_the_verb_and_keeps_the_subject(string goal, string expected)
    {
        Assert.Equal(expected, SubjectPhrase.Clean(goal));
    }

    [Fact]
    public void Clean_never_returns_a_leading_verb()
    {
        // The failure mode is grammatical: anything starting with one of these reads as broken
        // the moment it lands in "Foundations of ___".
        string[] goals =
        [
            "Get Promotion", "Find Remote Work", "Earn More Money",
            "Build a startup", "Secure funding", "Land a graduate role",
        ];

        foreach (var goal in goals)
        {
            var cleaned = SubjectPhrase.Clean(goal);
            var firstWord = cleaned.Split(' ')[0].ToLowerInvariant();

            Assert.DoesNotContain(firstWord, new[] { "get", "find", "earn", "build", "secure", "land" });
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Clean_falls_back_rather_than_returning_empty(string? goal)
    {
        // An empty subject produces "Foundations of " with nothing after it, which looks broken.
        Assert.False(string.IsNullOrWhiteSpace(SubjectPhrase.Clean(goal)));
    }

    [Fact]
    public void Clean_falls_back_to_the_interest_when_the_goal_is_blank()
    {
        Assert.Equal("Nursing", SubjectPhrase.Clean("", "Nursing"));
    }

    [Fact]
    public void Lower_preserves_acronyms_and_brand_casing()
    {
        // A blanket ToLowerInvariant turned "SOC Analyst" into "soc analyst" and "AI" into "ai"
        // in the middle of generated sentences.
        Assert.Equal("SOC Analyst", SubjectPhrase.Lower("SOC Analyst"));
        Assert.Equal("AI", SubjectPhrase.Lower("AI"));
        Assert.Equal("SQL", SubjectPhrase.Lower("SQL"));
    }

    [Fact]
    public void Lower_lowercases_ordinary_words_for_mid_sentence_use()
    {
        Assert.Equal("cybersecurity", SubjectPhrase.Lower("Cybersecurity"));
    }

    [Fact]
    public void Clean_keeps_subjects_short_enough_for_a_title()
    {
        var long_ = SubjectPhrase.Clean("Become a senior principal distinguished staff engineering manager of platforms");
        Assert.True(long_.Split(' ').Length <= 6, $"Subject too long for a title: '{long_}'");
    }
}
