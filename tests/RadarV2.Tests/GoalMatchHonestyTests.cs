using RadarV2.Models;
using RadarV2.Services;

namespace RadarV2.Tests;

/// <summary>
/// A card may not claim a match it does not have.
///
/// The three items in the first test are real: they are what a Computer Science student was shown
/// under "What matters to you today", each captioned "Matches your goal: Computer Science". The
/// single word "science" scored half of "Computer Science", any score above zero recorded a
/// match, and the explanation then named the whole phrase.
/// </summary>
public class GoalMatchHonestyTests
{
    private static UserProfile Student() => new()
    {
        Id = "u1",
        Persona = PersonaType.Student,
        PrimaryGoal = "Computer Science",
        Interests = ["Computer Science"],
    };

    private static ContentItem Item(string title, string topic = "", string signal = "") => new()
    {
        Id = Guid.NewGuid().ToString(),
        Title = title,
        Topic = topic,
        Signal = signal,
        Source = "Example",
    };

    [Theory]
    [InlineData("Women's body temperature changes may increase autoimmune disease risk", "Science")]
    [InlineData("What Type of Mattress Is Right for You? (2026)", "Ideas")]
    [InlineData("Unlocking the secrets of the Dragon Man skull", "Science")]
    public void DoesNotClaimTheGoalOnSomethingThatMerelySaysScience(string title, string topic)
    {
        var explanation = InterestPersonalization.Explain(Student(), Item(title, topic));

        Assert.DoesNotContain(
            explanation.MatchedSignals,
            signal => signal.StartsWith("Goal:", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void StillMatchesWhenTheWholePhraseIsThere()
    {
        var item = Item("A computer science degree is not what employers test for", "Tech");

        var explanation = InterestPersonalization.Explain(Student(), item);

        Assert.Contains(
            explanation.MatchedSignals,
            signal => signal.StartsWith("Goal:", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void StillMatchesWhenBothWordsAppearApart()
    {
        var item = Item(
            "Science that computer engineers keep rediscovering",
            "Tech",
            "A computer scientist explains the overlap.");

        var explanation = InterestPersonalization.Explain(Student(), item);

        Assert.Contains(
            explanation.MatchedSignals,
            signal => signal.StartsWith("Goal:", StringComparison.OrdinalIgnoreCase));
    }

    // A single-word goal has nothing to be a fraction of, so it is unaffected by the floor.
    [Fact]
    public void ASingleWordGoalStillMatchesOnThatWord()
    {
        var profile = new UserProfile { Id = "u2", PrimaryGoal = "Cybersecurity", Interests = ["Cybersecurity"] };
        var item = Item("Cybersecurity budgets are being cut", "Tech");

        var explanation = InterestPersonalization.Explain(profile, item);

        Assert.Contains(
            explanation.MatchedSignals,
            signal => signal.StartsWith("Goal:", StringComparison.OrdinalIgnoreCase));
    }

    // Two of three is enough for a longer phrase: an article on AI ethics is genuinely about
    // "Artificial Intelligence Ethics" even without all three words.
    [Fact]
    public void ALongerPhraseMatchesOnTwoThirds()
    {
        var profile = new UserProfile
        {
            Id = "u3",
            PrimaryGoal = "Artificial Intelligence Ethics",
            Interests = ["Artificial Intelligence Ethics"],
        };
        var item = Item("The ethics of artificial decision making", "Tech");

        var explanation = InterestPersonalization.Explain(profile, item);

        Assert.Contains(
            explanation.MatchedSignals,
            signal => signal.StartsWith("Goal:", StringComparison.OrdinalIgnoreCase));
    }
}
